using Quartz;
using System;

namespace Smithers.Worker.Jobs
{
    public static class JobExtensions
    {
        public static CronScheduleBuilder InPacificTimeZone(this CronScheduleBuilder builder)
        {
            var pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

            return builder.InTimeZone(pacific);
        }
    }
}
