using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Camera;

/// <summary>
/// The camera's persisted state, and it is CONSENT METADATA ONLY.
///
/// <para><b>Three fields, and the count is the contract rather than an omission.</b>
/// <c>client/docs/capability-inventory.md</c>'s "Webcam, face, and gaze tracking" section allows
/// this product to persist consent metadata, engine/model identity, and compact numeric
/// calibration — and nothing else, ever. This slice has no engine and no calibration, so it
/// persists neither; they arrive with the code that produces them, which is the same rule
/// <c>Haptics/HapticSettingsDocument.cs</c> records for the settings a provider has not yet
/// brought.</para>
///
/// <para><b>What can never appear here, stated so a later edit has to argue with it.</b> A frame, a
/// crop, a tensor, a landmark, a gaze sample, an image path, a thumbnail, a preview, a capture
/// timestamp series, or anything else derived from what the camera saw. Those are memory-only by
/// the same section's rule; this document is the ONLY file this capability writes, so keeping it to
/// consent metadata is what makes "never saved to disk" a property of the build. Upstream persists
/// exactly the same three facts — <c>WebcamConsentGiven</c>, <c>WebcamConsentVersion</c>,
/// <c>WebcamConsentDate</c> (<c>Dialogs/WebcamConsentDialog.xaml.cs:147-149</c>) — and clears all
/// three on revoke (<c>Services/Webcam/WebcamTrackingService.cs:1065-1067</c>).</para>
/// </summary>
public sealed class CameraConsentDocument
{
    /// <summary>The document this build writes into &lt;dataDir&gt;.</summary>
    public const string FileName = "camera-consent.json";

    /// <summary>The schema version this build writes (persistence contract §1).</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Whether consent was given at all. <b>False in every file a fresh install writes</b>, and it is
    /// the field that upstream's own start path refuses on
    /// (<c>Services/Webcam/WebcamTrackingService.cs:112</c>).
    /// </summary>
    public bool Granted { get; set; }

    /// <summary>
    /// The privacy-contract version the user actually agreed to, verbatim as it was at the time.
    ///
    /// <para><b>This field is the entire mechanism behind re-consent</b>, and it only works because
    /// what is stored is what the USER saw rather than what the build currently promises. Upstream's
    /// comparison is <c>s.WebcamConsentVersion == ConsentVersion</c>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:113</c>) over a constant bumped <i>"any time we
    /// add a new sensor type, broaden what the camera observes, or change what numbers are stored"</i>
    /// (<c>:93-98</c>); a stored value that tracked the constant would make every bump a no-op and
    /// silently carry an old agreement onto a new promise.</para>
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>When consent was given, UTC. Upstream stores the same
    /// (<c>Dialogs/WebcamConsentDialog.xaml.cs:149</c>). Metadata about the DECISION, never about
    /// anything a camera observed.</summary>
    public DateTimeOffset? GrantedUtc { get; set; }

    /// <summary>Unknown-member preservation (persistence contract §6 — required on every persisted
    /// model), so a build that does not yet know about the engine identity and calibration fields
    /// this section will eventually allow cannot eat them on a round trip.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// The consent DECISION, as a value.
///
/// <para><b>The behaviour being preserved is upstream's four-screen consent dialog, and not one line
/// of its implementation.</b> That dialog gates its Enable button on three separate acknowledgements
/// AND a typed confirmation string — <c>allChecked &amp;&amp; typed</c>, where <c>typed</c> is
/// <c>TxtConfirm.Text.Trim() == "ENABLE"</c>
/// (<c>Dialogs/WebcamConsentDialog.xaml.cs:113-119</c>) — with none of the boxes pre-checked
/// (<c>Dialogs/WebcamConsentDialog.xaml:190</c>). Modelling that as a value rather than as a window
/// is what lets the RULE be proved without a display server, and it is what stops a future settings
/// page, command line, migration or test granting consent by writing a bool.</para>
/// </summary>
/// <param name="AcknowledgedFramesNeverTransmitted">
/// Upstream's first box: <i>"I understand camera frames stay on this device and are never
/// transmitted."</i> (<c>Dialogs/WebcamConsentDialog.xaml:193</c>).
/// </param>
/// <param name="AcknowledgedFramesNeverSaved">
/// Upstream's second box: <i>"I understand frames are processed in memory and never saved to
/// disk."</i> (<c>Dialogs/WebcamConsentDialog.xaml:196</c>).
/// </param>
/// <param name="AcknowledgedOthersPresentConsent">
/// Upstream's third box: <i>"I am the only person in front of this camera, or others present have
/// given consent."</i> (<c>Dialogs/WebcamConsentDialog.xaml:199</c>) — the one acknowledgement that
/// is about somebody who is not the user, and the one a "just tick to continue" flow would lose.
/// </param>
/// <param name="TypedConfirmation">
/// What the user typed into the confirmation box. Compared TRIMMED and case-sensitively against
/// <see cref="CameraConsent.ConfirmationWord"/>, which is upstream's own comparison
/// (<c>Dialogs/WebcamConsentDialog.xaml.cs:118</c>).
/// </param>
public readonly record struct CameraConsentRequest(
    bool AcknowledgedFramesNeverTransmitted,
    bool AcknowledgedFramesNeverSaved,
    bool AcknowledgedOthersPresentConsent,
    string? TypedConfirmation)
{
    /// <summary>Whether every gate upstream's Enable button waits for has been passed. Anything less
    /// is not consent and cannot be granted.</summary>
    public bool IsComplete =>
        AcknowledgedFramesNeverTransmitted
        && AcknowledgedFramesNeverSaved
        && AcknowledgedOthersPresentConsent
        && string.Equals(
            TypedConfirmation?.Trim(), CameraConsent.ConfirmationWord, StringComparison.Ordinal);
}

/// <summary>
/// The consent contract this build asks a user to agree to, and the one place that decides whether
/// a stored agreement still counts.
/// </summary>
public static class CameraConsent
{
    /// <summary>
    /// The privacy-contract version. <b>Bump it in the same change that broadens what the camera
    /// observes, what is derived from it, or what is stored</b> — upstream's rule, in its own words
    /// (<c>Services/Webcam/WebcamTrackingService.cs:93-98</c>, and the file-header restatement at
    /// <c>:34-36</c>: <i>"Any change that broadens what the camera observes ... MUST bump
    /// WebcamTrackingService.ConsentVersion so users re-consent on next launch"</i>).
    ///
    /// <para>It is <c>"1.0"</c> — upstream's value (<c>:98</c>) — because the promise is upstream's
    /// promise: frames stay on the device, frames are never written to disk, and audio is never
    /// opened. A different string here would re-prompt every user for a contract that has not
    /// changed.</para>
    /// </summary>
    public const string CurrentVersion = "1.0";

    /// <summary>The word the user must type to complete the consent gate
    /// (<c>Dialogs/WebcamConsentDialog.xaml.cs:118</c>).</summary>
    public const string ConfirmationWord = "ENABLE";

    /// <summary>
    /// Whether a stored agreement still counts, and WHY NOT when it does not. Null means current.
    ///
    /// <para><b>Upstream's predicate, split into two answers.</b> It returns one bool over both
    /// fields (<c>Services/Webcam/WebcamTrackingService.cs:108-114</c>) and then logs the two apart
    /// (<c>:814-815</c>) — because "you have not been asked yet" and "we changed the promise, so we
    /// are asking again" are different things to say to a person. The OUTCOME is identical: both
    /// refuse, and neither ever lets a camera be touched.</para>
    /// </summary>
    public static CapabilityReason? Evaluate(CameraConsentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!document.Granted)
        {
            return new CapabilityReason(
                CameraReasonCodes.CameraConsentAbsent,
                "no camera consent has been given on this installation, so NOTHING WAS ASKED OF ANY CAMERA — not "
                + "even which cameras exist. Webcam gaze is opt-in and off by default, and consent is given "
                + "explicitly and can be withdrawn at any time");
        }

        if (!string.Equals(document.Version, CurrentVersion, StringComparison.Ordinal))
        {
            return new CapabilityReason(
                CameraReasonCodes.CameraConsentStale,
                $"camera consent was given against privacy contract '{document.Version}' and this build's contract "
                + $"is '{CurrentVersion}', so the earlier agreement does not cover what this build would do and "
                + "consent must be given again. NOTHING WAS ASKED OF ANY CAMERA in the meantime");
        }

        return null;
    }

    /// <summary>
    /// Write a completed consent onto the document. Returns false and changes NOTHING when the
    /// request has not passed every gate — the port of upstream's disabled Enable button
    /// (<c>Dialogs/WebcamConsentDialog.xaml.cs:119</c>), moved from a button's
    /// <c>IsEnabled</c> to the only code that can write the grant, so an unfinished consent cannot
    /// be committed by any caller at all.
    ///
    /// <para><b>It does not start a camera and there is nothing here that could.</b> Upstream's own
    /// grant handler is explicit about the same separation — <i>"Persist consent. Camera stays
    /// closed — user must explicitly enable a feature toggle in the Lab card to actually start
    /// tracking."</i> (<c>Dialogs/WebcamConsentDialog.xaml.cs:142-144</c>).</para>
    /// </summary>
    public static bool TryGrant(CameraConsentDocument document, CameraConsentRequest request, DateTimeOffset grantedUtc)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!request.IsComplete)
        {
            return false;
        }

        document.Granted = true;
        document.Version = CurrentVersion;
        document.GrantedUtc = grantedUtc;
        return true;
    }

    /// <summary>
    /// Withdraw consent: all three fields cleared, which is upstream's revoke
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1065-1067</c>). Clearing the VERSION as well as
    /// the flag matters — a revoked document that kept its version would re-grant itself the moment
    /// something set the flag back.
    /// </summary>
    public static void Revoke(CameraConsentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Granted = false;
        document.Version = string.Empty;
        document.GrantedUtc = null;
    }
}
