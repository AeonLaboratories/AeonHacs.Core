using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace AeonHacs;

/// <summary>
/// Thread-safe manager for channel-scoped status messages indicating what the system is doing.
/// Each status is explicitly started and later ended; the most recently started unfinished status
/// is exposed as <see cref="Latest"/>. Multiple statuses may be active concurrently.
/// <see cref="Active"/> returns all active statuses in most-recent-first order.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="Start(string)"/> to begin a status and obtain a handle (<see cref="Status"/>).
/// Call <see cref="Status.End()"/> (or dispose the handle) to complete it. The channel raises
/// <see cref="INotifyPropertyChanged.PropertyChanged"/> for <see cref="Latest"/> and <see cref="Active"/>
/// when the active set or its recency order changes.
///</para>
///
/// <para><b>Last In, First Out:</b> When a status starts, it becomes the <see cref="Latest"/>.
/// When the latest ends, the previous unfinished status becomes <see cref="Latest"/>.</para>
///
/// Typical usage:
/// 
/// <code>
/// //=================
/// var step = ProcessStep.Start($"Waiting for pigs to fly");
/// while (!WaitFor(() => airborne(pigs), longEnough, 1000))
/// {
///     // handle timeout...
/// }
/// step.End() // OR ProcessStep.End(step);
/// </code>
///
/// <code>
/// //=================
/// var step = ProcessStep.Start("Preparing inlet port");
/// var substep = ProcessSubStep.Start($"Evacuating inlet port");
/// // work...
/// substep.End();
///
/// substep = ProcessSubStep.Start($"Flushing inlet port...");
/// for (int i = 1; i &lt;= n; i++)
/// {
///     substep.Update($"Flushing inlet port {i}/{n}.");
///     try { /* work... */ }
///     finally { substep.End(); }
/// }
/// step.End();
/// </code>
///
/// <para><b>Using-declaration:</b> The handle implements <see cref="IDisposable"/> so using
/// declarations are natural:</para>
/// <code>
/// using var status = channel.Start("Evacuating MC");
/// // work...
/// // status.Dispose() is called automatically at end of scope (even on exception)
/// </code>
///
/// <para><b>Reassignment:</b> If you reassign a using-declared variable, dispose the prior handle
/// first to avoid leaving it active:</para>
/// 
/// <code>
/// using var s = channel.Start("A");
/// s.End();                 // or s.Dispose()
/// s = channel.Start("B");  // safe: A ended, B will dispose at end of scope
/// </code>
/// </remarks>
public sealed class StatusChannel : INotifyPropertyChanged
{
    public static StatusChannel DefaultMajor { get; } = new StatusChannel("ProcessStep");
    public static StatusChannel Default { get; } = new StatusChannel("ProcessSubStep");

    /// <inheritdoc />
    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    /// Gets the channel name for diagnostics/logging.
    /// </summary>
    public string Name { get; }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Rec> _active = new();
    private readonly LinkedList<Guid> _order = new(); // tail == latest

    /// <summary>
    /// Initializes a new <see cref="StatusChannel"/>.
    /// </summary>
    /// <param name="name">Human-friendly channel name (e.g., "Major", "Minor", "ProcessStep").</param>
    public StatusChannel(string name = "Status") => Name = name;

    /// <summary>
    /// Starts a new status in this channel and returns a handle to it.
    /// </summary>
    /// <param name="description">Human-readable description of the activity.</param>
    /// <returns>
    /// A <see cref="Status"/> handle for updating or ending the status. The status becomes
    /// the current <see cref="Latest"/> until it ends or a newer one starts.
    /// </returns>
    /// <remarks>
    /// The returned handle is thread-safe and may be disposed from any thread. It is idempotent:
    /// multiple calls to <see cref="Status.End()"/> or <see cref="IDisposable.Dispose"/> are safe.
    /// </remarks>
    /// <example>
    /// <code>
    /// using var s = ProcessSubStep.Start("Heating inlet port quartz media...");
    /// // work...
    /// // ends automatically at end of scope
    /// </code>
    /// </example>
    public Status Start(string description)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            _active[id] = new Rec(description, now);
            _order.AddLast(id);
        }
        Hacs.SystemLog.Record($"{Name}: Start {description}");
        RaiseLatestAndActive();
        return new Status(this, id);
    }

    /// <summary>
    /// Ends the specified status handle (convenience wrapper).
    /// </summary>
    /// <param name="s">The handle to end (may be <c>null</c>).</param>
    /// <returns><c>true</c> if the status was active and is now ended; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Equivalent to calling <see cref="Status.End()"/> on the handle. Safe to call multiple times.
    /// </remarks>
    public bool End(Status s) => s?.End() ?? false;

    /// <summary>
    /// Updates the description of the specified status handle (convenience wrapper).
    /// </summary>
    /// <param name="s">The handle to update (may be <c>null</c>).</param>
    /// <param name="text">New description text.</param>
    /// <returns>
    /// <c>true</c> if the status was active and updated; <c>false</c> if the handle is null or inactive.
    /// </returns>
    public bool Update(Status s, string text) => s?.Update(text) ?? false;

    /// <summary>
    /// The most recently started status that is still active, or <c>null</c> if none are active.
    /// </summary>
    public Status? Latest
    {
        get
        {
            lock (_gate)
            {
                var tail = _order.Last;
                return tail is null ? null : new Status(this, tail.Value);
            }
        }
    }

    /// <summary>
    /// Gets a snapshot of all currently active statuses in most-recent-first order.
    /// </summary>
    /// <remarks>
    /// The returned handles remain valid until each status ends. Text changes to a
    /// non-latest item do not raise <see cref="PropertyChanged"/> for <see cref="Active"/>,
    /// but starting/ending any status will.
    /// </remarks>
    public IReadOnlyList<Status> Active
    {
        get
        {
            lock (_gate)
            {
                // create fresh handles; they remain valid until End()
                return _order
                    .Reverse()
                    .Select(id => new Status(this, id))
                    .ToArray();
            }
        }
    }

    /// <summary>
    /// Ends and removes all active statuses in this channel.
    /// </summary>
    /// <remarks>
    /// Observers are notified via <see cref="PropertyChanged"/> for both <see cref="Latest"/>
    /// and <see cref="Active"/> after the channel is cleared.
    /// </remarks>
    public void Clear()
    {
        int count;
        lock (_gate)
        {
            count = _active.Count;
            _active.Clear();
            _order.Clear();
        }
        if (count > 0) Hacs.SystemLog.Record($"{Name}: Cleared ({count})");
        RaiseLatestAndActive();
    }

    /// <summary>
    /// Attempts to read the current description and start time of an active status.
    /// </summary>
    /// <param name="id">Status identifier.</param>
    /// <param name="v">On success, receives a view of the active status.</param>
    /// <returns><c>true</c> if the status is active; otherwise <c>false</c>.</returns>
    internal bool TryRead(Guid id, out View v)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(id, out var r))
            { v = new View(r.Description, r.StartUtc); return true; }
            v = default; return false;
        }
    }

    /// <summary>
    /// Updates the description of an active status.
    /// </summary>
    /// <param name="id">Status identifier.</param>
    /// <param name="text">New description.</param>
    /// <returns><c>true</c> if updated; <c>false</c> if not found/active.</returns>
    /// <remarks>
    /// Raises <see cref="PropertyChanged"/> for <see cref="Latest"/> only if the updated
    /// status is the current latest.
    /// </remarks>
    internal bool Update(Guid id, string text)
    {
        bool touchLatest = false;
        lock (_gate)
        {
            if (!_active.TryGetValue(id, out var r)) return false;
            if (r.Description == text) return true;
            r.Description = text;
            touchLatest = (_order.Last?.Value == id);
        }
        if (touchLatest) OnPropertyChanged(nameof(Latest));
        return true;
    }

    /// <summary>
    /// Ends an active status by identifier.
    /// </summary>
    /// <param name="id">Status identifier.</param>
    /// <returns><c>true</c> if the status was active and is now ended; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Raises <see cref="PropertyChanged"/> for both <see cref="Latest"/> and <see cref="Active"/>
    /// after removal. Idempotent: ending an already-ended status returns <c>false</c>.
    /// </remarks>
    internal bool End(Guid id)
    {
        string? endedText = null;
        bool removed = false;
        lock (_gate)
        {
            if (_active.Remove(id, out var r))
            {
                endedText = r.Description;
                var node = _order.Find(id);
                if (node != null) _order.Remove(node);
                removed = true;
            }
        }
        if (removed)
        {
            if (endedText != null) Hacs.SystemLog.Record($"{Name}: End {endedText}");
            RaiseLatestAndActive();
        }
        return removed;
    }

    private void RaiseLatestAndActive()
    {
        OnPropertyChanged(nameof(Latest));
        OnPropertyChanged(nameof(Active));
    }
    private void OnPropertyChanged(string n) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    /// <inheritdoc />
    public override string ToString()
    {
        // Snapshot active once under lock
        Status[] list;
        lock (_gate)
        {
            list = _order.Reverse().Select(id => new Status(this, id)).ToArray();
        }
        if (list.Length == 0) return string.Empty;

        static string Fmt(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"hh\:mm\:ss") : t.ToString(@"mm\:ss");

        // Multiline reads best; switch to " • " if you prefer single-line
        return string.Join(Environment.NewLine, list.Select(s => $"{s.Description} ({Fmt(s.Elapsed)})"));
    }

    // Storage records for active statuses
    private sealed class Rec
    {
        public Rec(string d, DateTime s) { Description = d; StartUtc = s; }
        public string Description;
        public DateTime StartUtc;
    }

    /// <summary>
    /// Immutable view of an active status. Used for thread-safe reads.
    /// </summary>
    /// <param name="Description">Current description.</param>
    /// <param name="StartUtc">Start timestamp (UTC).</param>
    internal readonly record struct View(string Description, DateTime StartUtc);

    /// <summary>
    /// Handle for a started status in a <see cref="StatusChannel"/>.
    /// Provides thread-safe read access and delegates updates/ending to the channel.
    /// </summary>
    public sealed class Status : IDisposable
    {
        private readonly StatusChannel _owner;

        /// <summary>
        /// Gets the identifier of this status.
        /// </summary>
        public Guid Id { get; }

        internal Status(StatusChannel owner, Guid id)
        { _owner = owner; Id = id; }

        /// <summary>
        /// Gets a value indicating whether this status is still active.
        /// </summary>
        /// <value><c>true</c> if active; otherwise <c>false</c>.</value>
        public bool IsActive => _owner.TryRead(Id, out _);

        /// <summary>
        /// Gets the current description text, or an empty string if inactive.
        /// </summary>
        public string Description => _owner.TryRead(Id, out var v) ? v.Description : string.Empty;

        /// <summary>
        /// Gets the UTC start time, or <see cref="DateTime.MinValue"/> if inactive.
        /// </summary>
        public DateTime StartUtc => _owner.TryRead(Id, out var v) ? v.StartUtc : DateTime.MinValue;

        /// <summary>
        /// Gets the elapsed time since <see cref="StartUtc"/> (for inactive statuses, returns zero).
        /// </summary>
        public TimeSpan Elapsed => _owner.TryRead(Id, out var v) ? DateTime.UtcNow - v.StartUtc : TimeSpan.Zero;

        /// <summary>
        /// Updates the description text of this status if it is still active.
        /// </summary>
        /// <param name="text">New description.</param>
        /// <returns><c>true</c> if updated; <c>false</c> if inactive.</returns>
        /// <remarks>
        /// If this status is the channel's <see cref="StatusChannel.Latest"/>, the channel raises
        /// <see cref="INotifyPropertyChanged.PropertyChanged"/> for <see cref="StatusChannel.Latest"/>.
        /// </remarks>
        public bool Update(string text) => _owner.Update(Id, text);

        /// <summary>
        /// Ends this status if it is still active. Idempotent.
        /// </summary>
        /// <returns><c>true</c> if ended; <c>false</c> if already inactive.</returns>
        public bool End() => _owner.End(Id);

        /// <summary>
        /// Disposes this handle by ending the status (same as <see cref="End"/>).
        /// Safe to call multiple times.
        /// </summary>
        public void Dispose() => _ = End();

        /// <inheritdoc />
        public override string ToString() => Description;
    }
}
