using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Nancy;
using System.Dynamic;

namespace Smithers.Worker
{
    public class LogViewerModule : NancyModule
    {
        private const string CasUrl = "https://cas.ucdavis.edu:8443/cas/";
        
        public LogViewerModule()
        {
            Get["/"] = _ =>
                {
                    var user = GetUser();
                    
                    if (string.IsNullOrWhiteSpace(user)) //if user isn't logged in, authenticate
                    {
                        return Response.AsRedirect(CasUrl + "login?service=" + Context.Request.Url.SiteBase);
                    }
                    
                    if (!HasAccess(user))
                    {
                        return Nancy.HttpStatusCode.Forbidden;
                    }

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

        private bool HasAccess(string user)
        {
            var allowed = CloudConfigurationManager.GetSetting("AllowedUsers");

            return true;
            //return allowed != null && allowed.Split(';').Contains(user);
        }

        private string GetUser()
        {
            // get ticket & service
            string ticket = Context.Request.Query.ticket;
            string service = Context.Request.Url.SiteBase;

            // if ticket is defined then we assume they are coming from CAS
            if (!string.IsNullOrEmpty(ticket))
            {
                // validate ticket against cas
                var sr = new StreamReader(new WebClient().OpenRead(CasUrl + "validate?ticket=" + ticket + "&service=" + service));
                Context.Request.Query.ticket = null;

                // parse text file
                if (sr.ReadLine() == "yes")
                {
                    return sr.ReadLine();
                }
            }

            return string.Empty;
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
