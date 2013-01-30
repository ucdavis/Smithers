using System;
using Common.Logging;

namespace Smithers.Worker.Jobs
{
    public static class LoggerExtensions
    {
        public static void LogCustomError(this ILog log, Exception ex, string jobName = null)
        {
            if (jobName == null)
            {
                jobName = "Job";
            }

            log.ErrorFormat("Error: {0} failed at {1} with exception {2} {3}", ex, jobName, ex.Source, ex.Message, ex.StackTrace);
        }
    }
}