using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
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

        public static void Schedule()
        {
            // create job
            var jobDetails = JobBuilder.Create<StudentEmailSender>().Build();

            var quick =
                TriggerBuilder.Create().ForJob(jobDetails).WithSchedule(SimpleScheduleBuilder.RepeatMinutelyForever(10))
                              .Build();

            //run daily trigger after inital 30 second delay to give priority to warmup
            var dailyTrigger = TriggerBuilder.Create().ForJob(jobDetails).WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(16, 30).InPacificTimeZone())
                                             .StartAt(DateTimeOffset.Now.AddSeconds(30))
                                             .Build();

            var sched = StdSchedulerFactory.GetDefaultScheduler();
            sched.ScheduleJob(jobDetails, quick);
            //sched.ScheduleJob(dailyTrigger);
            sched.Start();
        }

        public override void ExecuteJob(IJobExecutionContext context)
        {
            _connectionString = CloudConfigurationManager.GetSetting("ace-connection");

            var body = GetEmailBody();
            var emailList = GetEmailList();

            if (emailList.Any())
            {
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
                                    IsBodyHtml = true,
                                    Body = body
                                };

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

        public void SendWithSendgrid()
        {
            var sendEmail = "Yes"; //CloudConfigurationManager.GetSetting("opp-send-email");

            //Don't execute unless email is turned on
            if (!string.Equals(sendEmail, "Yes", StringComparison.InvariantCultureIgnoreCase)) return;

            //Setup sendGrid info, so we only look it up once per execution call
            _sendGridUserName = "azure_ed741bc49fa79b0d32c8d660fb015d3a@azure.com";
            _sendGridPassword = "[]";

            //var studentsToEmail = EmailList();

            //if (studentsToEmail.Length > 0)
            //{

            //}

            var sgMessage = SendGrid.GetInstance();
            var transport = SMTP.GetInstance(new NetworkCredential(_sendGridUserName, _sendGridPassword));
            sgMessage.From = new MailAddress(SendGridFrom, "UCD Evaluations No Reply");

            sgMessage.Subject = "UC Davis Course Evaluation Notification";

            sgMessage.Html = string.Format("<a href=\"{0}\">{0}</a>", "https://eval.ucdavis.edu");
            sgMessage.Text =
                @"Dear UC Davis Student- You have one or more course evaluations ready for you to view.  
                        Please visit https://eval.ucdavis.edu for more information and to fill out your evaluations.";

            for (int i = 0; i < 1000; i++)
            {
                try
                {
                    sgMessage.To = new[] {new MailAddress(string.Format("student{0}@mailinator.com", i))};

                    transport.Deliver(sgMessage);
                }
                catch (Exception ex)
                {
                    Logger.Error(string.Format("Failed sending to {0}", "fakeemail@mailinator.com"), ex);
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

        private static string GetEmailBody()
        {
            const string link = @"<a href='https://eval.ucdavis.edu'>https://eval.ucdavis.edu</a>";
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
", link);
            return body.ToString();
        }
    }
}