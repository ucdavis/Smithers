using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Mail;
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
            var sendEmail = "Yes"; //CloudConfigurationManager.GetSetting("opp-send-email");
            
            //Don't execute unless email is turned on
            if (!string.Equals(sendEmail, "Yes", StringComparison.InvariantCultureIgnoreCase)) return;
            
            //Setup sendGrid info, so we only look it up once per execution call
            _sendGridUserName = "azure_ed741bc49fa79b0d32c8d660fb015d3a@azure.com";
            _sendGridPassword = "fake";

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

        public string[] EmailList()
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
    }
}