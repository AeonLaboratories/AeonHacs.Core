using System;
using System.Threading;

namespace AeonHacs;

public enum NoticeType
{
    ActionTaken,
    Alert,
    Information,
    MajorEvent,
    Question,
    Sound,
    Warning,
    Error
}

#nullable enable

// Subject is nullable so consumers can use the null-coalescing operator to provide a default value when used.
public struct Notice(string message, string? subject = null, NoticeType type = NoticeType.Information, CancellationToken cancellationToken = default)
{
    //TODO: Rename?
    public static readonly Notice NoResponse = new Notice("No Response");

    public DateTime Timestamp { get; } = DateTime.Now;

    public string? Subject { get; } = subject;

    public string Message { get; } = message;

    public NoticeType Type { get; } = type;

    public CancellationToken CancellationToken { get; set; } = cancellationToken;
}
