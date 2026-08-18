using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Pins the Audio Layers window's construction. The XAML declares <c>SliderMaster</c> (with
/// <c>Value="70"</c> and a <c>ValueChanged</c> handler) BEFORE <c>TxtMaster</c>, so BAML raises
/// the handler while the label is still null. With <c>_loading</c> defaulting to false the
/// handler ran, dereferenced the missing label and killed the constructor — so the window had
/// never opened for anyone, which the field read as "the Audio Layers button does nothing".
/// The fix starts <c>_loading</c> as true until <c>LoadMasterControls</c> owns it.
///
/// <para>The hazard only fires when <c>App.Settings.Current</c> is non-null (the handler's
/// settings guard sits before the label dereference), which is why these tests install a real
/// <see cref="AppSettings"/> instance: without one, even the broken constructor survives and
/// the regression goes invisible to the suite.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class LayeredAudioWindowTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    /// <summary>
    /// Installs a settings service (no disk IO: uninitialized instance, only Current populated)
    /// into App.Settings for the duration of <paramref name="body"/>, then restores the previous
    /// value so the shared STA collection never sees the stand-in.
    /// </summary>
    private static void WithAppSettings(AppSettings settings, Action body)
    {
        var prop = typeof(App).GetProperty("Settings",
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("App.Settings property not found");
        var previous = prop.GetValue(null);

        var service = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
        typeof(SettingsService).GetProperty(nameof(SettingsService.Current))!
            .SetValue(service, settings);

        prop.SetValue(null, service);
        try { body(); }
        finally { prop.SetValue(null, previous); }
    }

    [Fact]
    public void Constructor_survives_baml_raising_slider_events_before_the_label_exists()
    {
        OnStaThread(() => WithAppSettings(new AppSettings { AudioLayersMasterVolume = 55 }, () =>
        {
            var win = new LayeredAudioWindow();
            try
            {
                // LoadMasterControls ran after InitializeComponent and synced the label itself.
                var label = Assert.IsType<TextBlock>(win.FindName("TxtMaster"));
                var slider = Assert.IsType<Slider>(win.FindName("SliderMaster"));
                Assert.Equal(55, (int)slider.Value);
                Assert.Equal("55%", label.Text);
            }
            finally
            {
                win.Close();
            }
        }));
    }
}
