using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure;
using Quartz;
using Quartz.Impl;

namespace Smithers.Worker.Jobs.PrePurchasing
{
    public class DatabaseBackup : Job<DatabaseBackup>
    {
        public static void Schedule()
        {
            var jobDetails = JobBuilder.Create<DatabaseBackup>().Build();

            var nightly =
                TriggerBuilder.Create()
                              .ForJob(jobDetails)
                              .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(1, 30).InPacificTimeZone())
                              .StartNow()
                              .Build();

            var sched = StdSchedulerFactory.GetDefaultScheduler();
            sched.ScheduleJob(jobDetails, nightly);
            sched.Start();
        }

        public override void ExecuteJob(IJobExecutionContext context)
        {
            var storageAccountName = CloudConfigurationManager.GetSetting("opp-AzureStorageAccountName");
            var serverName = CloudConfigurationManager.GetSetting("opp-AzureServerName");
            var username = CloudConfigurationManager.GetSetting("opp-AzureUserName");
            var password = CloudConfigurationManager.GetSetting("opp-AzurePassword");
            var storageKey = CloudConfigurationManager.GetSetting("opp-AzureStorageKey");
            var blobContainer = CloudConfigurationManager.GetSetting("opp-AzureBlobContainer");

            // initialize the service
            var azureService = new AzureStorageService(serverName, username, password, storageAccountName, storageKey, blobContainer);

            // make the commands for backup
            string filename;
            var reqId = azureService.BackupDataSync("PrePurchasing", out filename);

            Logger.InfoFormat("PrePurchasing database backup completed w/ requestId {0} and filename {1}", reqId, filename);
            
            // clean up the blob
            azureService.BlobCleanup();
        }
    }
}
