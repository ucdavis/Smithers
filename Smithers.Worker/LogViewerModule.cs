using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Nancy;
using System.Linq;

namespace Smithers.Worker
{
    public class LogViewerModule : NancyModule
    {
        public LogViewerModule()
        {
            Get["/"] = _ =>
                {
                    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("SmithersStorage"));

                    // Create the table client.
                    CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                    CloudTable table = tableClient.GetTableReference("LogEntries");

                    var query = new TableQuery().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "2013-01"));
                    var res = table.ExecuteQuery(query);
                    var num = res.Count();

                    return View["logviewer.html"];
                };
        }
    }
}
