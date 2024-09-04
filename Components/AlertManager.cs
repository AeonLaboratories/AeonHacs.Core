using AeonHacs.Utilities;
using MailKit.Net.Smtp;
using MimeKit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace AeonHacs.Components
{
    public struct AlertMessage
    {
        public string Subject;
        public string Message;
        public AlertMessage(string subject, string message)
        { Subject = subject; Message = message; }
    }

    public static class Alert
    {
        public static IAlertManager DefaultAlertManager { get; set; }

        /// <summary>
        /// Dispatch a message to the remote operator only.
        /// The process is not paused.
        /// </summary>
        public static void Send(string subject, string message) =>
            DefaultAlertManager?.Send(subject, message);

        /// <summary>
        /// Dispatch a message to the remote operator and to the local user interface.
        /// The process is not paused.
        /// </summary>
        public static Notice Announce(string subject, string message) =>
            DefaultAlertManager?.Announce(subject, message);

        /// <summary>
        /// Pause and give the local operator the option to continue.
        /// </summary>
        public static Notice Pause(string subject, string message) =>
            DefaultAlertManager?.Pause(subject, message);

        /// <summary>
        /// Make an entry in the EventLog, pause and give the local operator
        /// the option to continue. The notice is transmitted as a Warning.
        /// </summary>
        public static Notice Warn(string subject, string message) =>
            DefaultAlertManager?.Warn(subject, message);
    }


    // TODO: should this class derive from StateManager?
    public class AlertManager : HacsComponent, IAlertManager
    {
        static HacsLog SystemLog => Hacs.SystemLog;

        #region HacsComponent

        [HacsStart]
        protected virtual void Start()
        {
            Stopping = false;
            alertThread = new Thread(AlertHandler) { Name = $"{Name} AlertHandler", IsBackground = true };
            alertThread.Start();
        }

        [HacsStop]
        protected virtual void Stop()
        {
            Stopping = true;
            alertSignal.Set();
            stoppedSignal.WaitOne();
        }

        ManualResetEvent stoppedSignal = new ManualResetEvent(true);
        public new bool Stopped => stoppedSignal.WaitOne(0);
        protected bool Stopping { get; set; }

        #endregion HacsComponent

        public SmtpInfo SmtpInfo => SmtpInfo.DefaultSmtpInfo;

        [JsonProperty, DefaultValue(1440)]
        public int MinutesToSuppressSameMessage
        {
            get => minutesToSuppressSameMessage;
            set => Ensure(ref minutesToSuppressSameMessage, value);
        }
        int minutesToSuppressSameMessage = 1440;

        [JsonProperty] public string PriorAlertMessage
        {
            get => lastAlertMessage;
            set => Ensure(ref lastAlertMessage, value);
        }
        string lastAlertMessage;

        [JsonProperty, DefaultValue(true)]
        public bool AlertsEnabled
        {
            get => alertsEnabled;
            set => Ensure(ref alertsEnabled, value);
        }
        bool alertsEnabled = true;

        //[JsonProperty] public ContactInfo ContactInfo
        //{
        //    get => contactInfo;
        //    set => Ensure(ref contactInfo, value, OnPropertyChanged);
        //}
        //ContactInfo contactInfo;

        // alert system
        protected Queue<AlertMessage> QAlertMessage = new Queue<AlertMessage>();
        protected Thread alertThread;
        protected AutoResetEvent alertSignal = new AutoResetEvent(false);
        protected Stopwatch AlertTimer = new Stopwatch();

        void PlaySound() => Notice.Send("PlaySound", Notice.Type.Tell);
        public IHacsLog EventLog => Hacs.EventLog;

        public enum AlertType { Alert, Announce, Pause, Warn }


        /// <summary>
        /// Send a message to the remote operator.
        /// </summary>
        public void Send(string subject, string message)
        {
            if (!AlertsEnabled ||
                message == PriorAlertMessage &&
                    AlertTimer.IsRunning &&
                    AlertTimer.Elapsed.TotalMinutes < MinutesToSuppressSameMessage)
                return;

            string date = $"({DateTime.Now:MMMM dd, H:mm:ss}) ";
            AlertMessage alert = new AlertMessage(date + subject, message);
            lock (QAlertMessage) QAlertMessage.Enqueue(alert);
            alertSignal.Set();

            PlaySound();
            PriorAlertMessage = message;
            AlertTimer.Restart();
        }

        /// <summary>
        /// Dispatch a message to the remote operator and to the local user interface.
        /// The process is not paused.
        /// </summary>
        public virtual Notice Announce(string subject, string message)
        {
            Send(subject, message);
            return Notice.Send(subject, message, Notice.Type.Tell);
        }

        /// <summary>
        /// Pause and give the local operator the option to continue.
        /// </summary>
        public virtual Notice Pause(string subject, string message)
        {
            Send(subject, message);
            return Notice.Send(subject, message + "\r\nPress Ok to continue");
        }

        /// <summary>
        /// Make an entry in the EventLog, pause and give the local operator
        /// the option to continue. The notice is transmitted as a Warning.
        /// </summary>
        public virtual Notice Warn(string subject, string message)
        {
            EventLog.Record(subject + ": " + message);
            Send(subject, message);
            return Notice.Send(subject, message, Notice.Type.Warn);
        }

        protected void AlertHandler()
        {
            stoppedSignal.Reset();
            AlertMessage alert;
            while (!Stopping)
            {
                while (SmtpInfo != null && QAlertMessage.Count > 0)
                {
                    lock (QAlertMessage) alert = QAlertMessage.Dequeue();
                    SendMail(alert.Subject, alert.Message);
                    Utility.WaitFor(() => Stopping, 60000, 500); // Always wait at least 60 seconds between messages.
                }
                alertSignal.WaitOne(500);
            }
            stoppedSignal.Set();
        }

        public void ClearLastAlertMessage()
        { PriorAlertMessage = ""; AlertTimer.Stop(); }

        protected void SendMail(string subject, string message)
        {
            try
            {
                var smtpInfo = SmtpInfo;
                var address = new MailboxAddress(smtpInfo.SenderName, smtpInfo.EmailAddress);
                var mail = new MimeMessage();
                mail.From.Add(address);
                mail.To.Add(address);
                mail.Subject = subject;
                mail.Body = new TextPart(MimeKit.Text.TextFormat.Plain)
                {
                    Text = $"{message}\r\n{smtpInfo.SenderName}: {DateTime.Now}"
                };

                using (var client = new SmtpClient())
                {
                    client.ServerCertificateValidationCallback = (l, j, c, m) => true;
                    client.Connect(smtpInfo.Host, smtpInfo.Port/*, MailKit.Security.SecureSocketOptions.SslOnConnect*/);
                    //client.AuthenticationMechanisms.Remove("XOAUTH2");
                    client.Authenticate(smtpInfo.EmailAddress, smtpInfo.Password);
                    var response = client.Send(mail);
                    SystemLog.Record($"{response}\r\n\t{subject}\r\n\t{message}");
                    client.Disconnect(true);
                }
            }
            catch (Exception e)
            {
                SystemLog.Record($"{e.Message}\r\n\t{subject}\r\n\t{message}");
                var errorCaption = $"{Name}: Can't transmit Alert";
                var errorMessage = $"Subject: '{subject}'\r\n" +
                    $"Message: '{message}'\r\n\r\n";
                if (SmtpInfo == null)
                    errorMessage += $"(No Email account configured)\r\n";
                else
                {
                    errorMessage += $"(Check Email configuration in '{SmtpInfo.CredentialsFilename}'.)\r\n";
                    errorMessage += $"Exception: { e.Message}\r\n";
                }
                errorMessage += $"\r\nRespond Ok to continue or Cancel to disable Alerts";
                if (Notice.Send(errorCaption, errorMessage, Notice.Type.Warn).Text != "Ok")
                {
                    AlertsEnabled = false;
                    PriorAlertMessage = "Alerts are disabled";
                }
            }
        }

        protected virtual void OnPropertyChanged(object sender = null, PropertyChangedEventArgs e = null)
        {
            if (sender == SmtpInfo)
                NotifyPropertyChanged(nameof(SmtpInfo));
        }
    }
}
