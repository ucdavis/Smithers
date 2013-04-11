using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Practices.EnterpriseLibrary.WindowsAzure.TransientFaultHandling.SqlAzure;
using Microsoft.WindowsAzure;
using OpenPop.Mime.Header;
using OpenPop.Pop3;
using Quartz;
using Quartz.Impl;
using Dapper;
using OpenPop;
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
        private static readonly List<string> ClosedOrders = new List<string>{"CN", "CP", "OC", "OD"};

        private string _hostName;
        private int _port;
        private string _userName;
        private string _password;

        public static void Schedule()
        {
            var job = JobBuilder.Create<ReadEmailForAttachments>().Build();

            //run trigger every 15 minutes after inital 2 second delay
            var trigger = TriggerBuilder.Create().ForJob(job)
                            .WithSchedule(SimpleScheduleBuilder.RepeatMinutelyForever(15))
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

            //System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));
            //Logger.Info("Job is doing some heavy work here");
            //System.Threading.Thread.Sleep(TimeSpan.FromSeconds(10));
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

                        var userId = user == null ? null : user.Id;
                        if (string.IsNullOrWhiteSpace(userId))
                        {
                            //Unique User not Found. Delete message
                            client.DeleteMessage(i);
                            userNotFound++;
                            //Logger.Info(string.Format("User not found: {0}", from.Address.ToLower()));

                            continue;
                        }
                        if (connection.Query("select Id from Attachments where MessageId = @messageId", new { messageId = messageId}).Any())
                            //TODO: The MessageId field 
                        {
                            //This email was already processed, just delete it.
                            client.DeleteMessage(i);
                            duplicateEmail++;
                            //Logger.Info(string.Format("Duplicate Email Message Id: {0}", messageId));

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
                                Logger.Info(string.Format("Debugging Info {0}", 7));
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
                                var contentType = attachment.ContentType.ToString();
                                if (string.IsNullOrWhiteSpace(contentType))
                                {
                                    contentType = "application/octet-stream";
                                }
                                var dateTime = DateTime.UtcNow;

                                Logger.Info(string.Format("Debugging Info {0} -- {1} - {2} - {3} - {4} - {5}", 8, attachment.FileName, contentType, orderId.Value, userId, messageId));
                                connection.Execute("insert into Attachments (Id, Filename, ContentType, Contents, OrderId, DateCreated, UserId, Category, MessageId) values (@id, @fileName, @contentType, @contents, @orderId, @dateCreated, @userId, 'Email Attachment', @messageId)"
                                    , new { id = Guid.NewGuid(), fileName = "test", contentType = "application/octet-stream", contents = attachment.Body, orderId = 17542, dateCreated = dateTime, userId = "jsylvest", messageId = "test" });
                                Logger.Info("Debugging Info 8A");
                                NotifyUsersAttachmentAdded(connection, orderId.Value, user);
                            }

                        }
                        catch (Exception ex)
                        {
                            throw ex;
                            Logger.Info(ex.Message);
                            //TODO: Log exception?
                            exceptionCount++;
                            Logger.Info(string.Format("Debugging Info {0}", 9));
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

        private bool HasAccess(ReliableSqlConnection connection, object userId, int orderId, string orderStatusCode)
        {
            Logger.Info(string.Format("Debugging Info {0} - {1}", 1, orderStatusCode));
            //if (ClosedOrders.Contains(orderStatusCode))
            if (orderStatusCode == "CN" || orderStatusCode == "CP" || orderStatusCode == "OC" || orderStatusCode == "OD")
            {
                Logger.Info(string.Format("Debugging Info {0}", 2));
                if (connection.Query("select id from vClosedAccess where orderid = @orderId and accessuserid = @userId", new {orderId = orderId, userId = userId.ToString()}).Any())
                {
                    Logger.Info(string.Format("Debugging Info {0}", 3));
                    return true;
                }
            }
            else
            {
                Logger.Info(string.Format("Debugging Info {0}", 4));
                if (connection.Query("select Read from vOpenAccess where orderid = @orderId and accessuserid = @userId", new { orderId = orderId, userId = userId.ToString() }).Any())
                {
                    Logger.Info(string.Format("Debugging Info {0}", 5));
                    return true;
                }
            }
            Logger.Info(string.Format("Debugging Info {0}", 6));
            return false;
        }

        private void NotifyUsersAttachmentAdded(ReliableSqlConnection connection, int orderId, dynamic actor)
        {
            var users = connection.Query("select distinct UserId from OrderTracking where OrderId = @orderId", new { orderId = orderId }).ToList();

            foreach (var user in users)
            {
                var pref = connection.Query("select AddAttachment, NotificationType from EmailPreferences where UserId = @userId", new { userId = user.ToString() }).SingleOrDefault();
                if (pref == null || pref.AddAttachment == 1)
                {
                    //var id = Guid.NewGuid().ToString(); //DO I need this? I don't see a default value in the table.

                    connection.Execute(
                        "insert into EmailQueueV2 (Id, UserId, OrderId, Pending, NotificationType, Action, Details) values (@id, @userId, @orderId, 1, @notificationType, 'Attachment Added', @details)",
                        new { id = Guid.NewGuid(), userId = user.ToString(), orderId = orderId, notificationType = pref == null ? EmailPreferences.NotificationTypes.PerEvent.ToString() : pref.NotificationType, details = string.Format("By {0} {1}.", actor.FirstName, actor.Lastname) });
                }
            }

        }

        private void NotifyFailure(ReliableSqlConnection connection, int orderId, object userId, string error)
        {
            Logger.Info(string.Format("Debugging Info {0}", 10));
           // var id = Guid.NewGuid().ToString(); //DO I need this? I don't see a default value in the table.
            Logger.Info(string.Format("Debugging Info {0}", 11));
            connection.Execute(
                "insert into EmailQueueV2 (Id, UserId, OrderId, Pending, NotificationType, Action, Details) values (@id, @userId, @orderId, 1, @notificationType, @action, 'Unable to add attachment')",
                new { id = Guid.NewGuid(), userId = userId.ToString(), orderId = orderId, notificationType = EmailPreferences.NotificationTypes.PerEvent.ToString(), action = error });
            Logger.Info(string.Format("Debugging Info {0}", 12));
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
            var sgMessage = SendGrid.GetInstance();
            sgMessage.From = new MailAddress(SendGridFrom, "UCD PrePurchasing No Reply");
            sgMessage.Subject = subject;
            sgMessage.AddTo(email);
            sgMessage.Html = "<p>You tried to add an attachment by emailing oppattach@ucdavis.edu but the related order was not found. The subject line must be in the exact format:</p><p>Request # xxxx-xxxxxxx</p><p>You may copy this from the Order Review Page</p>";

            var transport = SMTP.GetInstance(new NetworkCredential(_sendGridUserName, _sendGridPassword));
            transport.Deliver(sgMessage);   
        }
    }
}
