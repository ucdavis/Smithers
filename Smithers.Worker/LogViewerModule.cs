﻿using System;
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
                    var lastMonth = now.AddMonths(-1);

                    var filterCurrent = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal,
                                                                           string.Format("{0}-{1:D2}", now.Year,
                                                                                         now.Month));

                    var filterLast = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal,
                                                                           string.Format("{0}-{1:D2}", lastMonth.Year,
                                                                                         lastMonth.Month));

                    var query =
                        new TableQuery().Where(
                            TableQuery.CombineFilters(filterCurrent, TableOperators.Or, filterLast)
                            );

                    var res = table.ExecuteQuery(query);
                    
                    
                    dynamic model = new ExpandoObject();
                    model.Events = res.Select(
                        logEvent => new LogInfo
                            {
                                LoggerName = logEvent.Properties["LoggerName"].StringValue,
                                Timestamp = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(logEvent.Timestamp, "Pacific Standard Time").ToString("MM/dd/yy H:mm:ss"),
                                Message = logEvent.Properties["Message"].StringValue,
                                Level = logEvent.Properties["Level"].StringValue,
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
        public string Level { get; set; }
    }
}
