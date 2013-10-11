using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Dapper;
using Microsoft.Practices.EnterpriseLibrary.WindowsAzure.TransientFaultHandling.SqlAzure;
using Microsoft.WindowsAzure;
using Newtonsoft.Json;
using Quartz;
using Quartz.Impl;
using System.Threading.Tasks;

namespace Smithers.Worker.Jobs.Evaluations
{
    public class ReleaseEvaluations : Job<ReleaseEvaluations>
    {
        private string _connectionString;
        private string _serviceKey;

        public static void Schedule()
        {
            // create job
            var jobDetails = JobBuilder.Create<ReleaseEvaluations>().Build();

            //run daily trigger after inital 30 second delay to give priority to warmup
            var dailyTrigger = TriggerBuilder.Create().ForJob(jobDetails).WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(2, 30).InPacificTimeZone())
                                             .StartAt(DateTimeOffset.Now.AddSeconds(30))
                                             .Build();

            var quickTrigger =
                TriggerBuilder.Create()
                              .ForJob(jobDetails)
                              .WithSchedule(SimpleScheduleBuilder.Create().WithIntervalInMinutes(1).WithRepeatCount(0))
                              .Build();

            var sched = StdSchedulerFactory.GetDefaultScheduler();
            sched.ScheduleJob(jobDetails, quickTrigger);
            //sched.ScheduleJob(jobDetails, dailyTrigger);
            sched.Start();
        }

        public override void ExecuteJob(IJobExecutionContext context)
        {
            _connectionString = CloudConfigurationManager.GetSetting("ace-connection");
            _serviceKey = "TmB1nCT5iQhftXmRI91xw9x4pmvonHP3mxCMITnlVhej7e2LoeJGwzJSg5940bV";

            TermSection[] termSections;
            var serviceCalls = new List<Task<string>>();
            /*
            //Setup connection string
            var connection = new ReliableSqlConnection(_connectionString);
            connection.Open();

            using (connection)
            {
                //Get all sections which are closed but not released
                termSections =
                    connection.Query<TermSection>(@"select distinct s.TermCode, s.Crn from ClassTimeEvaluations cte
                                                        inner join Sections s on s.Id = cte.SectionId where cte.[End] < GETUTCDATE()")
                              .ToArray();
            }
             */
            termSections = new TermSection[]
                {
                    new TermSection {TermCode = "201309", Crn = "90006"},
                    new TermSection {TermCode = "201309", Crn = "90010"}
                };

            if (termSections.Any())
            {
                var terms = termSections.Select(t => t.TermCode).Distinct();

                foreach (var term in terms)
                {
                    var crns = termSections.Where(t => t.TermCode == term).Select(c => c.Crn).ToArray();

                    //Call the student service to determine which of those crns have open grades
                    using (var webClient = new WebClient())
                    {
                        var url = string.Format("{0}?term={1}&crns={2}&key={3}",
                                                "https://test.caes.ucdavis.edu/StudentService/Course/GradesRemaining",
                                                term.ToString(CultureInfo.InvariantCulture),
                                                string.Join("&crns=", crns), _serviceKey);
                        
                        serviceCalls.Add(webClient.DownloadStringTaskAsync(url));
                    }
                }

                //now we have all the service calls generated, wait for them to finish
                Task.WhenAll(serviceCalls).Wait();

                foreach (var serviceCall in serviceCalls)
                {
                    var result = JsonConvert.DeserializeObject<SectionGrade[]>(serviceCall.Result);
                    var releaseable = result.Where(x => x.Gradable == "Y" && x.GradesOutstandingCount == 0);

                    //Release each section with is gradable and has no more outstanding grades
                    foreach (var releaseSection in releaseable)
                    {
                        //TODO: Call some service to release grades
                    }
                }
            }
        }
    }

    public class SectionGrade
    {
        public string TermCode { get; set; }
        public string Crn { get; set; }
        public int GradesOutstandingCount { get; set; }
        public string Gradable { get; set; }
    }
    
    public class TermSection
    {
        public string TermCode { get; set; }
        public string Crn { get; set; }
    }
}