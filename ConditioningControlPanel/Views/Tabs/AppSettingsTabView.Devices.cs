using System.Windows.Controls;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Settings door · DEVICES — compatibility passthroughs.
    ///
    /// <para>Every name below is one a MainWindow partial already addressed before Phase 2, when
    /// these controls lived on the Lab tab (<c>LabTab.X</c>), inside the Collapsed voice block on
    /// the Takeover tab (<c>BambiTakeoverTab.X</c>) or in the dead LegacyDashboardHost
    /// (<c>SettingsTab.X</c>). Re-publishing them here is what let those handlers move host without
    /// being rewritten: the only change on the MainWindow side is the object before the dot.</para>
    ///
    /// <para>Nothing here is lazy. <c>SectionDevices</c> is a XAML-instantiated child of the host,
    /// so these are non-null from construction — <c>LoadSettings</c> and the webcam tracker
    /// callbacks dereference several of them without a guard, exactly as they always did.</para>
    ///
    /// <para>Workshop-cell precedent: <see cref="CompanionTabView"/> lines 90-115.</para>
    /// </summary>
    public partial class AppSettingsTabView
    {
        // ── webcam engine bar (was LabTab.*) ────────────────────────────────────────────────
        /// <summary>Tutorial/automation anchor for the whole webcam block.</summary>
        internal Border LabWebcamEngineBar => SectionDevices.LabWebcamEngineBar;
        internal CheckBox ChkBlinkRecalWebcamBar => SectionDevices.ChkBlinkRecalWebcamBar;
        internal ComboBox CmbWebcamDevice => SectionDevices.CmbWebcamDevice;
        internal Button BtnWebcamDeviceRefresh => SectionDevices.BtnWebcamDeviceRefresh;
        internal ComboBox CmbWebcamMonitor => SectionDevices.CmbWebcamMonitor;
        internal Border LabTrackerPill => SectionDevices.LabTrackerPill;
        internal Ellipse LabTrackerDot => SectionDevices.LabTrackerDot;
        internal TextBlock TxtWebcamDebugStatus => SectionDevices.TxtWebcamDebugStatus;
        internal Button BtnWebcamReviewPrivacy => SectionDevices.BtnWebcamReviewPrivacy;
        internal Button BtnWebcamDebugCalibrate => SectionDevices.BtnWebcamDebugCalibrate;
        internal Button BtnWebcamDebugQuickRecal => SectionDevices.BtnWebcamDebugQuickRecal;
        internal Button BtnWebcamDebugStart => SectionDevices.BtnWebcamDebugStart;
        internal Button BtnWebcamDebugTrackerTest => SectionDevices.BtnWebcamDebugTrackerTest;
        internal Button BtnWebcamRevokeConsent => SectionDevices.BtnWebcamRevokeConsent;
        internal CheckBox ChkWebcamDebugCursor => SectionDevices.ChkWebcamDebugCursor;
        internal CheckBox ChkWebcamDriftCorrection => SectionDevices.ChkWebcamDriftCorrection;
        internal TextBlock TxtWebcamDebugCounters => SectionDevices.TxtWebcamDebugCounters;
        internal TextBlock TxtWebcamDebugLog => SectionDevices.TxtWebcamDebugLog;
        internal CheckBox ChkRestrictGazeToCalScreen => SectionDevices.ChkRestrictGazeToCalScreen;

        // ── voice input modes (was BambiTakeoverTab.TakeoverVoiceInputLegacy.*) ─────────────
        internal CheckBox ChkSpeechWakeWord => SectionDevices.ChkSpeechWakeWord;
        internal TextBox TxtSpeechWakeWords => SectionDevices.TxtSpeechWakeWords;
        internal CheckBox ChkSpeechPushToTalk => SectionDevices.ChkSpeechPushToTalk;
        internal TextBlock TxtPttKey => SectionDevices.TxtPttKey;
        internal Button BtnSetPttKey => SectionDevices.BtnSetPttKey;

        // ── microphone hardware (was Features/WebcamFeatureControl, now retired) ────────────
        internal ComboBox CmbMicDevice => SectionDevices.CmbMicDevice;
        internal Slider SliderWakePrecision => SectionDevices.SliderWakePrecision;
        internal Slider SliderCmdPrecision => SectionDevices.SliderCmdPrecision;
        internal CheckBox ChkHeadphones => SectionDevices.ChkHeadphones;

        // ── panic key (was SettingsTab.*, in the dead LegacyDashboardHost) ─────────────────
        internal CheckBox ChkNoPanic => SectionDevices.ChkNoPanic;
        internal Button BtnPanicKey => SectionDevices.BtnPanicKey;

        // ── global hotkey launchers (labels repainted by MainWindow.SessionIO.cs) ──────────
        internal TextBlock TxtChatShortcutLabelDevices => SectionDevices.TxtChatShortcutLabelDevices;
        internal TextBlock TxtCameraShortcutLabelDevices => SectionDevices.TxtCameraShortcutLabelDevices;
    }
}
