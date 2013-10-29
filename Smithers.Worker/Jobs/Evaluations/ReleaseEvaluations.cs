using System;
using System.Net;
using Microsoft.WindowsAzure;
using Quartz;
using Quartz.Impl;

namespace Smithers.Worker.Jobs.Evaluations
{
    public class ReleaseEvaluations : Job<ReleaseEvaluations>
    {
        private string _serviceKey;
        private string _serviceUrl;

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
            _serviceKey = CloudConfigurationManager.GetSetting("ace-service-key");
            _serviceUrl = CloudConfigurationManager.GetSetting("ace-service-url");

            //Call the student service to release evaluations for the proper courses
            using (var webClient = new WebClient())
            {
                var url = string.Format("{0}/ReleaseEvaluations?key={1}", _serviceUrl, _serviceKey);
                webClient.DownloadStringTaskAsync(url).Wait();
            }
        }
    }
}