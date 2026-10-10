using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.CirceEmotes.cs: the
    /// animated emote avatar. CCP Default, Bambi Sleep and Sissy play the avatar0 cel set
    /// (Resources/avatar0_emotes, picked through avatar_emotes_registry.json); a mod that ships
    /// resources/emotes/set{N}/emotes.json plays its own. Same rules as WPF: a weighted idle
    /// rotation (emotes.json idleRotation, never the clip already on screen), every clip plays
    /// ONCE and crossfades (fadeMs) into the next, a click plays one clickEmotes clip (3 s cooldown,
    /// 2 s minimum hold on the clip it interrupts), the set's layout delta rides EffAvatar*.
    ///
    /// <para>XamlAnimatedGif is WPF-only; the layers here are fed by <see cref="GifPlayer"/>
    /// (SkiaSharp SKCodec) from one <see cref="FrameClock"/> at 30 fps that honours each GIF
    /// frame's own delay. Only two clips are ever open: the one on screen and the one fading.</para>
    ///
    /// <para>Motion: WPF's CirceEmotes has no MotionLevel gate (the clips are the avatar itself, not
    /// an effect), so neither does this. A map entry whose GIF is missing or will not decode is
    /// skipped: the tube keeps the frame it has, or falls back to the still avatar, never blank.</para>
    ///
    /// <para>ponytail: not ported yet - the speech-driven half (talk clips sized by clip_timing.json
    /// windows, the stemPrefix / mood reaction, expressive non-verbals): its callers live in the
    /// Speech partial, which has not crossed. clipScale per-clip multipliers (empty for avatar0).</para>
    /// </summary>
    public partial class AvatarTubeWindow
    {
        private const int EmoteMinHoldMs = 2000;       // WPF CirceMinHoldMs
        private const int EmoteClickCooldownMs = 3000; // WPF CirceClickCooldownMs

        private bool _emoteMode;
        private string? _emoteFolder;                   // bare embedded folder, or an absolute mod path
        private int _emoteFadeMs = 1000;
        private readonly List<(string clip, int weight)> _emoteIdle = new();
        private readonly List<string> _emoteClickClips = new();
        private readonly HashSet<string> _emoteBadClips = new(StringComparer.OrdinalIgnoreCase);
        private bool _emoteHasLayout;
        private double _emoteScaleMul = 1.0;
        private int _emoteOffX, _emoteOffY, _emoteDetX, _emoteDetY;

        private Image? _emoteImgA, _emoteImgB, _emoteActiveImg, _emoteFadeOutImg;
        private GifPlayer? _emotePlayerA, _emotePlayerB;
        private string? _emoteCurrentClip;
        private string? _emotePendingClip;
        private long _emoteClipStartMs;
        private long _emoteFadeStartMs = -1;
        private long _emoteClickCooldownMs = long.MinValue / 2;
        private long _emoteLastTickMs;
        private readonly Stopwatch _emoteWatch = Stopwatch.StartNew();
        private FrameClock? _emoteClock;

        private static List<(string modId, int set, string folder)>? _emoteRegistry;

        internal bool EmoteModeActive => _emoteMode;
        internal string? EmoteCurrentClip => _emoteCurrentClip;
        internal Image? EmoteActiveLayer => _emoteActiveImg;
        internal int EmoteActiveFrameIndex => PlayerFor(_emoteActiveImg)?.FrameIndex ?? -1;

        private bool EmoteLayoutActive => _emoteMode && _emoteHasLayout;

        // ---------------------------------------------------------------- engage / leave

        /// <summary>Call after any avatar/mod/set setup to enter, leave or switch the emote set.</summary>
        private void TryUpdateEmoteMode()
        {
            try
            {
                var folder = ResolveEmoteFolder();
                if (!string.IsNullOrEmpty(folder))
                {
                    if (!_emoteMode) EnterEmoteMode(folder!);
                    else if (!string.Equals(folder, _emoteFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        LeaveEmoteMode();
                        EnterEmoteMode(folder!);
                    }
                }
                else if (_emoteMode) LeaveEmoteMode();
            }
            catch (Exception ex) { Log.Warning("Emote mode toggle failed: {Error}", ex.Message); }
        }

        /// <summary>Mod-local set{N} folder first, then the embedded registry (WPF ResolveEmoteFolder).</summary>
        private string? ResolveEmoteFolder()
        {
            var modId = CoreMods.ActiveModId;
            if (string.IsNullOrEmpty(modId)) return null;
            try
            {
                var installed = CoreMods.ActiveModPackage?.InstalledPath;
                if (!string.IsNullOrEmpty(installed))
                {
                    var dir = Path.Combine(installed, "resources", "emotes", $"set{_currentAvatarSet}");
                    if (File.Exists(Path.Combine(dir, "emotes.json"))) return dir;
                }
            }
            catch { }
            foreach (var e in LoadEmoteRegistry())
                if (string.Equals(e.modId, modId, StringComparison.OrdinalIgnoreCase) && e.set == _currentAvatarSet)
                    return e.folder;
            return null;
        }

        private static List<(string modId, int set, string folder)> LoadEmoteRegistry()
        {
            if (_emoteRegistry != null) return _emoteRegistry;
            var list = new List<(string, int, string)>();
            try
            {
                using var s = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/avatar_emotes_registry.json"));
                using var doc = JsonDocument.Parse(s, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (doc.RootElement.TryGetProperty("sets", out var sets) && sets.ValueKind == JsonValueKind.Array)
                    foreach (var it in sets.EnumerateArray())
                    {
                        var modId = Str(it, "modId");
                        var folder = Str(it, "folder");
                        if (!string.IsNullOrEmpty(modId) && !string.IsNullOrEmpty(folder)
                            && it.TryGetProperty("avatarSet", out var set) && set.TryGetInt32(out var n))
                            list.Add((modId!, n, folder!));
                    }
            }
            catch (Exception ex) { Log.Warning("avatar_emotes_registry.json unreadable: {Error}", ex.Message); }
            _emoteRegistry = list;
            return list;
        }

        /// <summary>Engage a set. False (and the still avatar stays) when its map or every idle clip is missing.</summary>
        internal bool EnterEmoteMode(string folder)
        {
            _emoteImgA ??= this.FindControl<Image>("ImgAvatarAnimated");
            _emoteImgB ??= this.FindControl<Image>("ImgAvatarAnimatedB");
            if (_emoteImgA == null || _emoteImgB == null) return false;
            if (_emoteMode) LeaveEmoteMode();

            _emoteFolder = folder;
            _emoteBadClips.Clear();
            if (!LoadEmoteMap()) { _emoteFolder = null; return false; }

            _emoteMode = true;
            _emoteCurrentClip = null;
            _emotePendingClip = null;
            _emoteFadeStartMs = -1;
            _emoteActiveImg = null;
            _emoteImgA.Opacity = 0; _emoteImgB.Opacity = 0;

            if (!DoEmoteCrossfade(PickWeightedIdle()))
            {
                // No idle clip of the map decodes: never trade the still avatar for an empty tube.
                LeaveEmoteMode();
                return false;
            }

            _poseTimer.Stop();               // no legacy 4-pose rotation under the clips
            _imgAvatar.IsVisible = false;
            var imgB = this.FindControl<Image>("ImgAvatarB");
            if (imgB != null) imgB.IsVisible = false;
            ApplyTubeLayoutOffsets();        // this set's layout delta
            if (IsVisible) StartEmoteClock();
            Log.Information("Emote mode engaged ({Folder}, set {Set}).", folder, _currentAvatarSet);
            return true;
        }

        private void LeaveEmoteMode()
        {
            bool was = _emoteMode;
            _emoteMode = false;
            _emoteHasLayout = false;
            _emoteFolder = null;
            _emoteCurrentClip = null;
            _emotePendingClip = null;
            _emoteFadeStartMs = -1;
            StopEmoteClock();
            ClearEmoteLayer(_emoteImgA);
            ClearEmoteLayer(_emoteImgB);
            _emoteActiveImg = null;
            _emoteFadeOutImg = null;
            _imgAvatar.IsVisible = true;
            if (was)
            {
                ApplyTubeLayoutOffsets();
                int loaded = 0;
                foreach (var pose in _avatarPoses) if (pose != null) loaded++;
                if (loaded > 1) _poseTimer.Start();
            }
        }

        // ---------------------------------------------------------------- the map

        private bool LoadEmoteMap()
        {
            try
            {
                using var s = OpenEmoteFile("emotes.json");
                if (s == null) { Log.Warning("emotes.json not found in {Folder}", _emoteFolder); return false; }
                using var doc = JsonDocument.Parse(s, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var j = doc.RootElement;

                _emoteFadeMs = Int(j, "fadeMs") ?? 1000;

                _emoteIdle.Clear();
                if (j.TryGetProperty("idleRotation", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var it in arr.EnumerateArray())
                        _emoteIdle.Add((Str(it, "clip") ?? "idle", Int(it, "weight") ?? 1));
                if (_emoteIdle.Count == 0) _emoteIdle.Add(("idle", 1));
                // A map naming a clip that does not ship must not blank the tube: drop it (WPF
                // build_pack CULL rule, and the drone pack's culled s2_boot).
                _emoteIdle.RemoveAll(x => !EmoteClipExists(x.clip));
                if (_emoteIdle.Count == 0) return false;

                _emoteClickClips.Clear();
                if (j.TryGetProperty("clickEmotes", out var ce) && ce.ValueKind == JsonValueKind.Array)
                    foreach (var it in ce.EnumerateArray())
                        if (it.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(it.GetString()))
                            _emoteClickClips.Add(it.GetString()!);
                if (_emoteClickClips.Count == 0)
                    _emoteClickClips.AddRange(new[] { "shy", "sultry", "adoring", "tender", "blowkiss" });
                _emoteClickClips.RemoveAll(c => !EmoteClipExists(c));

                _emoteHasLayout = false;
                if (j.TryGetProperty("layout", out var ly) && ly.ValueKind == JsonValueKind.Object)
                {
                    _emoteScaleMul = Dbl(ly, "scale") ?? 1.0;
                    _emoteOffX = Int(ly, "offsetX") ?? 0;
                    _emoteOffY = Int(ly, "offsetY") ?? 0;
                    _emoteDetX = Int(ly, "detachedX") ?? 0;
                    _emoteDetY = Int(ly, "detachedY") ?? 0;
                    _emoteHasLayout = true;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load emotes.json for {Folder}", _emoteFolder);
                return false;
            }
        }

        private Stream? OpenEmoteFile(string name)
        {
            if (string.IsNullOrEmpty(_emoteFolder)) return null;
            try
            {
                if (Path.IsPathRooted(_emoteFolder))
                {
                    var p = Path.Combine(_emoteFolder, name);
                    return File.Exists(p) ? File.OpenRead(p) : null;
                }
                var uri = new Uri($"avares://CCP.Avalonia/Resources/{_emoteFolder}/{name}");
                return AssetLoader.Exists(uri) ? AssetLoader.Open(uri) : null;
            }
            catch { return null; }
        }

        private bool EmoteClipExists(string clip)
        {
            if (string.IsNullOrEmpty(_emoteFolder) || string.IsNullOrEmpty(clip)) return false;
            try
            {
                if (Path.IsPathRooted(_emoteFolder)) return File.Exists(Path.Combine(_emoteFolder, clip + ".gif"));
                return AssetLoader.Exists(new Uri($"avares://CCP.Avalonia/Resources/{_emoteFolder}/{clip}.gif"));
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------- playback

        private string PickWeightedIdle()
        {
            var usable = _emoteIdle.Where(x => !_emoteBadClips.Contains(x.clip)).ToList();
            if (usable.Count == 0) return "idle";
            var pool = usable.Where(x => x.clip != _emoteCurrentClip).ToList();
            if (pool.Count == 0) pool = usable;
            int total = pool.Sum(x => Math.Max(1, x.weight));
            int r = _random.Next(total);
            foreach (var (clip, weight) in pool)
            {
                r -= Math.Max(1, weight);
                if (r < 0) return clip;
            }
            return pool[0].clip;
        }

        /// <summary>
        /// Starts <paramref name="clip"/> on the idle layer and fades it in over the one on screen.
        /// False when the clip is missing or will not decode; nothing on screen changes then.
        /// </summary>
        internal bool DoEmoteCrossfade(string clip)
        {
            if (!_emoteMode || string.IsNullOrEmpty(clip) || _emoteImgA == null || _emoteImgB == null) return false;
            if (_emoteBadClips.Contains(clip)) return false;

            GifClip? gif = null;
            try
            {
                using var s = OpenEmoteFile(clip + ".gif");
                if (s != null) gif = GifClip.Open(s);
            }
            catch { gif = null; }
            if (gif == null || gif.FrameCount == 0)
            {
                gif?.Dispose();
                _emoteBadClips.Add(clip);
                Log.Warning("[EMOTE] clip {Clip} missing or undecodable in {Folder}; skipped", clip, _emoteFolder);
                return false;
            }

            // A fade still running: finish it now, so only two clips are ever open.
            if (_emoteFadeStartMs >= 0) FinishEmoteFade();

            var outImg = _emoteActiveImg;
            var inImg = ReferenceEquals(outImg, _emoteImgA) ? _emoteImgB : _emoteImgA;
            ClearEmoteLayer(inImg);
            var player = new GifPlayer(clip, gif);
            if (ReferenceEquals(inImg, _emoteImgA)) _emotePlayerA = player; else _emotePlayerB = player;
            inImg.Source = player.Bitmap;
            inImg.IsVisible = true;

            long now = _emoteWatch.ElapsedMilliseconds;
            if (outImg != null && outImg.Source != null && _emoteFadeMs > 0)
            {
                inImg.Opacity = 0;
                _emoteFadeOutImg = outImg;
                _emoteFadeStartMs = now;
            }
            else
            {
                inImg.Opacity = 1;
                if (outImg != null) ClearEmoteLayer(outImg);
            }
            _emoteActiveImg = inImg;
            _emoteCurrentClip = clip;
            _emoteClipStartMs = now;
            return true;
        }

        private void FinishEmoteFade()
        {
            if (_emoteActiveImg != null) _emoteActiveImg.Opacity = 1;
            if (_emoteFadeOutImg != null && !ReferenceEquals(_emoteFadeOutImg, _emoteActiveImg))
                ClearEmoteLayer(_emoteFadeOutImg);
            _emoteFadeOutImg = null;
            _emoteFadeStartMs = -1;
        }

        private GifPlayer? PlayerFor(Image? img)
            => img == null ? null : ReferenceEquals(img, _emoteImgA) ? _emotePlayerA
             : ReferenceEquals(img, _emoteImgB) ? _emotePlayerB : null;

        private void ClearEmoteLayer(Image? img)
        {
            if (img == null) return;
            img.Source = null;
            img.Opacity = 0;
            img.IsVisible = false;
            if (ReferenceEquals(img, _emoteImgA)) { _emotePlayerA?.Dispose(); _emotePlayerA = null; }
            else if (ReferenceEquals(img, _emoteImgB)) { _emotePlayerB?.Dispose(); _emotePlayerB = null; }
        }

        /// <summary>The idle rotation step: the next idle clip, skipping any that fail to open.</summary>
        private void AdvanceEmote()
        {
            if (!_emoteMode) return;
            if (_talkSeqActive) return;   // the talk timer owns transitions mid-line (WPF OnCirceClipCompleted)
            for (int tries = 0; tries <= _emoteIdle.Count; tries++)
                if (DoEmoteCrossfade(PickWeightedIdle())) return;
            // Nothing else opens: replay the clip on screen rather than freeze or blank.
            if (_emoteCurrentClip != null && !_emoteBadClips.Contains(_emoteCurrentClip))
            {
                var keep = _emoteCurrentClip;
                _emoteCurrentClip = null;
                DoEmoteCrossfade(keep);
            }
        }

        /// <summary>
        /// WPF CirceClickEmote: one affectionate clickEmotes clip, once per 3 s, held off until the
        /// clip it interrupts has had 2 s on screen. False when not in emote mode or cooling down.
        /// </summary>
        internal bool EmoteClick()
        {
            if (!_emoteMode) return false;
            long now = _emoteWatch.ElapsedMilliseconds;
            if (now - _emoteClickCooldownMs < EmoteClickCooldownMs) return false;
            var pool = _emoteClickClips.Where(c => !_emoteBadClips.Contains(c) && c != _emoteCurrentClip).ToList();
            if (pool.Count == 0) pool = _emoteClickClips.Where(c => !_emoteBadClips.Contains(c)).ToList();
            if (pool.Count == 0) return false;
            _emoteClickCooldownMs = now;
            StopTalkSequence();   // a click interrupts any in-flight spoken line
            var clip = pool[_random.Next(pool.Count)];
            if (_emoteCurrentClip != null && now - _emoteClipStartMs < EmoteMinHoldMs) _emotePendingClip = clip;
            else if (!DoEmoteCrossfade(clip)) return false;
            return true;
        }

        /// <summary>One clock step: advance the clips, the crossfade, the pending click and the rotation.</summary>
        internal void StepEmotes(TimeSpan elapsed)
        {
            if (!_emoteMode) return;
            if (_emotePlayerA?.Advance(elapsed) == true) _emoteImgA?.InvalidateVisual();
            if (_emotePlayerB?.Advance(elapsed) == true) _emoteImgB?.InvalidateVisual();

            long now = _emoteWatch.ElapsedMilliseconds;
            if (_emoteFadeStartMs >= 0 && _emoteActiveImg != null)
            {
                double p = Math.Clamp((now - _emoteFadeStartMs) / (double)Math.Max(1, _emoteFadeMs), 0, 1);
                _emoteActiveImg.Opacity = p;
                if (_emoteFadeOutImg != null) _emoteFadeOutImg.Opacity = 1 - p;
                if (p >= 1) FinishEmoteFade();
            }

            if (_emotePendingClip != null && now - _emoteClipStartMs >= EmoteMinHoldMs)
            {
                var c = _emotePendingClip;
                _emotePendingClip = null;
                if (c != _emoteCurrentClip) DoEmoteCrossfade(c);
                return;
            }

            if (_emotePendingClip == null && PlayerFor(_emoteActiveImg)?.IsComplete == true)
                AdvanceEmote();
        }

        private void StartEmoteClock()
        {
            if (!_emoteMode) return;
            if (_emoteClock == null)
            {
                _emoteClock = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
                _emoteClock.Tick += (_, _) =>
                {
                    long now = _emoteWatch.ElapsedMilliseconds;
                    var dt = TimeSpan.FromMilliseconds(Math.Clamp(now - _emoteLastTickMs, 0, 250));
                    _emoteLastTickMs = now;
                    try { StepEmotes(dt); }
                    catch (Exception ex) { Log.Debug("[EMOTE] step failed: {Error}", ex.Message); }
                };
            }
            _emoteLastTickMs = _emoteWatch.ElapsedMilliseconds;
            _emoteClock.Start();
        }

        private void StopEmoteClock() => _emoteClock?.Stop();

        /// <summary>OnClosed: stop the clock and free both clips.</summary>
        private void ReleaseEmotes()
        {
            StopEmoteClock();
            ClearEmoteLayer(_emoteImgA);
            ClearEmoteLayer(_emoteImgB);
            _emoteMode = false;
        }

        // ---------------------------------------------------------------- json helpers

        private static string? Str(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int? Int(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
               ? (v.TryGetInt32(out var i) ? i : (int)Math.Round(v.GetDouble())) : null;

        private static double? Dbl(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    }
}
