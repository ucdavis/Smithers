using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Quartz;
using Quartz.Impl;

namespace Smithers.Worker.Jobs.PrePurchasing
{
    public class DatabaseBackup : Job<DatabaseBackup>
    {
        public static void Schedule()
        {
            var jobDetails = JobBuilder.Create<DatabaseBackup>().Build();

            var nightly = TriggerBuilder.Create().ForJob(jobDetails).WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(1, 0)).StartNow().Build();
            var sched = StdSchedulerFactory.GetDefaultScheduler();
            sched.ScheduleJob(jobDetails, nightly);
            sched.Start();
        }

        public override void ExecuteJob(IJobExecutionContext context)
        {
            var storageAccountName = context.MergedJobDataMap["storageAccountName"] as string;
            var serverName = context.MergedJobDataMap["serverName"] as string;
            var username = context.MergedJobDataMap["username"] as string;
            var password = context.MergedJobDataMap["password"] as string;
            var storageKey = context.MergedJobDataMap["storageKey"] as string;
            var blobContainer = context.MergedJobDataMap["blobContainer"] as string;

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
