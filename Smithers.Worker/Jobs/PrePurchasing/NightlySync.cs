using System;
using System.Data;
using System.Data.SqlClient;
using Dapper;
using Microsoft.WindowsAzure;
using Quartz;
using Quartz.Impl;

namespace Smithers.Worker.Jobs.PrePurchasing
{
    /// <summary>
    /// Run nightly database sync jobs at 4AM
    /// TODO: change back to 4AM once jobs on prepurchasing are turned off
    /// </summary>
    public class NightlySync : Job<NightlySync>
    {
        private const string OppConnectionKey = "opp-connection";

        public static void Schedule()
        {
            // create job
            var jobDetails = JobBuilder.Create<NightlySync>().Build();

            // create trigger
            var nightly =
                TriggerBuilder.Create().ForJob(jobDetails).WithSchedule(
                    CronScheduleBuilder.DailyAtHourAndMinute(3, 30).InPacificTimeZone()).StartNow().Build();

            // get reference to scheduler (remote or local) and schedule job
            var sched = StdSchedulerFactory.GetDefaultScheduler();
            sched.ScheduleJob(jobDetails, nightly);
            sched.Start();
        }

        public override void ExecuteJob(IJobExecutionContext context)
        {
            var connectionString = CloudConfigurationManager.GetSetting(OppConnectionKey);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Logger.ErrorFormat("Connectiong string not found for key {0}", OppConnectionKey);
                return;
            }
            
            var connection = new SqlConnection(connectionString);
            connection.Open();
            
            using (connection)
            {
                try
                {
                    var rows = connection.Execute("usp_ProcessOrgDescendants", commandType: CommandType.StoredProcedure, commandTimeout: 300);
                    Logger.Info(string.Format("{0} rows affected", rows));
                }
                catch (Exception ex)
                {
                    Logger.LogCustomError(ex, "Org Descendants");
                }

                try
                {
                    var rows = connection.Execute("usp_SyncWorkgroupAccounts", commandType: CommandType.StoredProcedure, commandTimeout: 300);
                    Logger.Info(string.Format("{0} rows affected", rows));
                }
                catch (Exception ex)
                {
                    Logger.LogCustomError(ex, "Sync Workgroup Accounts");
                }
            }
        }
    }
}
