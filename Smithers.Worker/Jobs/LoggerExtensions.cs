﻿using System;
using System.Net.Mail;
using Common.Logging;
using Microsoft.WindowsAzure;
using SendGridMail;
using SendGridMail.Transport;
using System.Net;

namespace Smithers.Worker.Jobs
{
    public static class LoggerExtensions
    {
        public static void LogCustomError(this ILog log, Exception ex, string jobName = null)
        {
            if (jobName == null)
            {
                jobName = "Job";
            }

            log.ErrorFormat("Error: {0} failed at {1} with exception {2} {3}", ex, jobName, ex.Source, ex.Message, ex.StackTrace);

            SendErrorEmail(jobName, ex, log);
        }

        private static void SendErrorEmail(string jobName, Exception ex, ILog log)
        {
            var username = CloudConfigurationManager.GetSetting("sendgrid-username");
            var pass = CloudConfigurationManager.GetSetting("sendgrid-pass");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(pass))
            {
                log.Error("Could not send email notification. Can username or password");
            }

            try
            {
                var sgMessage = SendGrid.GetInstance();
                sgMessage.From = new MailAddress("caes-tech@ucdavis.edu", "Smithers Job Notifications");
                sgMessage.Subject = "Smithers Error Notification";
                sgMessage.AddTo("caes-tech@ucdavis.edu");
                sgMessage.Html = string.Format("Error: {0} failed at {1} with exception {2} {3}", jobName, ex.Source,
                                               ex.Message, ex.StackTrace);

                var transport = SMTP.GetInstance(new NetworkCredential(username, pass));
                transport.Deliver(sgMessage);
            }
            catch (Exception mailException)
            {
                log.ErrorFormat("Error: Email send failed at {0} with exception {1} {2}", mailException,
                                mailException.Source, mailException.Message, mailException.StackTrace);
            }
        }
    }
}