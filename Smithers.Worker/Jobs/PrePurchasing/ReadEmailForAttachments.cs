﻿using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
using Dapper;
using Microsoft.Practices.EnterpriseLibrary.WindowsAzure.TransientFaultHandling.SqlAzure;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using OpenPop.Mime.Header;
using OpenPop.Pop3;
using Quartz;
using Quartz.Impl;
using SendGridMail;
using SendGridMail.Transport;

namespace Smithers.Worker.Jobs.PrePurchasing
{
    public class ReadEmailForAttachments : Job<ReadEmailForAttachments>
    {
        private string _connectionString;
        private string _sendGridUserName;
        private string _sendGridPassword;
        private const string SendGridFrom = "opp-noreply@ucdavis.edu";

        private string _hostName;
        private int _port;
        private string _userName;
        private string _password;

        public static void Schedule()
        {
            var job = JobBuilder.Create<ReadEmailForAttachments>().Build();

            //run trigger every 30 minutes after inital 2 second delay
            var trigger = TriggerBuilder.Create().ForJob(job)
                            .WithSchedule(SimpleScheduleBuilder.RepeatMinutelyForever(30))
                            .StartAt(DateTimeOffset.Now.AddSeconds(2))
                            .Build();

            var sched = StdSchedulerFactory.GetDefaultScheduler();
            sched.ScheduleJob(job, trigger);
            sched.Start();
        }

        public override void ExecuteJob(IJobExecutionContext context)
        {
            var readEmail = CloudConfigurationManager.GetSetting("opp-read-email");

            //Don't execute unless email is turned on
            if (!string.Equals(readEmail, "Yes", StringComparison.InvariantCultureIgnoreCase)) return;

            //Setup sendGrid info, so we only look it up once per execution call
            _sendGridUserName = CloudConfigurationManager.GetSetting("opp-sendgrid-username");
            _sendGridPassword = CloudConfigurationManager.GetSetting("opp-sendgrid-pass");

            //Setup connection string
            _connectionString = CloudConfigurationManager.GetSetting("opp-connection");

            _hostName = CloudConfigurationManager.GetSetting("opp-pop-host-name");
            _port = Convert.ToInt32(CloudConfigurationManager.GetSetting("opp-pop-port"));
            _userName = CloudConfigurationManager.GetSetting("opp-pop-user-name");
            _password = CloudConfigurationManager.GetSetting("opp-pop-password");

            ReadEmails();

        }


        private void ReadEmails()
        {
            var userNotFound = 0;
            var duplicateEmail = 0;
            var orderNotFound = 0;
            var noAccess = 0;
            var exceptionCount = 0;
            var successCount = 0;


            var connection = new ReliableSqlConnection(_connectionString);
            connection.Open();

            using (connection)
            {


                using (Pop3Client client = new Pop3Client())
                {
                    // Connect to the server
                    client.Connect(_hostName, _port, true);

                    // Authenticate ourselves towards the server
                    client.Authenticate(_userName, _password);

                    // Get the number of messages in the inbox
                    int messageCount = client.GetMessageCount();

                    // Messages are numbered in the interval: [1, messageCount]
                    // Ergo: message numbers are 1-based.
                    // Most servers give the latest message the highest number
                    for (int i = messageCount; i > 0; i--)
                    {
                        MessageHeader headers = client.GetMessageHeaders(i);

                        RfcMailAddress from = headers.From;
                        string subject = headers.Subject;
                        var messageId = headers.MessageId; // Can use to check if we have already processed.
                        var user = connection.Query("select Id, FirstName, LastName from Users where Email = @mailEmail", new { mailEmail = from.Address.ToLower() }).SingleOrDefault();

                        string userId = user == null ? null : user.Id;
                        if (string.IsNullOrWhiteSpace(userId))
                        {
                            //Unique User not Found. Delete message
                            client.DeleteMessage(i);
                            userNotFound++;

                            continue;
                        }
                        if (connection.Query("select Id from Attachments where MessageId = @messageId", new { messageId = messageId}).Any())
                        {
                            //This email was already processed, just delete it.
                            client.DeleteMessage(i);
                            duplicateEmail++;

                            continue;
                        }

                        var order = GetOrder(connection, subject);
                        int? orderId = null;
                        if (order == null)
                        {
                            client.DeleteMessage(i);
                            SendSingleEmail(from.ToString().ToLower(), string.Format("RE: {0}", subject));
                            orderNotFound++;

                            continue;
                        }
                        else
                        {
                            orderId = order.Id;
                        }
                        //OK, we have found a user and an Order.
                        try
                        {
                            if (!HasAccess(connection, userId, orderId.Value, order.OrderStatusCodeId))
                            {
                                NotifyFailure(connection, orderId.Value, userId, "No Access");
                                client.DeleteMessage(i);
                                noAccess++;

                                continue;
                            }
                            var message = client.GetMessage(i);
                            var attachmentFound = false;
                            foreach (var attachment in message.FindAllAttachments())
                            {
                                attachmentFound = true;
                                string contentType = attachment.ContentType.ToString();
                                if (string.IsNullOrWhiteSpace(contentType))
                                {
                                    contentType = "application/octet-stream";
                                }
                                else
                                {
                                    try
                                    {
                                        if (contentType.Contains(";")) //Sometimes contains the name for some reason. We don't want that.
                                        {
                                            contentType = contentType.Split(';')[0];
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        contentType = "application/octet-stream";
                                    }
                                }
                                var dateTime = DateTime.Now.InPacificTimeZone();

                                connection.Execute("insert into Attachments (Id, Filename, ContentType, Contents, OrderId, DateCreated, UserId, Category, MessageId) values (@id, @fileName, @contentType, @contents, @orderId, @dateCreated, @userId, 'Email Attachment', @messageId)"
                                    , new { id = Guid.NewGuid(), fileName = attachment.FileName, contentType = contentType, contents = attachment.Body, orderId = orderId.Value, dateCreated = dateTime, userId = userId, messageId = messageId });

                                string firstName = user.FirstName;
                                string lastName = user.LastName;
                                NotifyUsersAttachmentAdded(connection, orderId.Value, firstName, lastName);
                            }
                            if (!attachmentFound)
                            {
                                NotifyFailure(connection, orderId.Value, userId, "No Attachment Found");
                            }

                        }
                        catch (Exception ex)
                        {
                            //Logger.Info(ex.Message); //We may want to turn this on if we start getting exceptions, for now we just have the log count
                            exceptionCount++;
                            NotifyFailure(connection, orderId.Value, userId, "Error");
                            client.DeleteMessage(i);
                            continue;
                        }

                        successCount++;
                        client.DeleteMessage(i);

                    }

                    if (userNotFound > 0 ||
                        duplicateEmail > 0 ||
                        orderNotFound > 0 ||
                        noAccess > 0 ||
                        exceptionCount > 0 ||
                        successCount > 0)
                    {
                        Logger.Info(string.Format("User Not Found: {0} Duplicate Email: {1} Order Not Found: {2} No Access: {3} Exceptions: {4} Success: {5}", userNotFound, duplicateEmail, orderNotFound, noAccess, exceptionCount, successCount));
                    }

                }
            }
        }

        private bool HasAccess(ReliableSqlConnection connection, string userId, int orderId, string orderStatusCode)
        {
            if (orderStatusCode == "CN" || orderStatusCode == "CP" || orderStatusCode == "OC" || orderStatusCode == "OD")
            {
                if (connection.Query("select id from vClosedAccess where orderid = @orderId and accessuserid = @userId", new {orderId = orderId, userId = userId}).Any())
                {
                    return true;
                }
            }
            else
            {
                if (connection.Query("select [Read] from vOpenAccess where orderid = @orderId and accessuserid = @userId", new { orderId = orderId, userId = userId }).Any())
                {
                    return true;
                }
            }
            return false;
        }

        private void NotifyUsersAttachmentAdded(ReliableSqlConnection connection, int orderId, string firstName, string lastName)
        {
            var users = connection.Query("select distinct UserId from OrderTracking where OrderId = @orderId", new { orderId = orderId }).ToList();

            foreach (var user in users)
            {
                string localUser = user.UserId.ToString();
                var pref = connection.Query("select AddAttachment, NotificationType from EmailPreferences where Id = @id", new { id = localUser }).SingleOrDefault();
                if (pref == null || pref.AddAttachment == true)
                {
                    string localEventType = pref == null
                                                ? EmailPreferences.NotificationTypes.PerEvent.ToString()
                                                : pref.NotificationType;
                    connection.Execute(
                        "insert into EmailQueueV2 (Id, UserId, OrderId, Pending, NotificationType, Action, Details) values (@id, @userId, @orderId, 1, @notificationType, 'Attachment Added', @details)",
                        new { id = Guid.NewGuid(), userId = localUser, orderId = orderId, notificationType = localEventType, details = string.Format("By {0} {1}.", firstName, lastName) });
                }
            }

        }

        private void NotifyFailure(ReliableSqlConnection connection, int orderId, string userId, string error)
        {
            connection.Execute(
                "insert into EmailQueueV2 (Id, UserId, OrderId, Pending, NotificationType, Action, Details) values (@id, @userId, @orderId, 1, @notificationType, @action, 'Unable to add attachment')",
                new { id = Guid.NewGuid(), userId = userId, orderId = orderId, notificationType = EmailPreferences.NotificationTypes.PerEvent.ToString(), action = error });
        }


        private dynamic GetOrder(ReliableSqlConnection connection, string subject)
        {
            string orderRequestNumber;
            try
            {
                orderRequestNumber = subject.Split(' ')[2];
            }
            catch (Exception)
            {
                return null;
            }

            var order = connection.Query("select Id, OrderStatusCodeId from Orders where RequestNumber = @orderRequestNumber", new { orderRequestNumber }).SingleOrDefault();

            return order;
        }

        private void SendSingleEmail(string email, string subject)
        {
            if (RoleEnvironment.IsAvailable && RoleEnvironment.IsEmulated == false)
            {
                var sgMessage = SendGrid.GetInstance();
                sgMessage.From = new MailAddress(SendGridFrom, "UCD PrePurchasing No Reply");
                sgMessage.Subject = subject;
                sgMessage.AddTo(email);
                sgMessage.Html =
                    "<p>You tried to add an attachment by emailing oppattach@ucdavis.edu but the related order was not found. The subject line must be in the exact format:</p><p>Request # xxxx-xxxxxxx</p><p>You may copy this from the Order Review Page</p>";

                var transport = SMTP.GetInstance(new NetworkCredential(_sendGridUserName, _sendGridPassword));
                transport.Deliver(sgMessage);
            }
        }
    }
}
