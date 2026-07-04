using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class FlashFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private readonly IFlashService? _flash;
    private readonly ISessionService? _session;
    private readonly ILogger<FlashFeatureControl>? _logger;
    private bool _isLoading = true;

    public FlashFeatureControl()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _flash = App.Services.GetService<IFlashService>();
        _session = App.Services.GetService<ISessionService>();
        _logger = App.Services.GetRequiredService<ILogger<FlashFeatureControl>>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        LoadFromSettings();
        if (_settings.Current is INotifyPropertyChanged inpc)
            inpc.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current is INotifyPropertyChanged inpc)
            inpc.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void LoadFromSettings()
    {
        if (_settings.Current is not { } s) return;
        _isLoading = true;
        try
        {
            ChkEnable.IsChecked = s.FlashEnabled;
            SliderFrequency.Value = s.FlashFrequency;
            TxtFrequency.Text = s.FlashFrequency.ToString();
            SliderImages.Value = s.SimultaneousImages;
            TxtImages.Text = s.SimultaneousImages.ToString();
            SliderMaxOnScreen.Value = s.HydraLimit;
            TxtMaxOnScreen.Text = s.HydraLimit.ToString();
            ChkClickable.IsChecked = s.FlashClickable;
            ChkCorruption.IsChecked = s.CorruptionMode;
            SliderHydraMultiply.Value = s.HydraMultiplyCount;
            TxtHydraMultiply.Text = s.HydraMultiplyCount.ToString();
            ChkHydraLinked.IsChecked = s.HydraLinkedTiming;
            ChkGlow.IsChecked = s.FlashGlowEnabled;
            ChkFlashGazePop.IsChecked = s.FlashGazePopEnabled;
            ChkFlashGazeLinger.IsChecked = s.FlashGazeLingerEnabled;
            SliderFlashLingerMs.Value = s.FlashGazeLingerExtensionMs;
            TxtFlashLingerMs.Text = Loc.GetF("label_0_1_ms", s.FlashGazeLingerExtensionMs, " ");
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Reload on any flash-related property; the set is small.
        if (e.PropertyName == nameof(AppSettings.FlashEnabled) ||
            e.PropertyName == nameof(AppSettings.FlashFrequency) ||
            e.PropertyName == nameof(AppSettings.SimultaneousImages) ||
            e.PropertyName == nameof(AppSettings.HydraLimit) ||
            e.PropertyName == nameof(AppSettings.FlashClickable) ||
            e.PropertyName == nameof(AppSettings.CorruptionMode) ||
            e.PropertyName == nameof(AppSettings.HydraMultiplyCount) ||
            e.PropertyName == nameof(AppSettings.HydraLinkedTiming) ||
            e.PropertyName == nameof(AppSettings.FlashGlowEnabled) ||
            e.PropertyName == nameof(AppSettings.FlashGazePopEnabled) ||
            e.PropertyName == nameof(AppSettings.FlashGazeLingerEnabled) ||
            e.PropertyName == nameof(AppSettings.FlashGazeLingerExtensionMs))
        {
            Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void ChkFlashGazePop_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.FlashGazePopEnabled = ChkFlashGazePop.IsChecked ?? false;
        _settings.Save();
    }

    private void ChkFlashGazeLinger_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.FlashGazeLingerEnabled = ChkFlashGazeLinger.IsChecked ?? false;
        _settings.Save();
    }

    private void SliderFlashLingerMs_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtFlashLingerMs.Text = Loc.GetF("label_0_1_ms", v, " ");
        _settings.Current.FlashGazeLingerExtensionMs = v;
        _settings.Save();
    }

    private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var on = ChkEnable.IsChecked ?? false;
        _settings.Current.FlashEnabled = on;
        _settings.Save();

        LiveApply(on);
    }

    private void SliderFrequency_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtFrequency.Text = v.ToString();
        _settings.Current.FlashFrequency = v;
        _settings.Save();

        try { _flash?.RefreshSchedule(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Flash frequency change: RefreshSchedule failed"); }
    }

    private void LiveApply(bool on)
    {
        if (_session?.State != SessionState.Running || _flash == null) return;

        try
        {
            if (on && !_flash.IsRunning) _flash.Start();
            else if (!on && _flash.IsRunning) _flash.Stop();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Flash enable toggle: live apply failed");
        }
    }

    private void SliderImages_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtImages.Text = v.ToString();
        _settings.Current.SimultaneousImages = v;
        _settings.Save();
    }

    private void SliderMaxOnScreen_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtMaxOnScreen.Text = v.ToString();
        _settings.Current.HydraLimit = v;
        _settings.Save();
    }

    private void ChkClickable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.FlashClickable = ChkClickable.IsChecked ?? false;
        _settings.Save();
    }

    private void ChkCorruption_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var newValue = ChkCorruption.IsChecked ?? false;
        _logger?.LogDebug("ChkCorruption_Changed: setting CorruptionMode from {Old} to {New}", _settings.Current.CorruptionMode, newValue);
        _settings.Current.CorruptionMode = newValue;
        _settings.Save();
        _logger?.LogDebug("ChkCorruption_Changed: settings saved, CorruptionMode is now {Value}", _settings.Current.CorruptionMode);
    }

    private void SliderHydraMultiply_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtHydraMultiply.Text = v.ToString();
        _settings.Current.HydraMultiplyCount = v;
        _settings.Save();
    }

    private void ChkHydraLinked_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.HydraLinkedTiming = ChkHydraLinked.IsChecked ?? false;
        _settings.Save();
    }

    private void ChkGlow_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.FlashGlowEnabled = ChkGlow.IsChecked ?? false;
        _settings.Save();
    }
}
