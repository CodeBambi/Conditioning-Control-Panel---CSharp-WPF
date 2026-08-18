using Avalonia.Controls;
using CcpClient.Desktop.Features.Dtrh;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// Hop 2 of the DTRH route (wpf-surface-reachability.md §3): rail door <c>Play</c> -> this
/// page -> <c>FALL IN</c> / <c>Quick Drop</c> -> <see cref="DtrhLaunch"/>. The door navigates
/// and opens nothing; the two buttons here are the port's only DTRH launchers, which is WPF's
/// one-entry rule (<c>MainWindow/MainWindow.Presets.cs:1007</c>) and matches WPF's own count:
/// <c>DtrhHostService.Launch</c> has exactly these two in-app callers
/// (<c>MainWindow.Lab.cs:239,252,321</c> plus the CLI).
///
/// <para>This class RENDERS a decision it does not make. The gate is
/// <see cref="DtrhGate"/> — a pure function proved in the unit suite — so the three branches
/// cannot be quietly collapsed here: the page switches on the decision's TYPE, and the two
/// refusal types are different types precisely so one cannot be rendered as the other.</para>
/// </summary>
public partial class PlayPage : UserControl
{
    /// <summary>WPF's band caption for the tier-2 lock, <c>hm3_rail_lock_t2</c>
    /// (<c>en.json:4842</c>), shown when an authority actually answered about this account.</summary>
    public const string NotEntitledBandTitle = "LAB ONLY";

    /// <summary>The band caption for the branch WPF does not have. It must never read like a
    /// refusal of the person: nothing was determined about them (§9 D21).</summary>
    public const string UnverifiedBandTitle = "COULD NOT VERIFY";

    public PlayPage(DtrhLaunch dtrh)
    {
        ArgumentNullException.ThrowIfNull(dtrh);
        InitializeComponent();

        // Fire-and-forget on purpose: the gate resolves asynchronously (it reads the shipping
        // app's login) and a click handler may not block the UI thread. The awaited work
        // resumes on this thread, so Decided lands here and renders below. Neither button is
        // ever disabled, in any branch — a gated press must ARRIVE (PlayTabView.xaml:503-506).
        FallInButton.Click += (_, _) => _ = dtrh.FallInAsync();
        QuickDropButton.Click += (_, _) => _ = dtrh.QuickDropAsync();

        dtrh.Decided += Render;
    }

    private void Render(DtrhGateDecision decision)
    {
        switch (decision)
        {
            case DtrhGateDecision.Proceed:
                // The hole is opening. Any band from an earlier refusal is stale the instant a
                // later press succeeds — leaving it up would tell the user they were refused
                // while the descent runs.
                GateBand.IsVisible = false;
                GateBandTitle.Text = string.Empty;
                GateBandText.Text = string.Empty;
                break;

            case DtrhGateDecision.RefusedNotEntitled refused:
                Show(NotEntitledBandTitle, refused.Message);
                break;

            case DtrhGateDecision.RefusedUnverified unverified:
                Show(UnverifiedBandTitle, unverified.Message);
                break;
        }
    }

    private void Show(string title, string message)
    {
        GateBandTitle.Text = title;
        GateBandText.Text = message;
        GateBand.IsVisible = true;
    }
}
