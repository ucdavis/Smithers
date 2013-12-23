using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography.X509Certificates;
using System.Web.Security;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Nancy;
using System.Dynamic;
using Nancy.Cookies;
using Nancy.Responses;
using log4net.Repository.Hierarchy;

namespace Smithers.Worker
{
    public class LogViewerModule : NancyModule
    {
        private const string CasUrl = "https://cas.ucdavis.edu:8443/cas/";
        private const string UserTokenKey = "smithers.userToken";

        public LogViewerModule()
        {
            Get["/config"] = _ => CloudConfigurationManager.GetSetting("WebUrl");
            
            Get["/email"] = _ =>
                {
                    var certPath = Path.Combine(Environment.GetEnvironmentVariable("RoleRoot") + @"\",
                                                @"approot\smithersbot.ucdavis.edu.cer");

                    var cert = new X509Certificate(certPath, CloudConfigurationManager.GetSetting("certificate-key"));
                    
                    using (var client = new SmtpClient("bulkmail-dev.ucdavis.edu") {UseDefaultCredentials = false})
                    {
                        client.ClientCertificates.Add(cert);
                        client.EnableSsl = true;
                        client.Port = 587;

                        try
                        {
                            client.Send("srkirkland@ucdavis.edu", "srkirkland@ucdavis.edu", "bulkmail sample",
                                        "sample email");
                            return "Email sent";
                        }
                        catch (Exception ex)
                        {
                            return string.Format("Email failed because: {0}, with certificate subject {1}", ex.Message,
                                                 cert.Subject);
                        }
                    }
                };

            Get["/auth"] = _ =>
                {
                    var user = GetUser();

                    if (string.IsNullOrWhiteSpace(user)) //if user isn't logged in, authenticate
                    {
                        return Response.AsRedirect(CasUrl + "login?service=" + Context.Request.Url.SiteBase + "/auth");
                    }
                    else
                    {
                        var response = Response.AsRedirect("/", RedirectResponse.RedirectType.Temporary);

                        //place the user in a forms ticket, encrypt it, and then create a nancy cookie from it
                        var ticket = new FormsAuthenticationTicket(user, true, TimeSpan.FromDays(30).Minutes);
                        
                        var cookie = new NancyCookie(UserTokenKey, FormsAuthentication.Encrypt(ticket), true)
                        {
                            Expires = DateTime.Now + TimeSpan.FromDays(30)
                        };
                        
                        response.AddCookie(cookie);

                        return response;
                    }
                };

            Get["/"] = _ =>
                {
                    var user = ProcessUserCookie();
                    
                    if (string.IsNullOrWhiteSpace(user)) //if user isn't logged in, authenticate
                    {
                        return Response.AsRedirect(CasUrl + "login?service=" + Context.Request.Url.SiteBase + "/auth");
                    }
                    
                    if (!HasAccess(user))
                    {
                        return Nancy.HttpStatusCode.Forbidden;
                    }

                    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("SmithersStorage"));

                    // Create the table client.
                    CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                    CloudTable table = tableClient.GetTableReference("LogEntries");

                    var filterLevel = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, Request.Query.level.HasValue ? Request.Query.level.Value as string : "ERROR");
                    var filterCurrent = TableQuery.GenerateFilterConditionForDate("Timestamp",
                                                                                  QueryComparisons.GreaterThanOrEqual,
                                                                                  DateTime.UtcNow.AddHours(-1 * (int)(Request.Query.hours.HasValue ? int.Parse(Request.Query.hours.Value) : 24))
                                                                                  );

                    var query = new TableQuery().Where(TableQuery.CombineFilters(filterLevel, TableOperators.And, filterCurrent));
                    
                    var res = table.ExecuteQuery(query);
                    
                    dynamic model = new ExpandoObject();
                    model.Events = res.Select(
                        logEvent => new LogInfo
                            {
                                LoggerName = logEvent.Properties["LoggerName"].StringValue,
                                Timestamp = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(logEvent.Timestamp, "Pacific Standard Time").ToString("MM/dd/yy H:mm:ss"),
                                Message = logEvent.Properties["Message"].StringValue,
                                Level = logEvent.PartitionKey,
                            }).ToList();

                    return View["logviewer.html", model];
                };
        }

        private bool HasAccess(string user)
        {
            var allowed = CloudConfigurationManager.GetSetting("AllowedUsers");

            return allowed != null && allowed.Split(';').Contains(user);
        }

        private string ProcessUserCookie()
        {
            var userCookie = Request.Cookies.ContainsKey(UserTokenKey) ? Request.Cookies[UserTokenKey] : string.Empty;

            try
            {
                var userTicket = FormsAuthentication.Decrypt(userCookie);

                if (userTicket != null)
                {
                    return userTicket.Name;
                }
            }
            catch (Exception)
            {
                if (Request.Cookies.ContainsKey(UserTokenKey)) //remove cookie that is causing exception
                {
                    Request.Cookies.Remove(UserTokenKey);
                }
            }

            return null;
        }

        private string GetUser()
        {
            // get ticket & service
            string ticket = Context.Request.Query.ticket;
            string service = Context.Request.Url.SiteBase + "/auth";

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
