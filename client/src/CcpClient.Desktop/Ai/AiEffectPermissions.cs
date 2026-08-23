namespace CcpClient.Desktop.Ai;

/// <summary>
/// One row of the permissions grid: the label the user reads and the command kinds that row
/// governs. Ten rows for eleven kinds, because upstream's OVERLAY switch governs BOTH the spiral
/// and the pink filter through one setting (<c>Services/Commands/AiCommandService.cs:186-187</c>:
/// <c>spiral =&gt; s.AllowAiOverlay</c> and <c>pink =&gt; s.AllowAiOverlay</c>). The row order and
/// the labels are upstream's own grid, read in its reading order
/// (<c>Views/Controls/Companion/AiPermissionsGrid.xaml:185-199</c>, labels from
/// <c>Localization/Languages/en.json:1733-1742</c>).
/// </summary>
/// <param name="Id">The stable identity, upstream's own <c>Tag</c> string. Never a display string:
/// upstream tags each switch so "re-ordering this grid can never remap a saved permission"
/// (<c>AiPermissionsGrid.xaml:183-184</c>).</param>
/// <param name="Label">What the user reads.</param>
/// <param name="Kinds">The command kinds this one switch admits.</param>
public sealed record AiEffectPermissionRow(string Id, string Label, IReadOnlyList<AiCommandKind> Kinds);

/// <summary>
/// What the companion is allowed to do to the user's screen: the master switch and the per-effect
/// set, in the exact shape <see cref="AiExecutionGates"/> and <see cref="AiEnvelopePolicy"/>
/// already consume (<c>MasterEffectsEnabled</c> + <c>IsEffectAllowed</c>), so the surface and the
/// gate cannot drift into two different answers.
///
/// <para><b>The default is closed and stays closed.</b> <see cref="NoneAdmitted"/> is master OFF
/// and an EMPTY set — not upstream's baseline, which is master off but bubbles, subliminal and
/// bounce pre-ticked (<c>Models/CompanionPromptSettings.cs:99-112</c>). That divergence was
/// already recorded by <see cref="AiExecutionGates.NoneAdmitted"/> and it is kept here: which
/// effects a companion may drive is a consent question and it is the owner's. A "safe subset"
/// admitted by a lane is a decision nobody made.</para>
///
/// <para><b>Session-scoped, deliberately.</b> Upstream persists these ten booleans in
/// settings.json and saves on every click (<c>MainWindow/MainWindow.Patreon.cs:1477,1501</c>).
/// This port holds them in memory beside the companion's other consent states
/// (<c>CompanionParticipant.MemoryConsent</c>, <c>AiAwarenessService.Consent</c>), which are the
/// same owner-pending posture — and it means a fresh process is always closed, not merely a fresh
/// install. Persisting them is an owner decision because a persisted value reads as one.</para>
/// </summary>
public sealed record AiEffectPermissions
{
    private readonly IReadOnlySet<AiCommandKind> _allowed;

    private AiEffectPermissions(bool masterEnabled, IReadOnlySet<AiCommandKind> allowed)
    {
        MasterEnabled = masterEnabled;
        _allowed = allowed;
    }

    /// <summary>Nothing admitted: master off, zero per-effect allowances. The only default.</summary>
    public static AiEffectPermissions NoneAdmitted { get; } =
        new(false, new HashSet<AiCommandKind>());

    /// <summary>Upstream's master gate (<c>AllowAiToControlEffects</c>). While it is off, no per-effect tick admits anything.</summary>
    public bool MasterEnabled { get; }

    /// <summary>
    /// The grid's rows, in upstream's order. A kind missing from every row would be a kind the
    /// user cannot see or change; <c>AiEffectPermissionsTests</c> pins that every
    /// <see cref="AiCommandKind"/> appears exactly once.
    /// </summary>
    public static IReadOnlyList<AiEffectPermissionRow> Rows { get; } =
    [
        new("Flash", "Flash", [AiCommandKind.FlashImage]),
        new("Video", "Video", [AiCommandKind.Video]),
        new("Audio", "Audio", [AiCommandKind.Audio]),
        new("Bubbles", "Bubbles", [AiCommandKind.Bubbles]),
        new("Subliminal", "Subliminal", [AiCommandKind.Subliminal]),
        new("Overlay", "Overlay", [AiCommandKind.Spiral, AiCommandKind.Pink]),
        new("LockCard", "Lock card", [AiCommandKind.MantraLockscreen]),
        new("Bounce", "Bounce text", [AiCommandKind.Bounce]),
        new("Haptic", "Haptic", [AiCommandKind.Haptic]),
        new("GetBackToMe", "Get back to me", [AiCommandKind.GetBackToMe]),
    ];

    /// <summary>
    /// The per-effect gate, in the shape <see cref="AiExecutionGates.IsEffectAllowed"/> takes.
    /// It answers the PER-EFFECT question only: the master switch is a separate gate at both the
    /// validation and the dispatch layer, and folding it in here would make a master-off refusal
    /// report the effect's name instead of <c>"master"</c>.
    /// </summary>
    public bool IsAllowed(AiCommandKind kind) => _allowed.Contains(kind);

    /// <summary>True when the row's kinds are admitted. A row admits all of its kinds or none.</summary>
    public bool IsRowAllowed(AiEffectPermissionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Kinds.All(_allowed.Contains);
    }

    /// <summary>Flip the master switch. Per-effect ticks are REMEMBERED across a master off/on, which is upstream's behaviour: <c>ChkCapEffects_Changed</c> writes only <c>AllowAiToControlEffects</c> and hides the panel (<c>MainWindow.Patreon.cs:1476-1478</c>).</summary>
    public AiEffectPermissions WithMaster(bool enabled) =>
        enabled == MasterEnabled ? this : new AiEffectPermissions(enabled, _allowed);

    /// <summary>Tick or untick one row.</summary>
    public AiEffectPermissions WithRow(AiEffectPermissionRow row, bool allowed)
    {
        ArgumentNullException.ThrowIfNull(row);
        var next = new HashSet<AiCommandKind>(_allowed);
        foreach (var kind in row.Kinds)
        {
            if (allowed)
            {
                next.Add(kind);
            }
            else
            {
                next.Remove(kind);
            }
        }

        return new AiEffectPermissions(MasterEnabled, next);
    }
}
