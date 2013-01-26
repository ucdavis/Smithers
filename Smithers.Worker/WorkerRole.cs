using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using Common.Logging;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage.Table;
using Smithers.Worker.Jobs;
using CloudStorageAccount = Microsoft.WindowsAzure.Storage.CloudStorageAccount;

namespace Smithers.Worker
{
    public class WorkerRole : RoleEntryPoint
    {
        private ILog _roleLogger;
        public override void Run()
        {
            var cancelSource = new CancellationTokenSource();
            _roleLogger.Info("Starting worker role");

            StartWeb();
            SampleJob.Schedule();

            cancelSource.Token.WaitHandle.WaitOne();
        }

        private void StartWeb()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("SmithersStorage"));

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference("LogEntries");

            var query = new TableQuery().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "2013-01"));
            var res = table.ExecuteQuery(query);
            var num = res.Count();
        }

        public override void OnStop()
        {
            _roleLogger.Info("Worker Role Exiting");

            base.OnStop();
        }

        public override bool OnStart()
        {
            _roleLogger = LogManager.GetCurrentClassLogger();

            // Set the maximum number of concurrent connections 
            ServicePointManager.DefaultConnectionLimit = 12;

            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            return base.OnStart();
        }
    }
}
