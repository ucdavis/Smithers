using Quartz;
using System;

namespace Smithers.Worker.Jobs
{
    public static class JobExtensions
    {
        static readonly TimeZoneInfo PacificTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        
        public static CronScheduleBuilder InPacificTimeZone(this CronScheduleBuilder builder)
        {
            return builder.InTimeZone(PacificTimeZone);
        }

        public static DateTime InPacificTimeZone(this DateTime dateTime)
        {
            return TimeZoneInfo.ConvertTime(dateTime, PacificTimeZone);
        }
    }
}
