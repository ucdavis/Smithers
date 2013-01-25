using System.Diagnostics;
using System.Net;
using System.Threading;
using Common.Logging;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace Smithers.Worker
{
    public class WorkerRole : RoleEntryPoint
    {
        public override void Run()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var cancelSource = new CancellationTokenSource();
            
            logger.Info("Starting worker role");
            
            cancelSource.Token.WaitHandle.WaitOne();
        }

        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections 
            ServicePointManager.DefaultConnectionLimit = 12;

            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            return base.OnStart();
        }
    }
}
