using System;
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
    public class ReleaseEvaluations : Job<ReleaseEvaluations>
    {
        private string _connectionString;
        
        public static void Schedule()
        {
            // create job
            var jobDetails = JobBuilder.Create<ReleaseEvaluations>().Build();

            //run daily trigger after inital 30 second delay to give priority to warmup
            var dailyTrigger = TriggerBuilder.Create().ForJob(jobDetails).WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(2, 30).InPacificTimeZone())
                                             .StartAt(DateTimeOffset.Now.AddSeconds(30))
                                             .Build();

            var sched = StdSchedulerFactory.GetDefaultScheduler();
            sched.ScheduleJob(jobDetails, dailyTrigger);
            sched.Start();
        }

        public override void ExecuteJob(IJobExecutionContext context)
        {
            _connectionString = CloudConfigurationManager.GetSetting("ace-connection");

            TermSection[] termSections;

            //Setup connection string
            var connection = new ReliableSqlConnection(_connectionString);
            connection.Open();

            using (connection)
            {
                termSections =
                    connection.Query<TermSection>(@"select distinct s.TermCode, s.Crn from ClassTimeEvaluations cte
                                                        inner join Sections s on s.Id = cte.SectionId where cte.[End] < GETUTCDATE()")
                              .ToArray();
            }
        }
    }
    
    public class TermSection
    {
        public string TermCode { get; set; }
        public string Crn { get; set; }
    }
}