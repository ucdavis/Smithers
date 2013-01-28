using System;
using System.Data.Services.Client;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using log4net.Appender;
using log4net.Core;

namespace Smithers.AzureLogAppender
{
    public class AzureTableStorageAppender : AppenderSkeleton
    {
        public string TableStorageConnectionStringName { get; set; }
        private CloudTable _table;
        private string _tableEndpoint;

        public override void ActivateOptions()
        {
            base.ActivateOptions();

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting(TableStorageConnectionStringName));

            // Create the table client.
            var tableClient = storageAccount.CreateCloudTableClient();

            _table = tableClient.GetTableReference("LogEntries");
            _table.CreateIfNotExists();

            _tableEndpoint = storageAccount.TableEndpoint.AbsoluteUri;
        }

        protected override void Append(LoggingEvent loggingEvent)
        {
            Action doWriteToLog = () =>
                {
                    try
                    {
                        var log = new LogEntry
                            {
                                RoleInstance = RoleEnvironment.CurrentRoleInstance.Id,
                                DeploymentId = RoleEnvironment.DeploymentId,
                                Timestamp = loggingEvent.TimeStamp,
                                Message = loggingEvent.RenderedMessage,
                                Level = loggingEvent.Level.Name,
                                LoggerName = loggingEvent.LoggerName,
                                Domain = loggingEvent.Domain,
                                ThreadName = loggingEvent.ThreadName,
                                Identity = loggingEvent.Identity
                            };

                        TableOperation insertOperation = TableOperation.Insert(log);
                        _table.Execute(insertOperation);
                    }
                    catch (DataServiceRequestException e)
                    {
                        ErrorHandler.Error(string.Format("{0}: Could not write log entry to {1}: {2}",
                                                         GetType().AssemblyQualifiedName, _tableEndpoint, e.Message));
                    }
                };
            doWriteToLog.BeginInvoke(null, null);
        }
    }
}