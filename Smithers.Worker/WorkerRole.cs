using System.Diagnostics;
using System.Net;
using System.Threading;
using Common.Logging;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace Smithers.Worker
{
    public class WorkerRole : RoleEntryPoint
    {
        private ILog _roleLogger;
        public override void Run()
        {
            var cancelSource = new CancellationTokenSource();
            _roleLogger.Info("Starting worker role");
            
            cancelSource.Token.WaitHandle.WaitOne();
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
