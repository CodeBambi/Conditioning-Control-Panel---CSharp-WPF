using System;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The head half of WPF 7.1.5 <c>CompanionService</c> (ai#5, progression#47) over Core
/// <see cref="CompanionCore"/>: the Leech drain timer (2 s) and the active-time timer (1 min) WPF's
/// constructor started, the CoreModsHooks companion seams the mod service calls on a switch, and the
/// level-up reactions WPF spread over three listeners - MainWindow.OnCompanionLevelUp (tray toast at
/// max level and every 10th, sound on the same levels), GamificationBridge (best_friends at 25) and the
/// tube's OnCompanionLevelUp (a priority line) - plus the tube's switch greeting.
/// ponytail: the tube's RefreshCompanionDisplay (caption/art for the new companion) is lane tube's
/// (T12, avatar sets + companion art swap); it should listen to CompanionCore.Switched / LevelUp.
/// </summary>
internal static class CompanionHead
{
    private static DispatcherTimer? _drainTimer, _activeTimer;
    private static bool _started;

    /// <summary>WPF GamificationBridge.BestFriendsCompanionLevel.</summary>
    internal const int BestFriendsCompanionLevel = 25;

    internal static void Start()
    {
        if (_started) return;
        _started = true;
        try { CompanionCore.EnsurePerkSeeded(); } catch (Exception ex) { Log.Debug("Companion perk seed: {E}", ex.Message); }

        CoreModsHooks.ActiveCompanionProvider = () => CompanionCore.ActiveCompanion;
        CoreModsHooks.SwitchCompanion = id => CompanionCore.SwitchCompanion(id);

        _drainTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(CompanionCore.DrainIntervalSeconds) };
        _drainTimer.Tick += (_, _) => { try { CompanionCore.DrainTick(DateTime.UtcNow); } catch (Exception ex) { Log.Debug("Leech drain: {E}", ex.Message); } };
        _drainTimer.Start();
        _activeTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _activeTimer.Tick += (_, _) => { try { CompanionCore.UpdateActiveTime(); } catch { } };
        _activeTimer.Start();

        CompanionCore.LevelUp += (id, level) => Dispatcher.UIThread.Post(() => OnLevelUp(id, level));
        CompanionCore.Switched += id => Dispatcher.UIThread.Post(() => OnSwitched(id));
        Log.Information("CompanionHead started. Active companion: {Companion}", CompanionCore.ActiveDef.Name);
    }

    /// <summary>The companion's display name the way WPF's tube shows it (CCP Default names its own companion).</summary>
    internal static string DisplayName(CompanionId id)
    {
        var raw = CompanionDefinition.GetById(id).Name;
        return CoreMods.MakeModAware(raw);
    }

    internal static void OnLevelUp(CompanionId id, int level)
    {
        var name = DisplayName(id);
        bool max = level == CompanionProgress.MaxLevel;
        // MainWindow.OnCompanionLevelUp: tray toast (WPF copy verbatim) + sound on max / every 10th.
        try
        {
            if (max) OsNotifications.Show("MAX LEVEL!", $"{name} has reached maximum level!");
            else if (level % 10 == 0) OsNotifications.Show("Companion Level Up!", $"{name} reached Level {level}!");
        }
        catch (Exception ex) { Log.Debug("companion level toast: {E}", ex.Message); }
        if (max || level % 10 == 0) ProgressionHead.PlayLevelUpSound();

        // GamificationBridge.OnCompanionLevelUp.
        if (level >= BestFriendsCompanionLevel)
            try { App.Achievements?.TryUnlock("best_friends"); } catch { }

        // AvatarTubeWindow.Reactions.cs:542, the line: the tube's own handler speaks it (queue rules).
        CoreTubeEvents.RaiseCompanionLevelUp(name, level, max);
    }

    /// <summary>WPF SwitchToCompanionAvatar + OnCompanionSwitched: the tube swaps art, drops its queue
    /// and greets once rapid cycling settles (600 ms debounce lives in the tube).</summary>
    internal static void OnSwitched(CompanionId id)
    {
        try { Views.AvatarTube.AvatarTubeWindow.Live?.SwitchToCompanionAvatar(id); }
        catch (Exception ex) { Log.Debug("companion avatar swap: {E}", ex.Message); }
        CoreTubeEvents.RaiseCompanionSwitched(DisplayName(id));
    }
}
