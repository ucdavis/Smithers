using System;
using Common.Logging;
using Quartz;

namespace Smithers.Worker.Jobs
{
    public abstract class Job<T> : IJob where T : IJob
    {
        protected readonly ILog Logger;

        protected Job()
        {
            Logger = LogManager.GetLogger<T>();
        }

        public void Execute(IJobExecutionContext context)
        {
            Logger.Info("Job Starting");
            
            try
            {
                ExecuteJob(context);

                Logger.Info("Job Completed");
            }
            catch (Exception ex)
            {
                Logger.ErrorFormat("Error: Job failed at {0} with exception {1} {2}", ex.Source, ex.Message,
                                   ex.StackTrace);
            }
        }

        public abstract void ExecuteJob(IJobExecutionContext context);
    }
}