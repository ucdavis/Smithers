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
            ExecuteJob(context);
            Logger.Info("Job Completed");
        }

        public abstract void ExecuteJob(IJobExecutionContext context);
    }
}