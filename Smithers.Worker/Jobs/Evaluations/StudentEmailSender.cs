using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Dapper;
using Microsoft.Practices.EnterpriseLibrary.WindowsAzure.TransientFaultHandling.SqlAzure;
using Microsoft.WindowsAzure;
using Quartz;
using Quartz.Impl;
using SendGridMail;
using SendGridMail.Transport;

namespace Smithers.Worker.Jobs.Evaluations
{
    public class StudentEmailSender : Job<StudentEmailSender>
    {
        private string _sendGridUserName;
        private string _sendGridPassword;
        private string _connectionString;
        private const string SendGridFrom = "eval-noreply@ucdavis.edu";
        private const string EvalLink = @"<a href='https://eval.ucdavis.edu'>https://eval.ucdavis.edu</a>";

        public static void Schedule()
        {
            // create job
            var jobDetails = JobBuilder.Create<StudentEmailSender>().Build();

            //run daily trigger after inital 30 second delay to give priority to warmup
            var dailyTrigger = TriggerBuilder.Create().ForJob(jobDetails).WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(8, 0).InPacificTimeZone())
                                             .StartAt(DateTimeOffset.Now.AddSeconds(30))
                                             .Build();

            var sched = StdSchedulerFactory.GetDefaultScheduler();
            sched.ScheduleJob(jobDetails, dailyTrigger);
            sched.Start();
        }

        public override void ExecuteJob(IJobExecutionContext context)
        {
            _connectionString = CloudConfigurationManager.GetSetting("ace-connection");

            var emailList = GetEmailList();

            if (emailList.Any())
            {
                var plainBody = GetPlainTextBody();
                var htmlBody = GetHtmlBody();

                //Setup sendGrid info, so we only look it up once per execution call
                _sendGridUserName = CloudConfigurationManager.GetSetting("ace-sendgrid-username");
                _sendGridPassword = CloudConfigurationManager.GetSetting("ace-sendgrid-password");

                foreach (var email in emailList)
                {
                    try
                    {
                        var sgMessage = SendGrid.GetInstance();
                        var transport = SMTP.GetInstance(new NetworkCredential(_sendGridUserName, _sendGridPassword));
                        sgMessage.From = new MailAddress(SendGridFrom, "UCD Evaluations No Reply");

                        sgMessage.Subject = "UC Davis Course Evaluation Notification";

                        sgMessage.Html = htmlBody;
                        sgMessage.Text = plainBody;
                        
                        sgMessage.To = new[] {new MailAddress(email)};

                        transport.Deliver(sgMessage);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogCustomError(ex, string.Format("ACE Emailing for {0}", email));
                    }
                }
            }
        }

        public void SendWithMailtrap()
        {
            _connectionString = CloudConfigurationManager.GetSetting("ace-connection");

            var emailList = GetEmailList();

            if (emailList.Any())
            {
                var plainBody = GetPlainTextBody();
                var htmlBody = GetHtmlBody();

                using (var smtpClient = new SmtpClient("mailtrap.io", 2525))
                {
                    smtpClient.UseDefaultCredentials = false;
                    smtpClient.Credentials = new NetworkCredential("evaluations-599e350d576fda8b", "5c9eb64e1d499ff8");

                    foreach (var email in emailList)
                    {
                        try
                        {
                            var message = new MailMessage
                            {
                                From = new MailAddress(SendGridFrom),
                                Subject = "UC Davis Course Evaluation Notification",
                                Body = plainBody
                            };

                            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, null, MediaTypeNames.Text.Html));
                            message.To.Add("fake" + email);

                            smtpClient.Send(message);
                        }
                        catch (Exception exception)
                        {
                            Logger.ErrorFormat("Couldn't send email to {0}", exception, email);
                        }
                    }
                }
            }
        }

        public void SendWithBulkmail()
        {
            var certPath = Path.Combine(Environment.GetEnvironmentVariable("RoleRoot") + @"\", @"approot\smithersbot.ucdavis.edu.cer");

            using (var client = new SmtpClient("bulkmail-dev.ucdavis.edu") { UseDefaultCredentials = false })
            {
                client.ClientCertificates.Add(new X509Certificate(certPath, "[]"));
                client.EnableSsl = true;
                client.Port = 587;

                try
                {
                    client.Send("srkirkland@ucdavis.edu", "srkirkland@ucdavis.edu", "bulkmail sample", "sample email");
                }
                catch (Exception ex)
                {
                    Logger.Error("Didn't work", ex);
                }
            }            
        }

        public string[] GetEmailList()
        {
            string[] studentEmailsWithOpenEvaluations;

            //Setup connection string
            var connection = new ReliableSqlConnection(_connectionString);
            connection.Open();

            using (connection)
            {
                studentEmailsWithOpenEvaluations =
                    connection.Query<string>(@"
select distinct s.Email from ClassTimeEvaluations cte
inner join students s on s.ClassTimeId = cte.ClassTimeId
left join CompletedEvaluations ce on ce.ClassTimeEvaluationId = cte.id
where [start] < GETUTCDATE() 
	AND [end] > GETUTCDATE()
	AND ce.Completed is null"
                        ).ToArray();
            }

            return studentEmailsWithOpenEvaluations;
        }

        private static string GetHtmlBody()
        {
            var body = new StringBuilder();

            body.AppendFormat(@"<p>
	Dear UC Davis Student,</p>
<p style='margin-left: 40px;'>
	You have one or more course evaluations ready for you to complete. &nbsp;Please visit the below link for more information and to fill out your evaluations.</p>
<p>
	{0}</p>
<p style='margin-left: 40px;'>
	Thank you for taking part in this important process, your feedback is greatly appreciated,</p>
<p>
	&nbsp;</p>
<p>
	- The UC Davis Course Evaluation System</p>
<hr />
<p>
	Please do not respond to this email directly. &nbsp;For more information or other questions, please visit&nbsp;{0}</p>
", EvalLink);
            return body.ToString();
        }

        private static string GetPlainTextBody()
        {
            return
                string.Format(@"Dear UC Davis Student- You have one or more course evaluations ready for you to view.  
                        Please visit https://eval.ucdavis.edu for more information and to fill out your evaluations.");
        }
    }
}