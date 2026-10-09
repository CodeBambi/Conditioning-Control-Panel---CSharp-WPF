// PORTED from WPF 7.1.5 Controls/Leash/LeashExplainHost.cs: the one door to the leash explainer.
// Every leash surface has a visible "?" that calls Show. The Presenter opens LeashExplainer
// (filled by LeashExplainer itself on first use, as WPF's explain lane did).
using System;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public enum LeashExplainRole { Holder, Leashed, Offer, Ask, Gate }

public static class LeashExplainHost
{
    /// <summary>What actually shows the explainer.</summary>
    public static Action<LeashExplainRole>? Presenter { get; set; }

    /// <summary>Raised on every "?" press, for the suite and anything that wants to know.</summary>
    public static event Action<LeashExplainRole>? Requested;

    public static void Show(LeashExplainRole role)
    {
        try { Requested?.Invoke(role); } catch { }
        try
        {
            if (Presenter != null) Presenter(role);
            else Serilog.Log.Debug("[Leash] explainer asked for {Role}; no presenter", role);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] explainer failed: {E}", ex.Message); }
    }
}
