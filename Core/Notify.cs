using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AeonHacs;

public delegate void NoticeHandler(Notice notice);

public delegate Notice PromptHandler(Notice notice);

#nullable enable

public static class Notify
{
    public static event NoticeHandler? OnActionTaken; // Write to SystemLog
    public static event NoticeHandler? OnAlert; // Send to AlertManager
    public static event NoticeHandler? OnInfo; // Show in UI
    public static event NoticeHandler? OnMajorEvent; // Write to EventLog
    public static event PromptHandler? OnQuestion; // Show in UI and await response
    public static event NoticeHandler? OnSound; // Play sound
    public static event PromptHandler? OnWarning; // Send to AlertManager + Show in UI and await response
    public static event PromptHandler? OnError; // Write to EventLog + Show in UI, Exit/Restart application?

    private static void Notice(NoticeHandler? handler, Notice notice)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(Hacs.CancellationToken, notice.CancellationToken);
        notice.CancellationToken = cts.Token;

        Task.Run(() => Parallel.Invoke(handler?.GetInvocationList().Cast<NoticeHandler>().Select<NoticeHandler, Action>(h => () => h(notice)).ToArray() ?? []));
    }

    private static async Task<Notice> Prompt(PromptHandler? handler, Notice notice)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(Hacs.CancellationToken, notice.CancellationToken);
        notice.CancellationToken = cts.Token;

        var tasks = handler?.GetInvocationList().Cast<PromptHandler>().Select(h => Task.Run(() => h(notice)));

        var response = await tasks.WhenAny().Result;

        cts.Cancel();

        return response;
    }

    public static void ActionTaken(string message, string? subject = null, NoticeType type = NoticeType.ActionTaken, CancellationToken cancellationToken = default) =>
        Notice(OnActionTaken, new Notice(message, subject, type, cancellationToken));

    public static void Alert(string message, string? subject = null, NoticeType type = NoticeType.Alert, CancellationToken cancellationToken = default) =>
        Notice(OnAlert, new Notice(message, subject, type, cancellationToken));

    /// <summary>
    /// Send a message to all subscribers of the <see cref="OnAlert"/> and <see cref="OnInfo"/> events.
    /// </summary>
    public static void Tell(string message, string? subject = null, NoticeType type = NoticeType.Information, CancellationToken cancellationToken = default)
    {
        var notice = new Notice(message, subject, type, cancellationToken);

        Notice(OnAlert, notice);
        Notice(OnInfo, notice);
    }

    public static void Announce(string message, string? subject = null, NoticeType type = NoticeType.Information, CancellationToken cancellationToken = default) =>
        Notice(OnInfo, new Notice(message, subject, type, cancellationToken));

    public static void MajorEvent(string message, string? subject = null, NoticeType type = NoticeType.MajorEvent, CancellationToken cancellationToken = default) =>
        Notice(OnMajorEvent, new Notice(message, subject, type, cancellationToken));

    public static Notice Ask(string message, string? subject = null, NoticeType type = NoticeType.Question, CancellationToken cancellationToken = default) =>
        Prompt(OnQuestion, new Notice(message, subject, type, cancellationToken)).Result;

    public static void PlaySound(string message = "chord", string? subject = null, NoticeType type = NoticeType.Sound, CancellationToken cancellationToken = default) =>
        Notice(OnSound, new Notice(message, subject, type, cancellationToken));

    public static Notice Warn(string message, string? subject = null, NoticeType type = NoticeType.Warning, CancellationToken cancellationToken = default) =>
        Prompt(OnWarning, new Notice(message, subject, type, cancellationToken)).Result;

    public static Notice Error(string message, string? subject = null, NoticeType type = NoticeType.Error, CancellationToken cancellationToken = default) =>
        Prompt(OnError, new Notice(message, subject, type, cancellationToken)).Result;

    #region Extension Methods

    public static bool Ok(this Notice notice) =>
        notice.Message == "Ok";

    /// <param name="notice"></param>
    /// <returns>True if the notice is null or its Message is "Cancel"</returns>
    public static bool Cancelled(this Notice notice) =>
        notice.Equals(AeonHacs.Notice.NoResponse) || notice.Message.Equals("Cancel");

    #endregion Extension Methods
}
