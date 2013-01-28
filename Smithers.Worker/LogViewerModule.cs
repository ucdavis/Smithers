using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Nancy;
using System.Dynamic;

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

                    var now = DateTime.Now;

                    var query =
                        new TableQuery().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal,
                                                                                  string.Format("{0}-{1:D2}", now.Year,
                                                                                                now.Month)));
                    var res = table.ExecuteQuery(query);
                    
                    dynamic model = new ExpandoObject();
                    model.Events = res.Select(
                        logEvent => new LogInfo
                            {
                                LoggerName = logEvent.Properties["LoggerName"].StringValue,
                                Timestamp = logEvent.Timestamp.ToLocalTime().ToString("MM/dd/yy H:mm:ss"),
                                Message = logEvent.Properties["Message"].StringValue,
                                RoleInstance = logEvent.Properties["RoleInstance"].StringValue,
                            }).ToList();

                    return View["logviewer.html", model];
                };
        }
    }

    public class LogInfo
    {
        public string LoggerName { get; set; }
        public string Timestamp { get; set; }
        public string Message { get; set; }
        public string RoleInstance { get; set; }
    }
}
