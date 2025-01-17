using System;
using System.Collections.Generic;
using System.Threading;

namespace AeonHacs;

public enum NoticeType
{
    Alert,
    Information,
    Question,
    Sound,
    Warning,
    Error
}

#nullable enable

public struct Notice(string message, string details = "", NoticeType type = NoticeType.Information, CancellationToken cancellationToken = default)
{
    public static readonly Notice NoResponse = new Notice("No Response");

    /// <summary>
    /// The time this notice was created.
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.Now;

    public string Message { get; } = message;

    public string? Details { get; } = details;

    public NoticeType Type { get; } = type;

    /// <summary>
    /// Suggestions for responses.
    /// </summary>
    public IEnumerable<string> Responses { get; set; } = [];

    public CancellationToken CancellationToken { get; set; } = cancellationToken;
}
