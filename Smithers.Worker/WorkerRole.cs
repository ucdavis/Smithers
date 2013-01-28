using System;
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
using Nancy.Hosting.Self;

namespace Smithers.Worker
{
    public class WorkerRole : RoleEntryPoint
    {
        private ILog _roleLogger;
        private NancyHost _host;

        public override void Run()
        {
            var cancelSource = new CancellationTokenSource();
            _roleLogger.Info("Starting worker role");

            StartWeb();
            //SampleJob.Schedule();

            cancelSource.Token.WaitHandle.WaitOne();
        }

        private void StartWeb()
        {
            _roleLogger.Info("Starting Web Server");

            _host = new NancyHost(new Uri(CloudConfigurationManager.GetSetting("WebUrl")));
            _host.Start();
        }

        public override void OnStop()
        {
            _roleLogger.Info("Worker Role Exiting");
            _host.Stop();

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
