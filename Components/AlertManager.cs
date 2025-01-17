using MailKit.Net.Smtp;
using MimeKit;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static AeonHacs.Notify;

#nullable enable

namespace AeonHacs.Components;

public class AlertManager : HacsComponent, IAlertManager
{
    CancellationTokenSource? HandlerTokenSource;
    Task? HandlerTask;

    protected virtual void Start()
    {
        HandlerTokenSource = CancellationTokenSource.CreateLinkedTokenSource(Hacs.CancellationToken);
        HandlerTask = Task.Run(() => AlertHandler(HandlerTokenSource.Token), HandlerTokenSource.Token);

        OnAlert += EnqueueNotice;
    }

    [HacsStop]
    protected virtual void Stop()
    {
        MessageQueue.Clear();
        AlertTimer.Reset();

        OnAlert -= EnqueueNotice;

        HandlerTokenSource?.Cancel();
        HandlerTask?.Wait();
        HandlerTask?.Dispose();
        HandlerTask = null;
        HandlerTokenSource?.Dispose();
        HandlerTokenSource = null;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    [JsonProperty]
    public bool AlertsEnabled
    {
        get => alertsEnabled;
        set
        {
            Ensure(ref alertsEnabled, value);
            if (alertsEnabled && HandlerTokenSource == null)
                Start();
            else if (!alertsEnabled && HandlerTokenSource != null)
                Stop();
        }
    }
    bool alertsEnabled;

    [JsonProperty]
    [DefaultValue(typeof(TimeSpan), "1.00:00:00")]
    public TimeSpan DuplicateMessageSuppressionDuration
    {
        get => duplicateMessageSuppressionDuration;
        set => Ensure(ref duplicateMessageSuppressionDuration, value);
    }
    TimeSpan duplicateMessageSuppressionDuration = TimeSpan.FromDays(1);

    protected Stopwatch AlertTimer { get; } = new();
    bool OkToSend() => !AlertTimer.IsRunning || AlertTimer.ElapsedMilliseconds > 60 * 1000;  // at least one minute since last alert
    protected ConcurrentQueue<Notice> MessageQueue { get; } = new();

    protected SmtpInfo SmtpInfo => SmtpInfo.DefaultSmtpInfo;
    
    protected void SendMail(Notice notice)
    {
        // We can use HandlerTokenSource.Token to check for cancellation
        var subject = notice.Message;
        var message = $"({notice.Timestamp:MMMM dd, H:mm:ss})\r\n {notice.Details}";

        try
        {
            if (SmtpInfo is not SmtpInfo smtpInfo)
                return;

            var address = new MailboxAddress(smtpInfo.SenderName, smtpInfo.EmailAddress);
            var mail = new MimeMessage();
            mail.From.Add(address);
            mail.To.Add(address);
            // add timestamp?
            mail.Subject = subject;
            mail.Body = new TextPart(MimeKit.Text.TextFormat.Plain)
            {
                Text = $"{message}\r\n{smtpInfo.SenderName}: {DateTime.Now}"
            };

            using (var client = new SmtpClient())
            {
                client.ServerCertificateValidationCallback = (l, j, c, m) => true;
                client.Connect(smtpInfo.Host, smtpInfo.Port);
                client.Authenticate(smtpInfo.EmailAddress, smtpInfo.Password);
                client.Send(mail);
                client.Disconnect(true);
            }

            AlertTimer.Restart();
        }
        catch (Exception e)
        {
            var errorDetails = $"Subject: '{subject}'\r\nMessage: '{message}'\r\n\r\n";
            if (SmtpInfo == null)
                errorDetails += $"(No Email account configured)\r\n";
            else
            {
                errorDetails += $"(Check Email configuration in '{SmtpInfo.CredentialsFilename}'.)\r\n";
                errorDetails += $"Exception: {e.Message}\r\n";
            }
            errorDetails += $"\r\nOk to continue or Cancel to disable Alerts";
            if (OkCancel($"{Name}: Can't transmit Alert", 
                errorDetails, NoticeType.Warning).Cancelled())
                AlertsEnabled = false;
        }
    }


    protected virtual void AlertHandler(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (OkToSend() && MessageQueue.TryDequeue(out var notice) && !notice.CancellationToken.IsCancellationRequested)
                SendMail(notice);

            Task.Delay(500, cancellationToken).ContinueWith(t => { }).Wait();
        }
    }

    protected List<Notice> History { get; } = new();
    protected bool Repeated(Notice notice)
    {
        var fresh = DateTime.Now.Subtract(DuplicateMessageSuppressionDuration);
        History.RemoveAll(h => h.Timestamp < fresh);

        bool match = History.RemoveAll(h => h.Message == notice.Message && h.Details == notice.Details) > 0;

        History.Add(notice);
        return match;
    }

    protected virtual void EnqueueNotice(Notice notice)
    {
        if (notice.CancellationToken.IsCancellationRequested || !AlertsEnabled || Repeated(notice))
            return;
        MessageQueue.Enqueue(notice);
    }
    
}
