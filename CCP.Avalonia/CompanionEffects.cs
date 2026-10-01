using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Commands;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Launcher;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// CompanionBrain's CommandExecutor and ActivitiesProvider on this head (WPF App.xaml.cs:2767-2781).
    /// The gate is Core's AiCommandService - the same master switch + Lab entitlement, per-effect
    /// toggles, video-feature gate and 3-per-reply cap WPF runs. Only the drawing is here. A command
    /// whose surface this head lacks (spiral: no spiral overlay port) stays unseeded, so Core refuses
    /// it as "didn't fire" - never a pretend success. An overlay surface also refuses where
    /// click-through windows are impossible (X11Overlay unavailable), as the overlays themselves do.
    /// ponytail: no Live actions panel on this head, so AiCommandService.LiveActionSink stays unseeded.
    /// </summary>
    internal static class CompanionEffects
    {
        internal static AiCommandService Commands { get; } = new();

        private static Window? Host =>
            (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        private static bool OnUi(Func<bool> f) => Dispatcher.UIThread.CheckAccess() ? f() : Dispatcher.UIThread.Invoke(f);

        /// <summary>A desktop-effect start, through MainShellWindow.StartEffect so the portal panic
        /// shortcut is bound first on native Wayland. A start deferred behind that bind is dropped if a
        /// panic or switch-off came meanwhile, and reported as requested; inline, it reports what showed.</summary>
        internal static bool Start(Func<bool> show) => OnUi(() =>
        {
            var generation = AiCommandService.CancelGeneration;
            bool? shown = null;
            MainShellWindow.StartEffect(() =>
            {
                if (generation == AiCommandService.CancelGeneration && AiEffectControlGate.IsOnNow) shown = show();
                else shown = false;
            });
            return shown ?? true;
        });

        /// <summary>An overlay surface: refused without a host or click-through support.</summary>
        private static bool Overlay(Action<Window> show) => OnUi(() =>
            X11Overlay.IsAvailable && Host is not null && Start(() =>
            {
                if (Host is not { } host) return false;
                show(host);
                return true;
            }));

        internal static void Seed()
        {
            CompanionBrain.CommandExecutor = commands =>
            {
                Commands.BeginBatch();
                foreach (var command in commands) Commands.ExecuteCommand(command);
            };
            CompanionBrain.EffectScheduler = execute =>
            {
                if (Dispatcher.UIThread.CheckAccess()) execute();
                else Dispatcher.UIThread.Post(execute);
            };
            CompanionBrain.ActivitiesProvider = Activities;

            FlashImageCommand.Surface = (amount, durationMs, size) =>
                Overlay(host => FlashOverlay.TriggerOnce(host, amount, durationMs, size));
            BubbleCommand.Surface = (start, frequency) => start
                ? Overlay(host => BubbleOverlay.Start(host, frequency))
                : OnUi(() => { BubbleOverlay.Stop(); return true; });
            SubliminalCommand.Surface = (text, opacity) => Overlay(host =>
            {
                SubliminalOverlay.Show(host, text, opacity);
                CoreProgression.AddXP(10, "Subliminal");   // WPF FlashSubliminalCustom
            });
            BounceCommand.Surface = (on, words) => on
                ? Overlay(host => BouncingTextOverlay.Start(host, words))
                : OnUi(() => { BouncingTextOverlay.Stop(); return true; });
            MantraLockScreenCommand.Surface = (phrase, repeats) => Start(() =>
            {
                if (LockCardWindow.IsAnyOpen()) return false;   // never stacks (WPF), so nothing showed
                LockCardWindow.ShowOnAllMonitors(phrase, repeats, strictMode: true, isTest: false,
                    voiceMode: CoreSettings.Current.LockCardVoiceMode);
                return LockCardWindow.IsAnyOpen();
            });
            // Without WPF's BypassLevelCheck the tint shows only while the engine does; refused otherwise.
            PinkCommand.Surface = (on, intensity) => Start(() =>
            {
                var s = CoreSettings.Current;
                s.PinkFilterOpacity = intensity;
                s.PinkFilterEnabled = on;
                if (Host is { } host) PinkFilterOverlay.Refresh(host);
                CoreSettings.Save();
                return !on || PinkFilterOverlay.IsShowing;
            });
            MediaCommand.VideoSurface = path => Start(() =>
                CoreEngine.Video?.Trigger(path == null ? null : false, path) == true);
            MediaCommand.AudioSurface = PlayAudio;
            GetBackToMeCommand.AiProvider = () => App.Ai;
            GetBackToMeCommand.SaySurface = (text, aiGenerated) =>
                Views.AvatarTube.AvatarTubeWindow.Live?.RunOnAvatar(() =>
                    Views.AvatarTube.AvatarTubeWindow.Live?.GigglePriority(text, playSound: true, aiGenerated: aiGenerated));
        }

        /// <summary>True once the clip is really playing, false when refused or it never starts.</summary>
        internal static async Task<bool> PlayAudio(string path)
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CoreAudio.PlayOneShot(path, 1f, "ai", _ => started.TrySetResult(true), () => started.TrySetResult(false));
            return await Task.WhenAny(started.Task, Task.Delay(10_000)) == started.Task && started.Task.Result;
        }

        /// <summary>WPF CompanionActivities.Current: the launcher games this head can open, then the pages.</summary>
        internal static IReadOnlyList<CompanionActivity> Activities()
        {
            var result = new List<CompanionActivity>();
            foreach (var card in LauncherWindow.VisibleCards)
            {
                var dest = LauncherWindow.Destinations[card.Id];
                result.Add(new("game." + card.Id, Loc.Get(card.TitleKey), Loc.Get(card.BlurbKey),
                    () => CompanionPages.CanOfferGame(true, dest.Locked(), true,
                        card.RequiresAccount && !CoreAccount.IsLoggedIn, CoreSettings.Current.AudioOnlySession),
                    () => OnUi(() =>
                    {
                        if (LauncherWindow.Instance is { } launcher) { launcher.OpenPanel(dest.Open); return true; }
                        if (MainShellWindow.Current is not { } panel) return false;
                        panel.ShowFromTray();
                        dest.Open(panel);
                        return true;
                    })));
            }
            result.AddRange(CompanionPages.Current(() => MainShellWindow.Current != null,
                entry => OnUi(() =>
                {
                    if (MainShellWindow.Current is not { } shell) return false;
                    SettingsPaletteWindow.Navigate(shell, entry);
                    return true;
                })));
            return result;
        }
    }
}
