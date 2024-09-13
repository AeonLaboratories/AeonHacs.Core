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

        OnAlert += EnqueueAlert;
        OnWarning += EnqueueAlertAsync;
    }

    [HacsStop]
    protected virtual void Stop()
    {
        MessageQueue.Clear();
        AlertTimer.Reset();

        OnAlert -= EnqueueAlert;
        OnWarning -= EnqueueAlertAsync;

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
        var subject = $"({notice.Timestamp:MMMM dd, H:mm:ss}) {notice.Subject}";
        var message = notice.Message;

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
                Text = $"{notice.Message}\r\n{smtpInfo.SenderName}: {DateTime.Now}"
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
            var errorCaption = $"{Name}: Can't transmit Alert";
            var errorMessage = $"Subject: '{subject}'\r\n" +
                $"Message: '{message}'\r\n\r\n";
            if (SmtpInfo == null)
                errorMessage += $"(No Email account configured)\r\n";
            else
            {
                errorMessage += $"(Check Email configuration in '{SmtpInfo.CredentialsFilename}'.)\r\n";
                errorMessage += $"Exception: {e.Message}\r\n";
            }
            errorMessage += $"\r\nRespond Ok to continue or Cancel to disable Alerts";
            if (Ask(errorMessage, errorCaption, NoticeType.Warning).Ok())
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

        bool match = History.RemoveAll(h => h.Subject == notice.Subject && h.Message == notice.Message) > 0;

        History.Add(notice);
        return match;
    }

    protected virtual void EnqueueAlert(Notice notice)
    {
        if (notice.CancellationToken.IsCancellationRequested || !AlertsEnabled || Repeated(notice))
            return;
        MessageQueue.Enqueue(notice);
    }

    protected virtual Task<Notice?> EnqueueAlertAsync(Notice notice)
    {
        EnqueueAlert(notice);
        return NoResponse();
    }
}
