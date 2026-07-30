using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Guards the dosage-vs-comfort split of the session feature lock
/// (<c>MainWindow/MainWindow.SessionFeatureLock.cs</c>, <c>Features/SessionLock.cs</c>).
///
/// While a session runs, the dials it prescribes are greyed out so the user cannot quietly
/// opt out of the dose they started; comfort controls stay live. That split is declared as
/// <c>feat:SessionLock.Owned="True"</c> attributes spread across the Features/*.xaml files,
/// which makes it easy to author but also easy to silently BREAK - deleting one attribute
/// reopens a bypass, and adding one to the wrong control takes a comfort/safety affordance
/// away from the user mid-session. Neither shows up as a compile error and neither is visible
/// in a diff review of a large XAML file.
///
/// So this suite asserts the load-bearing entries directly against the XAML source. It is
/// deliberately a TEXT/XML-level test with no WPF dependency: instantiating these controls
/// would need an STA thread and an Application, and the thing worth protecting is the
/// declaration, not the rendering.
///
/// Not exhaustive by design - it pins the calibration anchors the product owner named, the
/// never-lock safety set, and the non-obvious cases whose rationale lives in a comment that a
/// future edit could discard.
/// </summary>
public class SessionLockMarkerTests
{
    private const string FeatNs = "clr-namespace:ConditioningControlPanel.Features";
    private const string OwnedAttr = "SessionLock.Owned";

    /// <summary>
    /// Walks up from the test assembly to the repo, then into the app's Features folder.
    /// Throws with the searched path rather than silently reporting "no markers found", which
    /// would make every assertion below pass vacuously if the layout ever changes.
    /// </summary>
    private static string FeaturesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel", "Features");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate ConditioningControlPanel/Features walking up from {AppContext.BaseDirectory}");
    }

    private static XDocument Load(string file)
    {
        var path = Path.Combine(FeaturesDir(), file);
        Assert.True(File.Exists(path), $"missing feature control: {path}");
        return XDocument.Load(path);
    }

    /// <summary>All elements in the document carrying x:Name, keyed by that name.</summary>
    private static Dictionary<string, XElement> ByName(XDocument doc)
    {
        var xNs = (XNamespace)"http://schemas.microsoft.com/winfx/2006/xaml";
        var map = new Dictionary<string, XElement>();
        foreach (var el in doc.Descendants())
        {
            var name = el.Attribute(xNs + "Name")?.Value;
            if (!string.IsNullOrEmpty(name)) map[name!] = el;
        }
        return map;
    }

    private static bool IsOwned(XElement el)
    {
        // Match on local name so the test does not break if the xmlns prefix is renamed.
        var attr = el.Attributes().FirstOrDefault(a => a.Name.LocalName == OwnedAttr);
        return attr != null && string.Equals(attr.Value, "True", StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertOwned(string file, string control, bool expected)
    {
        var map = ByName(Load(file));
        Assert.True(map.ContainsKey(control), $"{file}: no control named {control}");
        Assert.Equal(expected, IsOwned(map[control]));
    }

    // ---------------------------------------------------------------------------------
    // The two anchors the product owner named explicitly: bubbles-per-minute must lock,
    // bubble volume must not.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void BubbleSpawnRate_IsLocked()
        => AssertOwned("BubblePopFeatureControl.xaml", "SliderFreq", true);

    [Fact]
    public void BubbleVolume_StaysEditable()
        => AssertOwned("BubblePopFeatureControl.xaml", "SliderVolume", false);

    // ---------------------------------------------------------------------------------
    // The named comfort exceptions. Each of these has a SessionSettings field behind it, so
    // the "lock whatever the session prescribes" rule would sweep them up by accident.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void HydraMode_StaysEditable()
        => AssertOwned("FlashFeatureControl.xaml", "ChkCorruption", false);

    [Fact]
    public void ClickToDismiss_StaysEditable()
        => AssertOwned("FlashFeatureControl.xaml", "ChkClickable", false);

    [Fact]
    public void SolidModeRenderer_StaysEditable()
        => AssertOwned("BubblePopFeatureControl.xaml", "ChkSolidMode", false);

    /// <summary>Volume is never owned, even where SessionSettings prescribes it.</summary>
    [Theory]
    [InlineData("MindWipeFeatureControl.xaml", "SliderVolume")]      // SessionSettings.MindWipeVolume
    [InlineData("SubliminalFeatureControl.xaml", "SliderWhisperVol")] // SessionSettings.WhisperVolume
    public void Volumes_StayEditable(string file, string control)
        => AssertOwned(file, control, false);

    /// <summary>
    /// Silencing audio is the change a user most often needs immediately and for reasons
    /// outside the session, which puts it on the safety side of the line like volume.
    /// </summary>
    [Fact]
    public void FlashAudioToggle_StaysEditable()
        => AssertOwned("VisualsFeatureControl.xaml", "ChkAudio", false);

    /// <summary>Strict Lock is a way out. Locking a way out is the worse bug.</summary>
    [Theory]
    [InlineData("BubbleCountFeatureControl.xaml")]
    [InlineData("LockCardFeatureControl.xaml")]
    public void StrictLock_IsNeverOwned(string file)
        => AssertOwned(file, "ChkStrict", false);

    // ---------------------------------------------------------------------------------
    // Non-obvious locks whose reasoning would otherwise only live in a comment.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The global ramp curve is the one live dial in the Intensity Ramp popup: everything else
    /// there stands down mid-session, but SessionEngine.UpdateRampingValues resolves
    /// `session.RampCurve ?? App.Settings.Current.RampCurve` on every tick, so this dropdown
    /// reshapes the running session's own ramp whenever the session leaves RampCurve unset.
    /// </summary>
    [Fact]
    public void GlobalRampCurve_IsLocked()
        => AssertOwned("IntensityRampFeatureControl.xaml", "CmbRampCurve", true);

    /// <summary>
    /// Length filters are asset-eligibility, not a rate - but exposure is rate x length, so
    /// clamping max length guts a locked "videos per hour" prescription without touching the
    /// locked rate.
    /// </summary>
    [Theory]
    [InlineData("SliderVideoMinDur")]
    [InlineData("SliderVideoMaxDur")]
    public void VideoLengthFilters_AreLocked(string control)
        => AssertOwned("VideoFeatureControl.xaml", control, true);

    /// <summary>
    /// Randomizing the attention-target count picks 1..density, which would sidestep the
    /// locked density dial if it stayed editable.
    /// </summary>
    [Fact]
    public void AttentionRandomize_IsLocked()
        => AssertOwned("VideoFeatureControl.xaml", "ChkRandomize", true);

    /// <summary>The core rate dials, one per prescribed feature.</summary>
    [Theory]
    [InlineData("FlashFeatureControl.xaml", "SliderFrequency")]
    [InlineData("SubliminalFeatureControl.xaml", "SliderPerMin")]
    [InlineData("VideoFeatureControl.xaml", "SliderPerHour")]
    [InlineData("LockCardFeatureControl.xaml", "SliderFreq")]
    [InlineData("BubbleCountFeatureControl.xaml", "SliderFreq")]
    [InlineData("MindWipeFeatureControl.xaml", "SliderFreq")]
    [InlineData("PinkFilterFeatureControl.xaml", "SliderOpacity")]
    [InlineData("SpiralFeatureControl.xaml", "SliderOpacity")]
    public void PrescribedRateDials_AreLocked(string file, string control)
        => AssertOwned(file, control, true);

    // ---------------------------------------------------------------------------------
    // Structural invariants that hold across every feature control.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// A marker on a layout container would disable everything inside it - including the
    /// comfort controls the split exists to keep live - because IsEnabled is inherited.
    /// </summary>
    [Fact]
    public void MarkersOnlySitOnLeafControls()
    {
        string[] containers = ["Border", "StackPanel", "Grid", "WrapPanel", "DockPanel",
                               "ScrollViewer", "UniformGrid", "Canvas"];
        var offenders = new List<string>();

        foreach (var path in Directory.GetFiles(FeaturesDir(), "*FeatureControl.xaml"))
            foreach (var el in XDocument.Load(path).Descendants().Where(IsOwned))
                if (containers.Contains(el.Name.LocalName))
                    offenders.Add($"{Path.GetFileName(path)}: <{el.Name.LocalName}>");

        Assert.Empty(offenders);
    }

    /// <summary>
    /// A file using the marker without declaring the namespace fails at XAML parse time, i.e.
    /// the popup throws when opened rather than at build. Catch it here instead.
    /// </summary>
    [Fact]
    public void FilesUsingTheMarkerDeclareTheNamespace()
    {
        var offenders = new List<string>();

        foreach (var path in Directory.GetFiles(FeaturesDir(), "*FeatureControl.xaml"))
        {
            var doc = XDocument.Load(path);
            if (!doc.Descendants().Any(IsOwned)) continue;

            var declared = doc.Root!.Attributes()
                .Any(a => a.IsNamespaceDeclaration && a.Value == FeatNs);
            if (!declared) offenders.Add(Path.GetFileName(path));
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Sanity gate. Every assertion above is a lookup against parsed XAML, so a wrong repo
    /// path or a mangled parse would make them all pass vacuously. Assert the sweep really
    /// does see a substantial marker set.
    /// </summary>
    [Fact]
    public void MarkerSetIsNotEmpty()
    {
        var total = Directory.GetFiles(FeaturesDir(), "*FeatureControl.xaml")
            .Sum(p => XDocument.Load(p).Descendants().Count(IsOwned));

        Assert.True(total >= 35, $"expected the dosage marker set to be substantial, saw {total}");
    }
}
