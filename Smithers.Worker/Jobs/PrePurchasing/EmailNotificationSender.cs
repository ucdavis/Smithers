using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using Dapper;
using Microsoft.WindowsAzure;
using SendGridMail;
using SendGridMail.Transport;

namespace Smithers.Worker.Jobs.PrePurchasing
{
    public class EmailNotificationSender
    {
        private static string _sendGridUserName;
        private static string _sendGridPassword;
        private readonly string _connectionString;
        private const string SendGridFrom = "opp-noreply@ucdavis.edu";

        public EmailNotificationSender()
        {
            _connectionString = CloudConfigurationManager.GetSetting("opp-connection");
        }

        public void SendNotifications()
        {
            // always trigger per event emails
            ProcessEmails(EmailPreferences.NotificationTypes.PerEvent);
        }

        public void SendDailyWeeklyNotifications()
        {
            ProcessEmails(EmailPreferences.NotificationTypes.Daily);

            // send weekly summaries
            if (DateTime.Now.DayOfWeek == DayOfWeek.Friday)
            {
                ProcessEmails(EmailPreferences.NotificationTypes.Weekly);
            }
        }

        public void FakeEmailTesting()
        {
            ProcessEmails(EmailPreferences.NotificationTypes.Weekly);
        }

        private void ProcessEmails(EmailPreferences.NotificationTypes notificationType)
        {
            
            var connection = new SqlConnection(_connectionString);
            connection.Open();

            List<dynamic> pending, users;
            using (connection)
            {
                pending =
                    connection.Query(
                        "select * from EmailQueueV2 where Pending = 1 and NotificationType = @notificationType",
                        new {notificationType = notificationType.ToString()}).ToList();

                var pendingUserIds = pending.Where(x => x.UserId != null).Select(x => x.UserId).Distinct();

                users =
                    connection.Query("select distinct * from users where id in @ids",
                                     new {ids = pendingUserIds.ToArray()}).ToList();

                #region Workgroup Notifications have a null User

                var workgroupNotifications = pending.Where(b => b.UserId == null).Select(a => a.Email).Distinct();

                foreach (var wEmail in workgroupNotifications)
                {
                    var pendingForUser = pending.Where(e => e.Email == wEmail).ToList();

                    var email = wEmail;

                    FakeEmail(connection, email, pendingForUser);
                }

                #endregion Workgroup Notifications have a null User

                #region Normal Email Notification, user will never be null

                foreach (var user in users)
                {
                    var pendingForUser = pending.Where(e => e.UserId == user.Id).ToList();

                    var email = user.Email;

                    FakeEmail(connection, email, pendingForUser);
                }

                #endregion Normal Email Notification, user will never be null
            }
        }

        private void FakeEmail(SqlConnection connection, string email, List<dynamic> pendingForUser)
        {
            var pendingOrderIds = pendingForUser.Select(x => x.OrderId).Distinct();

            //Do batches inside of their own transactions
            using (var ts = connection.BeginTransaction())
            {
                var pendingOrders =
                    connection.Query("select * from orders where id in @ids",
                                new { ids = pendingOrderIds.ToArray() }, ts)
                              .ToList();

                var message = new StringBuilder();
                message.Append(string.Format("<p>{0}</p>", "Here is your summary for the PrePurchasing system."));
                foreach (var order in pendingOrders)
                {
                    var extraStyle1 = string.Empty;
                    var extraStyle2 = string.Empty;
                    
                    if (order.OrderStatusCodeId == "OC" || order.OrderStatusCodeId == "OD") //cancelled or denied
                    {
                        extraStyle1 = "<span style=\"color: Red;\">";
                        extraStyle2 = "</span>";
                    }
                    
                    message.Append("<p>");
                    message.Append("<table>");
                    message.Append("<tbody>");
                    message.Append(string.Format("<tr><td style=\"width: 100px;\">Order Request</td><td>{0}</td></tr>", GenerateLink(order.RequestNumber)));
                    message.Append(string.Format("<tr><td style=\"width: 100px;\"><strong>Created By:</strong></td><td>{0}</td></tr>", order.CreatedBy.FullName));
                    message.Append(string.Format("<tr><td style=\"width: 100px;\"><strong>Status:</strong></td><td>{0}{1}{2}</td></tr>", extraStyle1, order.StatusCode.Name, extraStyle2));
                    message.Append(string.Format("<tr><td style=\"width: 100px;\"><strong>Vendor:</strong></td><td>{0}</td></tr>", order.VendorName));
                    
                    message.Append("</tbody>");
                    message.Append("</table>");
                    
                    message.Append("<table border=\"1\">");
                    message.Append("<tbody>");

                    dynamic orderid = order.Id;
                    foreach (var emailQueue in pendingForUser.Where(a => a.OrderId == orderid).OrderByDescending(b => b.DateCreated))
                    {
                        message.Append("<tr>");
                        message.Append(string.Format("<td style=\"padding-left: 7px; border-left-width: 0px; margin-left: 0px; width: 180px;\">{0}</td>", emailQueue.DateTimeCreated));
                        message.Append(string.Format("<td style=\"width: 137px;\">{0}</td>", emailQueue.Action));
                        message.Append(string.Format("<td >{0}</td>", emailQueue.Details));
                        message.Append("</tr>");

                        //TODO: update when sent
                        /*
                        connection.Execute("update EmailQueueV2 set Pending = 0, DateTimeSent = @now where id = @id",
                                           new {now = DateTime.Now, id = emailQueue.Id}, ts);
                         */
                    }

                    message.Append("</tbody>");
                    message.Append("</table>");
                    message.Append("<hr>");
                    message.Append("</p></br>");
                }

                message.Append(string.Format("<p><em>{0} </em><em><a href=\"{1}\">{2}</a>&nbsp;</em></p>", "You can change your email preferences at any time by", "http://prepurchasing.ucdavis.edu/User/Profile", "updating your profile on the PrePurchasing site"));
                
                //TODO: Deliver!!

                ts.Commit();
            }

            //for now, do nothing
        }

        /*
        private void BatchEmail(string email, List<dynamic> pendingForUser)
        {
            _emailQueueV2Repository.DbContext.BeginTransaction();
            var orders = pendingForUser.Select(a => a.Order).Distinct().ToList();

            var message = new StringBuilder();
            message.Append(string.Format("<p>{0}</p>", "Here is your summary for the PrePurchasing system."));
            foreach (var order in orders)
            {
                var extraStyle1 = string.Empty;
                var extraStyle2 = string.Empty;
                if (order.StatusCode.Id == OrderStatusCode.Codes.Cancelled || order.StatusCode.Id == OrderStatusCode.Codes.Denied)
                {
                    extraStyle1 = "<span style=\"color: Red;\">";
                    extraStyle2 = "</span>";
                }
                message.Append("<p>");
                message.Append("<table>");
                message.Append("<tbody>");
                message.Append(string.Format("<tr><td style=\"width: 100px;\">Order Request</td><td>{0}</td></tr>", GenerateLink(order.RequestNumber)));
                message.Append(string.Format("<tr><td style=\"width: 100px;\"><strong>Created By:</strong></td><td>{0}</td></tr>", order.CreatedBy.FullName));
                message.Append(string.Format("<tr><td style=\"width: 100px;\"><strong>Status:</strong></td><td>{0}{1}{2}</td></tr>", extraStyle1, order.StatusCode.Name, extraStyle2));
                message.Append(string.Format("<tr><td style=\"width: 100px;\"><strong>Vendor:</strong></td><td>{0}</td></tr>", order.VendorName));


                message.Append("</tbody>");
                message.Append("</table>");


                message.Append("<table border=\"1\">");
                message.Append("<tbody>");

                foreach (var emailQueue in pendingForUser.Where(a => a.Order == order).OrderByDescending(b => b.DateTimeCreated))
                {
                    message.Append("<tr>");
                    message.Append(string.Format("<td style=\"padding-left: 7px; border-left-width: 0px; margin-left: 0px; width: 180px;\">{0}</td>", emailQueue.DateTimeCreated));
                    message.Append(string.Format("<td style=\"width: 137px;\">{0}</td>", emailQueue.Action));
                    message.Append(string.Format("<td >{0}</td>", emailQueue.Details));
                    message.Append("</tr>");

                    emailQueue.Pending = false;
                    emailQueue.DateTimeSent = DateTime.Now;
                    _emailQueueV2Repository.EnsurePersistent(emailQueue);
                }

                message.Append("</tbody>");
                message.Append("</table>");
                message.Append("<hr>");
                message.Append("</p></br>");
            }

            message.Append(string.Format("<p><em>{0} </em><em><a href=\"{1}\">{2}</a>&nbsp;</em></p>", "You can change your email preferences at any time by", "http://prepurchasing.ucdavis.edu/User/Profile", "updating your profile on the PrePurchasing site"));

            var sgMessage = SendGrid.GenerateInstance();
            sgMessage.From = new MailAddress(SendGridFrom, "UCD PrePurchasing No Reply");

            sgMessage.Subject = orders.Count == 1
                                    ? string.Format("PrePurchasing Notification for Order #{0}",
                                                    orders.Single().RequestNumber)
                                    : "PrePurchasing Notifications";

            sgMessage.AddTo(email);
            sgMessage.Html = message.ToString();

            var transport = SMTP.GenerateInstance(new NetworkCredential(_sendGridUserName, _sendGridPassword));
            transport.Deliver(sgMessage);

            _emailQueueV2Repository.DbContext.CommitTransaction();
        }
        */

        private string GenerateLink(string orderRequestNumber)
        {
            return string.Format("<a href=\"{0}{1}\">{1}</a>", "http://prepurchasing.ucdavis.edu/Order/Lookup/", orderRequestNumber);
        }
    }
}