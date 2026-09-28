using System;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>Which surface asked for the explainer, so it can open on the right page.</summary>
public enum LeashExplainRole { Holder, Leashed, Offer, Ask, Gate }

/// <summary>
/// The one door to the leash explainer. Every leash surface has a visible "?" that calls
/// <see cref="Show"/>. STUB: the explainer itself belongs to a later lane, which fills
/// <see cref="Presenter"/>; until then the press is logged and nothing opens.
/// </summary>
public static class LeashExplainHost
{
    /// <summary>What actually shows the explainer. Null until the explain lane lands.</summary>
    public static Action<LeashExplainRole>? Presenter { get; set; }

    /// <summary>Raised on every "?" press, for the suite and for anything that wants to know.</summary>
    public static event Action<LeashExplainRole>? Requested;

    public static void Show(LeashExplainRole role)
    {
        try { Requested?.Invoke(role); } catch { }
        try
        {
            if (Presenter != null) Presenter(role);
            else App.Logger?.Debug("[Leash] explainer asked for {Role}; not built yet", role);
        }
        catch (Exception ex) { App.Logger?.Debug("[Leash] explainer failed: {E}", ex.Message); }
    }
}
