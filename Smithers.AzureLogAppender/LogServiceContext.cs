using System.Linq;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table.DataServices;

namespace Smithers.AzureLogAppender
{
    public class LogServiceContext : TableServiceContext
    {
        public LogServiceContext(CloudTableClient client)
            : base(client)
        {
        }

        internal void Log(LogEntry logEntry)
        {
            AddObject("LogEntries", logEntry);
            SaveChanges();
        }

        public IQueryable<LogEntry> LogEntries
        {
            get
            {
                return CreateQuery<LogEntry>("LogEntries");
            }
        }
    }
}