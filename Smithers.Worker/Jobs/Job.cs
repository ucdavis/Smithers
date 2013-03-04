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
            var start = DateTime.Now;
            Logger.Info("Job Starting");
            
            try
            {
                ExecuteJob(context);

                Logger.InfoFormat("Job Completed. Elapsed time: {0}", (DateTime.Now - start));

                ErrorTracking.ResetFailCount<T>();
            }
            catch (Exception ex)
            {
                ErrorTracking.IncreaseFailCount<T>();
                
                Logger.LogCustomError(ex, notify: ShouldNotifyError());
            }
        }

        /// <summary>
        /// Default to notifying of errors only if the job fails twice consecutively
        /// </summary>
        protected bool ShouldNotifyError()
        {
            return ErrorTracking.GetFailCount<T>() > 1;
        }

        public abstract void ExecuteJob(IJobExecutionContext context);
    }
}