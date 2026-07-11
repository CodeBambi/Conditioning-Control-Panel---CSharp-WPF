using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private CirceEmoteEngine? _circeEngine;
        private bool _circeMapValid;
        private readonly List<CirceRegistryEntry> _circeRegistry = new();

        private sealed record CirceRegistryEntry(string? ModId, int AvatarSet, string Folder, bool Fallback);

        // Emote registry resolution --------------------------------------------------------

        private void TryUpdateCirceEmoteMode()
        {
            if (_portraitMode) return;

            string? folder = ResolveCirceFolderForCurrentState();
            bool wanted = !string.IsNullOrEmpty(folder);
            _logger?.LogDebug("TryUpdateCirceEmoteMode: wanted={Wanted}, folder={Folder}, currentMode={Mode}, engineActive={Active}",
                wanted, folder, _circeEmoteMode, _circeEngine?.IsActive);

            if (wanted)
            {
                if (!_circeEmoteMode)
                {
                    _circeEmoteMode = true;
                    EnterCirceEmoteMode(folder!);
                }
                else if (_circeEngine?.IsActive != true)
                {
                    EnterCirceEmoteMode(folder!);
                }
                else if (!string.Equals(_circeEngine?.CurrentFolder, folder, StringComparison.OrdinalIgnoreCase))
                {
                    // Switched to a different registered pose while already animating: re-engage.
                    LeaveCirceEmoteMode();
                    _circeEmoteMode = true;
                    EnterCirceEmoteMode(folder!);
                }
                else
                {
                    // Already animating the right folder; make sure the static avatar is hidden
                    // and the animated layers are visible (OnModChanged may have shown the static image).
                    if (ImgAvatar != null) ImgAvatar.IsVisible = false;
                    if (ImgAvatarB != null) ImgAvatarB.IsVisible = false;
                    ReassertCirceEmoteVisuals();
                }
            }
            else if (_circeEmoteMode)
            {
                LeaveCirceEmoteMode();
            }
        }

        private string? ResolveCirceFolderForCurrentState()
        {
            LoadCirceRegistryIfNeeded();

            string modId = App.Services.GetService<global::ConditioningControlPanel.IModService>()?.ActiveMod?.Id
                           ?? "builtin-bambisleep";

            var exact = _circeRegistry.FirstOrDefault(x => !x.Fallback
                && x.ModId != null
                && x.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase)
                && x.AvatarSet == _currentAvatarSet);
            return exact?.Folder;
        }

        private void LoadCirceRegistryIfNeeded()
        {
            if (_circeRegistry.Count > 0) return;
            try
            {
                var loader = App.Services.GetRequiredService<IAssetLoader>();
                var json = loader.ReadTextAsync(new Uri("avares://CCP.Avalonia/Assets/avatar_emotes_registry.json")).Result;
                var root = JObject.Parse(json);
                if (root["sets"] is JArray sets)
                {
                    foreach (var item in sets)
                    {
                        var m = (string?)item["modId"];
                        var a = (int?)item["avatarSet"];
                        var f = (string?)item["folder"];
                        if (!string.IsNullOrEmpty(m) && !string.IsNullOrEmpty(f))
                            _circeRegistry.Add(new CirceRegistryEntry(m, a.GetValueOrDefault(1), f, false));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load avatar emote registry");
            }
        }

        /// <summary>Registered emote sets for the active mod, ascending ([1] for BS/Sissy, [1..4] for Circe).</summary>
        private int[] EmoteSetsForActiveMod()
        {
            LoadCirceRegistryIfNeeded();
            var modId = App.Services.GetService<global::ConditioningControlPanel.IModService>()?.ActiveMod?.Id;
            if (string.IsNullOrEmpty(modId)) return Array.Empty<int>();
            return _circeRegistry
                .Where(e => e.ModId != null
                    && e.ModId.Equals(modId, StringComparison.OrdinalIgnoreCase)
                    && !e.Fallback)
                .Select(e => e.AvatarSet).Distinct().OrderBy(x => x).ToArray();
        }

        /// <summary>
        /// True for a mod whose ONLY avatar is one animated emote set — these drop the level picker /
        /// nav arrows entirely (BambiSleep, Sissy). Multi-set emote mods (Circe's 4 poses) and non-emote
        /// mods return false.
        /// </summary>
        private bool IsSingleEmoteAvatarMod(out int set)
        {
            var sets = EmoteSetsForActiveMod();
            if (sets.Length == 1) { set = sets[0]; return true; }
            set = 0;
            return false;
        }

        private void EnterCirceEmoteMode(string folder)
        {
            if (ImgAvatarAnimated == null || ImgAvatarAnimatedB == null)
            {
                _logger?.LogWarning("EnterCirceEmoteMode aborted: animated image layers are null");
                return;
            }

            _logger?.LogInformation("Entering Circe emote mode for folder {Folder}", folder);

            _circeEngine ??= new CirceEmoteEngine(ImgAvatarAnimated, ImgAvatarAnimatedB,
                App.Services.GetRequiredService<IAssetLoader>(),
                App.Services.GetRequiredService<ILogger<CirceEmoteEngine>>(), _random);

            _circeEngine.Leave();
            _circeEngine.ClipStarted += OnCirceClipStarted;

            _ = Task.Run(async () =>
            {
                bool ok = await _circeEngine.EnterAsync(folder).ConfigureAwait(false);
                _logger?.LogInformation("Circe emote EnterAsync returned {Ok} for folder {Folder}", ok, folder);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ok)
                    {
                        _circeMapValid = true;
                        ImgAvatar!.IsVisible = false;
                        ImgAvatarB!.IsVisible = false;
                        ReassertCirceEmoteVisuals();
                        _logger?.LogInformation("Circe emote mode active for folder {Folder}", folder);
                    }
                    else
                    {
                        _circeMapValid = false;
                        _circeEmoteMode = false;
                        ImgAvatarAnimated.IsVisible = false;
                        ImgAvatarAnimatedB.IsVisible = false;
                        ImgAvatar!.IsVisible = true;
                        _logger?.LogWarning("Circe emote mode failed for folder {Folder}; falling back to static avatar", folder);
                    }
                });
            });
        }

        private void LeaveCirceEmoteMode()
        {
            _circeEmoteMode = false;
            _circeMapValid = false;
            _circeEngine?.Leave();
            if (ImgAvatarAnimated != null) ImgAvatarAnimated.IsVisible = false;
            if (ImgAvatarAnimatedB != null) ImgAvatarAnimatedB.IsVisible = false;
            if (ImgAvatar != null) ImgAvatar.IsVisible = true;
            if (ImgAvatarB != null) ImgAvatarB.IsVisible = false;

            // Mirror WPF AvatarTubeWindow.CirceEmotes.cs LeaveCirceEmoteMode (which calls
            // ApplyTubeLayoutOffsets to revert). Now that _circeEngine.IsActive is false,
            // EmoteLayoutActive is false, so this recomputes the neutral pose scale + AvatarBorder
            // margin (mod-only layout, no emote delta). Also drops any per-image emote transform.
            ApplyAvatarTransform(_currentAvatarSet);
            ApplyTubeLayoutOffsets();
            ClearEmoteImageTransform(ImgAvatarAnimated);
            ClearEmoteImageTransform(ImgAvatarAnimatedB);
        }

        private void ReassertCirceEmoteVisuals()
        {
            if (_circeEngine?.IsActive != true || ImgAvatarAnimated == null) return;

            // Mirror WPF AvatarTubeWindow.CirceEmotes.cs ReassertCirceEmoteVisuals: the emote
            // layers share the SAME scale + AvatarBorder.Margin as the neutral pose. WPF achieves
            // this by putting ONE ScaleTransform(EffAvatarScale) as a LayoutTransform on all three
            // avatar images (ApplyTubeLayoutOffsets, AvatarTubeWindow.Avatar.cs:712-715, comment
            // "Circe emote crossfade layer must match") and the offset on AvatarBorder.Margin —
            // there is NO separate per-image emote transform in WPF.
            //
            // The prior Avalonia port diverged here: it applied a SEPARATE RenderTransform via
            // ApplyImageTransform(baseScale=1.0 * scaleMul, raw offX/offY) onto ImgAvatarAnimated
            // and ImgAvatarAnimatedB. baseScale=1.0 IGNORED the mod avatar scale (so the emote
            // rendered at 1.0*0.855 instead of modScale*0.855 => LARGER than the pose), the
            // center-origin ScaleTransform lifted the bottom-anchored figure ("shifted up"), and
            // the raw _emoteOffY TranslateTransform double-shifted on top of the margin. That is
            // the owner-repro "enlarged + shifted up" bug (Engram obs #6).
            //
            // Fix: route through the SHARED pose path. EffAvatarScale/Offset now fold the emote
            // delta (AvatarTubeWindow.Avatar.cs EmoteLayoutActive + Eff* accessors, mirroring WPF
            // CirceEmotes.cs:388-393), so ApplyAvatarTransform scales all three images identically
            // (via their common AvatarBorder, bottom-center origin) and ApplyTubeLayoutOffsets
            // positions the border with the folded margin. The emote frames now land at exactly
            // the pose's scale/position, with only the layout delta applied.
            ApplyAvatarTransform(_currentAvatarSet);
            ApplyTubeLayoutOffsets();

            // Drop any per-image RenderTransform left by the old ApplyImageTransform path so the
            // emote images rely exclusively on the shared AvatarBorder transform. The XAML
            // declares a no-op TranslateTransform(Y=0) on these layers; reset to that.
            ClearEmoteImageTransform(ImgAvatarAnimated);
            ClearEmoteImageTransform(ImgAvatarAnimatedB);

            ImgAvatarAnimated.IsVisible = true;
            if (ImgAvatarAnimatedB != null) ImgAvatarAnimatedB.IsVisible = true;

            if (ImgAvatar != null) ImgAvatar.IsVisible = false;
            if (ImgAvatarB != null) ImgAvatarB.IsVisible = false;
        }

        // Reset an animated emote layer to the XAML-declared no-op TranslateTransform(0,0) so no
        // stale ScaleTransform/TranslateTransform from a prior divergent path enlarges or shifts
        // the frame. The shared AvatarBorder transform (ApplyAvatarTransform) owns all scale/offset.
        private static void ClearEmoteImageTransform(Image? img)
        {
            if (img == null) return;
            img.RenderTransform = new TranslateTransform(0, 0);
            img.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        private void OnCirceClipStarted(string clip)
        {
            if (_circeEngine?.HasLayout != true || ImgAvatarAnimated == null) return;
            ReassertCirceEmoteVisuals();
            LogContentGeometry("circe-clip");
        }

        private void CircePlayEmote(string? emotionLineId, string? audioPath, string? text, string? mood)
        {
            if (!_circeEmoteMode || _circeEngine == null)
                return;
            _circeEngine.PlayEmote(emotionLineId, audioPath, text, mood);
        }

        private void CircePause()
        {
            _circeEngine?.Pause();
        }

        private void CirceResume()
        {
            _circeEngine?.Resume();
        }

        internal bool CirceClickEmote()
        {
            if (!_circeEmoteMode || _circeEngine == null) return false;
            return _circeEngine.ClickEmote();
        }

        private void RefreshVoiceLines()
        {
            // Legacy WPF voice-line cache invalidation. The LibVLC-backed IAudioPlayer
            // loads clips on demand; nothing to refresh here.
        }

        private bool LoadCirceMap()
        {
            // Replaced by async registry + engine enter; kept as sync probe.
            TryUpdateCirceEmoteMode();
            return _circeMapValid;
        }
    }
}
