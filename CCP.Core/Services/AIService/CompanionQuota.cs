using System;

namespace ConditioningControlPanel.Services.AIService;

/// <summary>Server quota belongs to the account that sent the request and expires at its UTC reset.</summary>
internal sealed class CompanionQuota
{
    private readonly object _gate = new();
    private string? _identity;
    private int? _remaining;
    private DateTimeOffset? _reset;

    internal void Update(string? sentIdentity, string? currentIdentity, int? remaining, string? resetsAt)
    {
        if (string.IsNullOrEmpty(sentIdentity) || sentIdentity != currentIdentity || remaining is null or < 0
            || !DateTimeOffset.TryParse(resetsAt, out var reset)) return;
        lock (_gate)
        {
            _identity = sentIdentity;
            _remaining = remaining;
            _reset = reset;
        }
    }

    internal int Remaining(string? identity, DateTimeOffset now)
    {
        lock (_gate)
            return !string.IsNullOrEmpty(identity) && identity == _identity && _reset > now
                ? _remaining ?? -1 : -1;
    }
}
