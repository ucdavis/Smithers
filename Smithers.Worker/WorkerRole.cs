using System;
using System.Net;
using System.Threading;
using Common.Logging;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using Nancy.Hosting.Self;
using Quartz.Impl;
using Smithers.Worker.Jobs;
using Smithers.Worker.Jobs.PrePurchasing;

namespace Smithers.Worker
{
    public class WorkerRole : RoleEntryPoint
    {
        private ILog _roleLogger;
        private NancyHost _host;

        private static readonly TimeSpan MaxExitWaitTime = TimeSpan.FromMinutes(2);

        public override void Run()
        {
            var cancelSource = new CancellationTokenSource();
            _roleLogger.Info("Starting worker role");

            StartWeb();
         
            //Only run production jobs if we are running in Azure
            if (RoleEnvironment.IsAvailable && RoleEnvironment.IsEmulated == false)
            {
                //PrePurchasing
                NightlySync.Schedule();
                DatabaseBackup.Schedule();
                EmailNotificationSender.Schedule();
            }
            else //local debugging
            {
                SampleJob.Schedule();    
            }

            cancelSource.Token.WaitHandle.WaitOne();
        }

        private void StartWeb()
        {
            _host = new NancyHost(new Uri(CloudConfigurationManager.GetSetting("WebUrl")));
            _host.Start();
        }

        public override void OnStop()
        {
            _roleLogger.Info("Worker Role Exiting");
            _host.Stop();

            var scheduler = StdSchedulerFactory.GetDefaultScheduler();
            var executingJobs = scheduler.GetCurrentlyExecutingJobs();

            if (executingJobs.Count > 0)
            {
                //If we have any currently executing jobs, pause new ones from being triggered and wait for them to finish
                _roleLogger.InfoFormat("Waiting for {0} job(s) to shut down before exiting", executingJobs.Count);
                scheduler.PauseAll();

                TimeSpan loopDuration = TimeSpan.FromSeconds(20); //check for running jobs every 20 seconds
                
                for (TimeSpan time = TimeSpan.FromSeconds(0); time < MaxExitWaitTime; time += loopDuration)
                {
                    if (scheduler.GetCurrentlyExecutingJobs().Count == 0)
                    {
                        break;
                    }

                    Thread.Sleep(loopDuration);
                }

                executingJobs = scheduler.GetCurrentlyExecutingJobs();

                if (executingJobs.Count > 0) //if there are still executing jobs that still haven't finished Azure will shut them down hard
                {
                    foreach (var job in executingJobs)
                    {
                        var jobLogger = LogManager.GetLogger(job.JobDetail.JobType);
                        
                        jobLogger.Error("Job hard shutdown forced by worker role restart");
                    }
                }
            }
            
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
