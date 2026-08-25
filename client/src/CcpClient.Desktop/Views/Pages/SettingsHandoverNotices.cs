namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// <b>The recorded decision NOT to migrate the shipping WPF product's settings</b>, said to the
/// user instead of only to a document — because the accident this closes was not a missing
/// feature, it was a SILENCE.
///
/// <para><b>What went wrong.</b> The shipping product keeps one 100 KB <c>settings.json</c> in
/// <c>%LOCALAPPDATA%\ConditioningControlPanel</c> (<c>Services/Settings/SettingsService.cs:105</c>
/// over <c>App.xaml.cs:157-171</c>). This port keeps its own per-module documents under its own
/// roaming data root (<c>Lifecycle/CompositionRoot.DefaultSettingsPath</c>,
/// <c>Session/SessionParticipant.cs:103-197</c>) and reads that file for nothing. An owner moving
/// across therefore meets a rack of dials that LOOK like his and are not, with no sentence
/// anywhere saying so — which is exactly how a size dial nobody remembers setting became a
/// mystery instead of a setting.</para>
///
/// <para><b>Why the answer is NOT to migrate, and this is the decision rather than a deferral.</b></para>
/// <list type="bullet">
/// <item><b>The schemas are not the same document.</b> Upstream's file is one flat
/// <c>AppSettings</c>; this port keeps twelve schema-versioned documents whose Degraded load path
/// takes a WHOLE document to defaults (<c>Persistence/PersistenceStore.cs</c>), which is the
/// reason they are separate at all (D71). A carry-over is one parity decision PER DIAL, each
/// needing its own upstream citation and its own fact — a packet, not a line.</item>
/// <item><b>A partial import is worse than none.</b> This port deliberately persists no
/// equivalent for several dials the shipping page writes — Visuals' fade and audio link
/// (<c>Session/VisualsPresetDocument.cs</c>), flash clickable, corruption mode, whisper volume
/// and ducking, mind wipe's escalation multiplier
/// (<c>Session/ScriptedSessionDials.Apply</c>'s remarks). A user told his settings came across
/// would reasonably believe ALL of them did, and would then be wrong about the ones that decide
/// what appears on his screen.</item>
/// <item><b>The file is a live document this product does not own.</b> The WPF app still ships
/// and still writes it. An import that ran once and then diverged would leave two truths and no
/// reconciliation, and this port's rule for that directory is already settled and narrower:
/// <c>Entitlement/ShippingAppDataLocation.cs</c> — "the port reads one file out of another
/// application's directory and writes nothing there, ever". <b>The settings file is not that one
/// file.</b> Nothing here opens it; this text names its FOLDER and stops.</item>
/// <item><b>And it would not have prevented the incident it came out of.</b> The owner's shipping
/// <c>ImageScale</c> is 100 and this port's <c>DefaultImageScalePercent</c> is 100
/// (<c>Session/VisualsPresetDocument.cs:58</c>), so a carry-over would have moved that dial from
/// 100 to 100. Migration is a real parity gap; it is not this incident's cause, and fixing it
/// here would have been a plausible remedy for a defect it does not touch.</item>
/// </list>
///
/// <para><b>What this file is instead.</b> Four sentences and no I/O: no probe, no existence
/// check, no read, no write, and therefore not one byte of new contact with the shipping app's
/// directory beyond the folder name <see cref="Entitlement.ShippingAppDataLocation.Resolve"/>
/// already computes for the sign-in token. Pure text on the <c>*Notices.cs</c> convention
/// (<see cref="MotionNotices"/>) so a unit fact checks what the user reads instead of a headless
/// test having to mount a window for it.</para>
/// </summary>
public static class SettingsHandoverNotices
{
    /// <summary>The module's title on the System door.</summary>
    public const string Title = "Settings and the Windows app";

    /// <summary>
    /// The whole notice. Four sentences, in the order a user needs them: what is true, what it
    /// costs him, what it does NOT cost him, and where each of the two sets of settings lives.
    ///
    /// <para>It promises no future import. A sentence that said "not yet" would be a placeholder
    /// on a screen, and the decision above is a decision.</para>
    /// </summary>
    /// <param name="clientFolder">Where THIS app keeps its documents — the composition root's real
    /// data directory, not a recomputed guess, so a build running under a harness data root tells
    /// the truth about itself.</param>
    /// <param name="shippingFolder">Where the shipping WPF app keeps its user data
    /// (<see cref="Entitlement.ShippingAppDataLocation.Resolve"/>). Named, never opened.</param>
    public static string Describe(string clientFolder, string shippingFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(shippingFolder);

        return "This app keeps its own settings and does not import the Windows app's. "
            + "Every dial here starts at this app's own default until you set it, so a dial you "
            + "tuned in the Windows app is not the dial you have here. "
            + "Your Windows app is left exactly as it is: this app never reads or writes its "
            + "settings file. "
            + $"This app's settings are in {clientFolder}; the Windows app's are in {shippingFolder}.";
    }
}
