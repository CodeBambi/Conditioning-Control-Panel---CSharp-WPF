using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The head half of the progression feed (wave A lane progress):
/// <list type="bullet">
/// <item>WPF <c>ActivityTracker</c> (Services/Tracking/ActivityTracker.cs): seeds
/// <see cref="ActivityIdle.IdleSecondsProvider"/> with GetLastInputInfo on Windows.</item>
/// <item>WPF <c>StartConditioningTimeTracker</c>'s 1 s DispatcherTimer: drives <see cref="ConditioningTime.Tick"/>
/// (it returns at once while the engine is stopped).</item>
/// <item>WPF <c>MainWindow.OnLevelUp</c> (MainWindow.xaml.cs:791): the tray toast and <c>PlayLevelUpSound</c>.
/// ponytail: no level-up spark burst at the XP bar (CelebrateLevelUp's FireBurstAt needs an FX layer on the
/// shell) and no avatar change by level (the tube has no UpdateAvatarForLevel yet).</item>
/// </list>
/// </summary>
internal static class ProgressionHead
{
    private static DispatcherTimer? _conditioningTimer;
    private static Action? _stopLevelUpSound;

    internal static void Start()
    {
        if (OperatingSystem.IsWindows()) ActivityIdle.IdleSecondsProvider = GetIdleSeconds;

        if (_conditioningTimer == null)
        {
            _conditioningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _conditioningTimer.Tick += (_, _) => ConditioningTime.Tick(DateTime.Now);
            _conditioningTimer.Start();
        }
        // hunt3 IC5 (WPF SyncConditioningTimeToServerAsync): every 15 minutes of a run and on its stop,
        // through the ordinary profile push (signed-in, loaded, cooldown and backoff rules are its own).
        ConditioningTime.SyncRequested = () => { _ = AccountSeed.Sync?.PushAsync("conditioning time"); };

        ProgressionBank.LevelUp += level => Dispatcher.UIThread.Post(() => OnLevelUp(level));
    }

    /// <summary>WPF MainWindow.OnLevelUp: toast, then the sound (the header flash lives in HeroFx).</summary>
    internal static void OnLevelUp(int newLevel)
    {
        try { OsNotifications.Show(Loc.Get("toast_level_up_title"), Loc.GetF("toast_level_up_body", newLevel)); }
        catch (Exception ex) { Log.Debug("level-up toast: {E}", ex.Message); }
        PlayLevelUpSound();
        try { App.DiscordRpc?.UpdateLevel(newLevel); } catch (Exception ex) { Log.Debug("presence level: {E}", ex.Message); }   // WPF ProgressionService:309
    }

    /// <summary>WPF PlayLevelUpSound verbatim: the first lvup.mp3 found, master volume ^1.5 x 0.2625,
    /// any previous level-up sound stopped first.</summary>
    internal static void PlayLevelUpSound()
    {
        try
        {
            var soundPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "lvup.mp3"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "lvlup.mp3"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sounds", "lvlup.mp3"),
            };
            var soundPath = soundPaths.FirstOrDefault(File.Exists);
            if (soundPath == null)
            {
                Log.Debug("Level up sound not found in any of: {Paths}", string.Join(", ", soundPaths));
                return;
            }
            try { _stopLevelUpSound?.Invoke(); } catch { }
            var masterVolume = CoreSettings.Current.MasterVolume / 100f;
            var curvedVolume = (float)Math.Pow(masterVolume, 1.5) * 0.2625f;
            _stopLevelUpSound = CoreAudio.PlayStoppable(soundPath, Math.Max(0.01f, curvedVolume), "level-up");
        }
        catch (Exception ex) { Log.Warning("Failed to play level up sound: {Error}", ex.Message); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>WPF ActivityTracker.GetIdleSeconds: a failed call reads as active.</summary>
    private static int GetIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        var idleMillis = (long)Environment.TickCount - info.dwTime;
        if (idleMillis < 0) idleMillis += (long)uint.MaxValue + 1;
        return (int)(idleMillis / 1000);
    }
}
