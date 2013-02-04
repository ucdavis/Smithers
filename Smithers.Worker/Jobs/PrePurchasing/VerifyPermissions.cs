using System;
using Quartz;
using Quartz.Impl;

namespace Smithers.Worker.Jobs.PrePurchasing
{
    public class VerifyPermissions : Job<VerifyPermissions>
    {
        public static void Schedule()
        {
            var jobDetails = JobBuilder.Create<VerifyPermissions>().Build();

            var nightly =
                TriggerBuilder.Create().ForJob(jobDetails).WithSchedule(
                    CronScheduleBuilder.DailyAtHourAndMinute(6, 0)).StartNow().Build();
            var sched = StdSchedulerFactory.GetDefaultScheduler();
            sched.ScheduleJob(jobDetails, nightly);
            sched.Start();
        }

        public override void ExecuteJob(IJobExecutionContext context)
        {
            throw new NotImplementedException();
        }
    }
}
