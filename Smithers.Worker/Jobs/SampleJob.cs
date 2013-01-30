using Quartz;
using System;
using Quartz.Impl;

namespace Smithers.Worker.Jobs
{
    /// <summary>
    /// Sample job
    /// Runs every hour
    /// Works for 2 seconds, logs message, then sleeps for 10 seconds
    /// </summary>
    public class SampleJob : Job<SampleJob>
    {
        public static void Schedule()
        {
            var job = JobBuilder.Create<SampleJob>().Build();

            //run trigger every hour after inital 2 second delay
            var trigger = TriggerBuilder.Create().ForJob(job)
                            .WithSchedule(SimpleScheduleBuilder.RepeatHourlyForever(1))
                            .StartAt(DateTimeOffset.Now.AddSeconds(2))
                            .Build();

            var sched = StdSchedulerFactory.GetDefaultScheduler();
            sched.ScheduleJob(job, trigger);
            sched.Start();
        }

        public override void ExecuteJob(IJobExecutionContext context)
        {
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));
            Logger.Info("Job is doing some heavy work here");
            var zero = 0;
            var oops = 12/zero;
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(10));
        }
    }
}
