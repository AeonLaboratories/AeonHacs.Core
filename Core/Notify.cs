using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AeonHacs;

/// <summary>
/// Represents the method that will handle notices that do not expect a response.
/// </summary>
/// <param name="notice"></param>
public delegate void NoticeHandler(Notice notice);

/// <summary>
/// Represents the method that will handle notices that expect a response.
/// </summary>
/// <param name="notice"></param>
/// <returns></returns>
public delegate Notice PromptHandler(Notice notice);

/// <summary>
/// The intended audience for a notice.
/// </summary>
[Flags]
public enum Audience
{
    /// <summary>
    /// Send a message to remote subscribers.
    /// </summary>
    Remote = 1,
    /// <summary>
    /// Show a message in the local UI.
    /// </summary>
    Local = 2,
    /// <summary>
    /// Both
    /// </summary>
    All = 3
}

#nullable enable

/// <summary>
/// Handles messaging between the application and the user.
/// </summary>
public static class Notify
{
    /// <summary>
    /// Invoked when the intended audience is remote.
    /// </summary>
    public static event NoticeHandler? OnAlert;

    /// <summary>
    /// Invoked when the intended audience is local.
    /// </summary>
    public static event NoticeHandler? OnNotice;

    /// <summary>
    /// Invoked when a response is expected from the local audience.
    /// </summary>
    public static event PromptHandler? OnPrompt;

    private static async Task<Notice> SendNotice(Notice notice, Audience audience)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(Hacs.CancellationToken, notice.CancellationToken);
        notice.CancellationToken = cts.Token;

        if (cts.Token.IsCancellationRequested)
            return Notice.NoResponse;

        Hacs.SystemLog.Record(notice.Message);

        if (audience.HasFlag(Audience.Remote))
            OnAlert?.GetInvocationList().Cast<NoticeHandler>().Select(h => Task.Run(() => h(notice))).ToArray();

        if (audience.HasFlag(Audience.Local))
        {
            if (notice.Responses.Any())
            {
                var tasks = OnPrompt?.GetInvocationList().Cast<PromptHandler>().Select(h => Task.Run(() => h(notice)));
                var response = await tasks.WhenAny().Result;

                cts.Cancel();

                return response;
            }
            else
            {
                OnNotice?.GetInvocationList().Cast<NoticeHandler>().Select(h => Task.Run(() => h(notice))).ToArray();
            }
        }
        
        return Notice.NoResponse;
    }

    /// <summary>
    /// Send an alert to the remote audience.
    /// </summary>
    public static void Alert(string message, string details = "") =>
        _ = SendNotice(new Notice(message, details), Audience.Remote);

    /// <summary>
    /// Send a notice to the local audience.
    /// </summary>
    public static void Tell(string message, string details = "", NoticeType type = NoticeType.Information, CancellationToken cancellationToken = default) =>
        _ = SendNotice(new Notice(message, details, type, cancellationToken), Audience.Local);

    /// <summary>
    /// Send a notice to both the remote and local audiences.
    /// </summary>
    public static void Announce(string message, string details = "", NoticeType type = NoticeType.Information, CancellationToken cancellationToken = default) =>
        _ = SendNotice(new Notice(message, details, type, cancellationToken), Audience.All);

    /// <summary>
    /// Send a notice requesting a response from the local audience.
    /// </summary>
    /// <param name="responses">A list of suggested responses.</param>
    /// <returns>A notice containing the response.</returns>
    public static Notice Prompt(string message, string details = "", NoticeType type = NoticeType.Question, CancellationToken cancellationToken = default, Audience audience = Audience.Local, params string[] responses) =>
        SendNotice(new Notice(message, details, type, cancellationToken) { Responses = responses }, audience).Result;

    /// <summary>
    /// Send a notice expecting a Yes or No response from the local audience.
    /// </summary>
    public static Notice YesNo(string message, string details = "", NoticeType type = NoticeType.Question, CancellationToken cancellationToken = default, Audience audience = Audience.Local) =>
        Prompt(message, details, type, cancellationToken, audience, [ "Yes", "No" ]);

    /// <summary>
    /// Send a notice expecting an Ok or Cancel response from the local audience.
    /// </summary>
    public static Notice OkCancel(string message, string details = "", NoticeType type = NoticeType.Information, CancellationToken cancellationToken = default, Audience audience = Audience.Local) =>
        Prompt(message, details, type, cancellationToken, audience, [ "Ok", "Cancel" ]);

    /// <summary>
    /// Play a sound locally.
    /// </summary>
    /// <param name="message">The requested sound.</param>
    public static void PlaySound(string message = "chord") =>
        _ = SendNotice(new Notice(message, type: NoticeType.Sound), Audience.Local);

    /// <summary>
    /// Send a message to the local audience, and wait for it to be acknowledged.
    /// </summary>
    public static void Pause(string message, string details = "", NoticeType type = NoticeType.Alert, CancellationToken cancellationToken = default, Audience audience = Audience.All, string response = "Ok") =>
        Prompt(message, details, type, cancellationToken, audience, [ response ]);

    /// <summary>
    /// Send an OkCancel notice with a warning icon.
    /// </summary>
    public static Notice Warn(string message, string details = "", CancellationToken cancellationToken = default, Audience audience = Audience.All) =>
        OkCancel(message, details, NoticeType.Warning, cancellationToken, audience);

    /// <summary>
    /// Send an Alert prompt to All with the message "Operator Needed", and details "Waiting for Operator; Ok to continue".
    /// </summary>
    public static void WaitForOperator() =>
        WaitForOperator("Waiting for Operator.");

    /// <summary>
    /// Send an Alert prompt to All with the message "Operator Needed". Appends "Ok to continue" to the Details.
    /// </summary>
    /// <param name="whatToDo">Operator instructions to be included in the notice Details</param>
    public static void WaitForOperator(string whatToDo) =>
        Pause("Operator Needed", $"{whatToDo}\r\nOk to continue.");

    /// <summary>
    /// Send a Configuration Error message to all suggesting to restart the application to abort the process, but allow the operator to elect to continue in this state.
    /// </summary>
    /// <param name="configError"></param>
    public static void ConfigurationError(string configError) =>
        Pause(configError,
            "This is a configuration error.\r\n" +
            "Restart the application to abort the process.",
            NoticeType.Error,
            response: "Continue in this state");

    #region Extension Methods

    /// <param name="notice"></param>
    /// <returns>True if the Message is "No Response"</returns>
    public static bool NoResponse(this Notice notice) =>
        notice.Message == "No Response";

    /// <param name="notice"></param>
    /// <returns>True if the Message is "Ok"</returns>
    public static bool Ok(this Notice notice) =>
        notice.Message == "Ok";

    /// <param name="notice"></param>
    /// <returns>True if the Message is "Cancel"</returns>
    public static bool Cancelled(this Notice notice) =>
        notice.Message == "Cancel";

    #endregion Extension Methods
}
