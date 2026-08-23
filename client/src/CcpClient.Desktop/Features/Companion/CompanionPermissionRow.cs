using System.ComponentModel;
using CcpClient.Desktop.Ai;

namespace CcpClient.Desktop.Features.Companion;

/// <summary>
/// One switch on the permissions grid, and the honest sentence underneath it.
///
/// <para><b>Two independent facts, never collapsed into one.</b> <see cref="Allowed"/> is what the
/// user permits; <see cref="Backed"/> is what this build can actually do. Upstream shows the same
/// separation with a notice above its ten switches — "Cloud AI can chat but can't trigger app
/// effects" (<c>Views/Controls/Companion/AiPermissionsGrid.xaml:112-137</c>,
/// <c>lab_ai_effects_needs_local_body</c>) — leaving the switches live while telling the user they
/// currently drive nothing. This grid says it per row instead of once for the card, because here
/// the answer differs per row.</para>
///
/// <para>A ticked switch over an absent backend is therefore not a lie and not a trap: it records
/// consent that will apply the day the backend lands, and the row says out loud that today it
/// does not.</para>
/// </summary>
public sealed class CompanionPermissionRow : INotifyPropertyChanged
{
    private readonly AiEffectPermissionRow _row;
    private readonly Func<AiEffectPermissions> _read;
    private readonly Action<AiEffectPermissions> _write;
    private readonly Func<AiCommandKind, bool> _handles;

    internal CompanionPermissionRow(
        AiEffectPermissionRow row,
        Func<AiEffectPermissions> read,
        Action<AiEffectPermissions> write,
        Func<AiCommandKind, bool> handles)
    {
        _row = row;
        _read = read;
        _write = write;
        _handles = handles;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Upstream's stable per-switch tag (<c>AiPermissionsGrid.xaml:186-198</c>) — the automation id, never the label.</summary>
    public string Id => _row.Id;

    /// <summary>What the user reads (upstream's own label).</summary>
    public string Label => _row.Label;

    /// <summary>Whether the user permits this. Writing goes straight to the participant's typed permission state, which is the same object the execution gates read.</summary>
    public bool Allowed
    {
        get => _read().IsRowAllowed(_row);
        set
        {
            if (_read().IsRowAllowed(_row) == value)
            {
                return;
            }

            _write(_read().WithRow(_row, value));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Allowed)));
        }
    }

    /// <summary>True when every kind this row governs has a registered handler. The overlay row governs two kinds and is backed only when both are.</summary>
    public bool Backed => _row.Kinds.All(_handles);

    /// <summary>Empty when <see cref="Backed"/>. Otherwise the named reason this build cannot do it — <see cref="AiEffectBridge.Absences"/> when the seam is genuinely missing, and the composition truth when the rack simply was not handed to the companion.</summary>
    public string BackendNote
    {
        get
        {
            if (Backed)
            {
                return string.Empty;
            }

            foreach (var kind in _row.Kinds)
            {
                if (AiEffectBridge.Absences.TryGetValue(kind, out var reason))
                {
                    return reason.Detail;
                }
            }

            return "this build has an effect module for it, but the companion was not given the "
                + "session's rack, so nothing would be dispatched";
        }
    }

    /// <summary>Drives the row's subdued treatment: bound to the same authority as the note.</summary>
    public bool BackendNoteVisible => !Backed;
}
