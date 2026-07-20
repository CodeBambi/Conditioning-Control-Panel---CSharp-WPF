namespace CcpClient.Desktop.Features;

/// <summary>
/// A-004 stable-identity quick-toggle dispatch. The ONLY dispatch key is the card's stable
/// ID (<see cref="StatusTickerParticipant.FeatureId"/> for the demonstrator); display titles
/// never participate (the first attempt's localized <c>switch (card.Title)</c> —
/// CCP.Avalonia SettingsTabView.axaml.cs:382 — is the replaced mechanism). Every gesture
/// (plain right-click on the card body, keyboard Enter, and later popup toggles/automation)
/// converges on this one resolution path.
/// Neutral cards (Visuals/System parity) simply have no entry: an unknown ID is a silent
/// no-op, matching WPF's <c>else return</c> (MainWindow.Presets.cs:818) — that is parity,
/// not error swallowing. No registry framework: one entry per toggleable card, constructed
/// at the composition seam.
/// </summary>
public sealed class QuickToggleDispatch
{
    private readonly IReadOnlyDictionary<string, Action> _byCardId;

    public QuickToggleDispatch(IReadOnlyDictionary<string, Action> byCardId)
    {
        // Ordinal: IDs are stable machine identifiers — casing/whitespace differences must
        // NOT resolve (capability-inventory: dispatch never keys on capitalization).
        _byCardId = new Dictionary<string, Action>(byCardId, StringComparer.Ordinal);
    }

    /// <summary>The stable IDs with a toggle entry (diagnostics/tests; never display text).</summary>
    public IReadOnlyCollection<string> CardIds => _byCardId.Keys.ToArray();

    /// <summary>
    /// Resolve and run the toggle for <paramref name="cardId"/>. Returns false (no-op) for
    /// null/unknown/neutral IDs — WPF <c>else return</c> parity (MainWindow.Presets.cs:818).
    /// </summary>
    public bool TryToggle(string? cardId)
    {
        if (cardId is null)
        {
            return false;
        }

        if (!_byCardId.TryGetValue(cardId, out var toggle))
        {
            return false;
        }

        toggle();
        return true;
    }
}
