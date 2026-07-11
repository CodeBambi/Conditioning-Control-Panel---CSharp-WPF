using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.Avatar;
using ConditioningControlPanel.Core.Services.Companion;
using ConditioningControlPanel.Avalonia.Services.Mod;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CS0169 // Avalonia port: unused stub fields kept for future companion/avatar work
#pragma warning disable CS0414
#pragma warning disable CS0649

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        // Emotive portrait avatar (mod-agnostic)
        private readonly IAvatarPortraitService? _portraitService;
        private IAvatarPortraitSet? _portraitSet;
        private int _skinIndex;
        private string _currentEmotion = "neutral";
        private int _emotionPoseIndex;
        private Image? _activeImg;
        private Image? _idleImg;
        private bool _crossfadeInFlight;
        private DispatcherTimer? _emotionReturnTimer;
        private DispatcherTimer? _poseSeqTimer;
        private DispatcherTimer? _crossfadeTimer;
        private DispatcherTimer? _portraitAmbientTimer;
        private int[] _seqOrder = Array.Empty<int>();
        private int _seqStep;
        private int _seqStepMs = 1000;
        private int _seqLastMs = 2000;
        private readonly Dictionary<string, double> _audioDurCache = new();
        private readonly AvaloniaModResourceResolver? _resourceResolver;
        private AvaloniaAnimatedGif? _animatedAvatarGif;

        #region Takeover countdown bar
        // A thin pink bar under the avatar that drains toward the next random Takeover action. The
        // ticker only runs while Takeover is enabled (gated on IAutonomyService.EnabledChanged), so
        // there is no always-on render loop when the feature is idle. Mirrors the WPF AvatarTubeWindow.
        private DispatcherTimer? _takeoverCountdownTimer;
        private readonly ScaleTransform _takeoverCountdownScale = new(1, 1);
        private Core.Services.Autonomy.IAutonomyService? _countdownAutonomy;

        internal void InitTakeoverCountdownBar()
        {
            try
            {
                if (TakeoverCountdownFill != null)
                    TakeoverCountdownFill.RenderTransform = _takeoverCountdownScale;
                _countdownAutonomy = App.Services?.GetService<Core.Services.Autonomy.IAutonomyService>();
                if (_countdownAutonomy == null) return;
                _countdownAutonomy.EnabledChanged -= OnAutonomyEnabledChangedForBar;
                _countdownAutonomy.EnabledChanged += OnAutonomyEnabledChangedForBar;
                Opened -= OnOpenedStartCountdownBar;
                Opened += OnOpenedStartCountdownBar;
                if (_countdownAutonomy.IsEnabled) StartCountdownTicker();
            }
            catch { }
        }

        private void OnOpenedStartCountdownBar(object? sender, EventArgs e)
        {
            try { if (_countdownAutonomy?.IsEnabled == true) StartCountdownTicker(); } catch { }
        }

        private void OnAutonomyEnabledChangedForBar(object? sender, bool enabled)
        {
            try
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(() => OnAutonomyEnabledChangedForBar(sender, enabled));
                    return;
                }
                if (enabled) StartCountdownTicker();
                else
                {
                    _takeoverCountdownTimer?.Stop();
                    if (TakeoverCountdownBar != null) TakeoverCountdownBar.IsVisible = false;
                }
            }
            catch { }
        }

        private void StartCountdownTicker()
        {
            if (_takeoverCountdownTimer == null)
            {
                _takeoverCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _takeoverCountdownTimer.Tick += TakeoverCountdown_Tick;
            }
            _takeoverCountdownTimer.Start();
        }

        private void TakeoverCountdown_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (TakeoverCountdownBar == null) return;
                var show = _settings?.Current?.ShowTakeoverCountdownBar == true
                           && _countdownAutonomy?.IsEnabled == true;
                double? frac = show ? _countdownAutonomy?.NextRandomFireFraction : null;

                if (frac == null)
                {
                    if (TakeoverCountdownBar.IsVisible) TakeoverCountdownBar.IsVisible = false;
                    return;
                }

                if (!TakeoverCountdownBar.IsVisible) TakeoverCountdownBar.IsVisible = true;
                _takeoverCountdownScale.ScaleX = frac.Value;
            }
            catch { }
        }
        #endregion

        private double _breathPhase;
        private double _wobblePhase;
        private double _mistPhase;
        private double _speakPhase;
        private double _speakEnvPhase;

        private const double BreathAmplitude = 0.01;
        private const double WobbleAmplitudeDeg = 0.4;
        private const double SpeakWobbleDeg = 0.175;
        private const double SpeakShakePx = 0.25;
        private const double PortraitSizeScale = 0.88;
        private const double PortraitRaisePx = 30;
        private const double PortraitShiftX = 10;
        private const double LegacyAvatarMaxHeight = 306;
        private const double LegacyAvatarMaxWidth = 198;

        private const int PoseStepMs = 1000;
        private const int LastPoseLingerMs = 2000;
        private const int MinSpeakPoses = 2;
        private const int MaxSpeakPoses = 5;
        private const double ShortLineSec = 3.5;
        private const double ShortSpeedFactor = 0.5;

        private readonly string[] AvatarTitleKeys =
        {
            "avatar_title_basic_bimbo",
            "avatar_title_dumb_airhead",
            "avatar_title_synthetic_blowdoll",
            "avatar_title_perfect_fuckpuppet",
            "avatar_title_brainwashed_slavedoll",
            "avatar_title_platinum_puppet",
            "avatar_title_bambi_cow"
        };

        private static readonly string[] AffirmationEmotions =
            { "alluring", "alluring", "alluring", "entrancing", "dreamy", "teasing" };

        public void StartPoseAnimation() => _poseTimer.Start();
        public void StopPoseAnimation() => _poseTimer.Stop();

        public void SetPose(int poseNumber)
        {
            if (poseNumber < 1 || poseNumber > 4) return;
            if (_avatarPoses.Length == 0) return;
            _currentPoseIndex = poseNumber - 1;
            if (ImgAvatar != null) ImgAvatar.Source = _avatarPoses[_currentPoseIndex];
        }

        public void SetPoseInterval(TimeSpan interval)
        {
            _poseTimer.Interval = interval;
        }

        public static int GetAvatarSetForLevel(int level) => 7;
        public static bool IsAvatarSetUnlocked(int setNumber, int level) => true;

        public int[] GetUnlockedAvatarSets(int level)
        {
            // Base sets in unlock-level order (not numerical order).
            int[] setsInOrder = { 1, 2, 3, 4, 7, 5, 6 };
            var unlocked = new List<int>();
            foreach (int set in setsInOrder)
            {
                if (IsAvatarSetUnlocked(set, level) && (_modService?.IsAvatarSetSupported(set) ?? true))
                    unlocked.Add(set);
            }

            var customSets = _modService?.GetCustomAvatarSets();
            if (customSets != null)
            {
                foreach (var cs in customSets.OrderBy(c => c.UnlockLevel))
                {
                    if (IsAvatarSetUnlocked(cs.SetNumber, level) && (_modService?.IsAvatarSetSupported(cs.SetNumber) ?? true))
                        unlocked.Add(cs.SetNumber);
                }
            }

            return unlocked.ToArray();
        }

        public void UpdateAvatarForLevel(int newLevel)
        {
            int newMax = GetAvatarSetForLevel(newLevel);
            if (newMax > _maxUnlockedSet)
            {
                _maxUnlockedSet = newMax;
                _selectedAvatarSet = newMax;
                if (_settings?.Current != null)
                    _settings.Current.SelectedAvatarSet = _selectedAvatarSet;
                SwitchToAvatarSet(newMax, animate: true);
            }
            UpdateTitleDisplay(newLevel);
            UpdateNavigationArrows();
        }

        private bool HasAnimatedAvatar(int setNumber)
        {
            try
            {
                return AvaloniaBitmapHelper.LoadResource($"animated{setNumber}_1.gif") != null;
            }
            catch { return false; }
        }

        private void LoadAnimatedAvatar(int setNumber)
        {
            try
            {
                LeavePortraitMode();
                _animatedAvatarGif?.Dispose();
                _animatedAvatarGif = null;

                var resourcePath = $"animated{setNumber}_1.gif";
                var resolver = App.Services?.GetService<AvaloniaModResourceResolver>();
                var uri = resolver?.ResolveUri(resourcePath) ?? "";

                AvaloniaAnimatedGif? gif = null;
                if (uri.StartsWith("file://", StringComparison.Ordinal))
                {
                    var path = uri.Substring(7);
                    gif = AvaloniaAnimatedGif.TryCreate(path);
                }
                else if (uri.StartsWith("avares://", StringComparison.Ordinal))
                {
                    try
                    {
                        using var stream = global::Avalonia.Platform.AssetLoader.Open(new Uri(uri));
                        if (stream != null)
                        {
                            var mem = new MemoryStream();
                            stream.CopyTo(mem);
                            mem.Position = 0;
                            gif = AvaloniaAnimatedGif.TryCreate(mem);
                            if (gif == null) mem.Dispose();
                        }
                    }
                    catch { }
                }

                if (gif != null)
                {
                    _animatedAvatarGif = gif;
                    if (ImgAvatar != null) ImgAvatar.IsVisible = false;
                    if (ImgAvatarAnimated != null)
                    {
                        ImgAvatarAnimated.IsVisible = true;
                        ImgAvatarAnimated.Source = gif.Source;
                        gif.FrameRendered += (_, _) => ImgAvatarAnimated.InvalidateVisual();
                    }
                    gif.Start();
                }
                else
                {
                    var bitmap = AvaloniaBitmapHelper.LoadResource(resourcePath);
                    if (ImgAvatar != null) ImgAvatar.IsVisible = false;
                    if (ImgAvatarAnimated != null)
                    {
                        ImgAvatarAnimated.IsVisible = true;
                        ImgAvatarAnimated.Source = bitmap;
                    }
                }

                _poseTimer.Stop();
                TryUpdateCirceEmoteMode();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Failed to load animated avatar {Set}: {Error}", setNumber, ex.Message);
                _useAnimatedAvatar = false;
                if (ImgAvatar != null) ImgAvatar.IsVisible = true;
                if (ImgAvatarAnimated != null) ImgAvatarAnimated.IsVisible = false;
            }
        }

        private Bitmap[] LoadAvatarPoses(int setNumber)
        {
            var list = new List<Bitmap>();
            // Set 1 uses the legacy "avatar_poseN.png" naming; higher sets use "avatar{set}_poseN.png".
            string prefix = setNumber == 1 ? "avatar_pose" : $"avatar{setNumber}_pose";
            for (int i = 1; i <= 4; i++)
            {
                var path = $"{prefix}{i}.png";
                var bitmap = AvaloniaBitmapHelper.LoadResource(path);
                if (bitmap != null)
                {
                    list.Add(bitmap);
                }
                else
                {
                    _logger?.LogWarning("Failed to load avatar pose asset: {Path}", path);
                }
            }
            return list.ToArray();
        }

        private void RefreshAvatarAnimation()
        {
            if (!_useAnimatedAvatar) return;
            LoadAnimatedAvatar(_currentAvatarSet);
        }

        private void PauseAvatarGif()
        {
            if (_circeEmoteMode) { CircePause(); return; }
            _animatedAvatarGif?.Stop();
        }

        private void ResumeAvatarGif()
        {
            if (_circeEmoteMode) { CirceResume(); return; }
            _animatedAvatarGif?.Start();
        }

        private int _avatarSwitchGen;
        private void SwitchToAvatarSet(int setNumber, bool animate = true)
        {
            int playerLevel = _settings?.Current?.PlayerLevel ?? 1;
            if (!IsAvatarSetUnlocked(setNumber, playerLevel)) return;

            int gen = ++_avatarSwitchGen;
            _currentAvatarSet = setNumber;
            _selectedAvatarSet = setNumber;
            _useAnimatedAvatar = HasAnimatedAvatar(setNumber);

            if (_settings?.Current != null)
                _settings.Current.SelectedAvatarSet = setNumber;

            Action switchAction = () =>
            {
                // In portrait mode the "set" picks a SKIN (outfit), not a companion.
                if (!UsePortraitSystem())
                {
                    var companionId = GetCompanionForAvatarSet(setNumber);
                    if (companionId.HasValue)
                    {
                        try
                        {
                            var companionService = global::ConditioningControlPanel.Avalonia.App.Services?.GetService<ICompanionService>();
                            companionService?.SwitchCompanion(companionId.Value);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning("Failed to switch companion from avatar set change: {Error}", ex.Message);
                        }
                    }
                }

                if (UsePortraitSystem())
                {
                    if (_portraitSet == null)
                        TryEnterPortraitMode();
                    else
                    {
                        _skinIndex = _portraitSet.ClampSkin(setNumber - 1);
                        ReloadPortraitSkin();
                    }
                }
                else if (_useAnimatedAvatar)
                {
                    LoadAnimatedAvatar(setNumber);
                }
                else
                {
                    LeavePortraitMode();

                    if (ImgAvatarAnimated != null)
                    {
                        ImgAvatarAnimated.IsVisible = false;
                        ImgAvatarAnimated.Source = null;
                    }
                    if (ImgAvatar != null) ImgAvatar.IsVisible = true;

                    _avatarPoses = LoadAvatarPoses(setNumber);
                    _currentPoseIndex = 0;
                    if (ImgAvatar != null) ImgAvatar.Source = _avatarPoses.Length > 0 ? _avatarPoses[0] : null;

                    if (!_portraitMode && _avatarPoses.Length > 1) _poseTimer.Start();
                }

                UpdateTitleDisplay(playerLevel);
                UpdateNavigationArrows();
                ApplyAvatarTransform(setNumber);
                TryUpdateCirceEmoteMode();
            };

            if (animate)
            {
                var target = _useAnimatedAvatar ? (Control?)ImgAvatarAnimated : ImgAvatar;
                AnimateOpacity(target, 1.0, 0.0, 200, () =>
                {
                    if (gen != _avatarSwitchGen) return;
                    switchAction();
                    AnimateOpacity(AvatarBorder, 0.0, 1.0, 200, null);
                });
            }
            else
            {
                switchAction();
            }
        }

        private static CompanionId? GetCompanionForAvatarSet(int setNumber)
        {
            return setNumber switch
            {
                3 => CompanionId.OGBambiSprite,
                4 => CompanionId.CultBunny,
                5 => CompanionId.BrainParasite,
                6 => CompanionId.BambiTrainer,
                7 => CompanionId.BimboCow,
                _ => null
            };
        }

        public static int GetAvatarSetForCompanion(CompanionId companionId)
        {
            return companionId switch
            {
                CompanionId.OGBambiSprite => 3,
                CompanionId.CultBunny => 4,
                CompanionId.BrainParasite => 5,
                CompanionId.BambiTrainer => 6,
                CompanionId.BimboCow => 7,
                _ => 1
            };
        }

        private void UpdateTitleDisplay(int level)
        {
            if (_portraitMode && _portraitSet != null && _portraitSet.SkinCount > 0)
            {
                int si = _portraitSet.ClampSkin(_skinIndex);
                var skin = _portraitSet.Skins[si];
                var skinTitle = string.IsNullOrWhiteSpace(skin.Title) ? skin.Id : skin.Title;
                skinTitle = _modService?.MakeModAware(skinTitle) ?? skinTitle;
                if (TxtAvatarTitle != null) TxtAvatarTitle.Text = (skinTitle ?? "").ToUpperInvariant();
                if (TxtAvatarLevel != null) TxtAvatarLevel.IsVisible = false;
                return;
            }

            var companionId = GetCompanionForAvatarSet(_currentAvatarSet);
            if (companionId.HasValue)
            {
                var def = CompanionDefinition.GetById(companionId.Value);
                var progress = new CompanionProgress { Level = 1 };
                bool isMax = progress.IsMaxLevel;
                var displayName = _modService?.MakeModAware(def.GetDisplayName(false)) ?? def.GetDisplayName(false);
                if (TxtAvatarTitle != null) TxtAvatarTitle.Text = displayName.ToUpperInvariant();
                if (TxtAvatarLevel != null)
                {
                    TxtAvatarLevel.IsVisible = true;
                    TxtAvatarLevel.Text = isMax
                        ? Loc.Get("avatar_level_max")
                        : Loc.GetF("avatar_level_format", progress.Level);
                }
            }
            else
            {
                int idx = Math.Clamp(_currentAvatarSet - 1, 0, AvatarTitleKeys.Length - 1);
                var title = _modService?.MakeModAware(Loc.Get(AvatarTitleKeys[idx])) ?? Loc.Get(AvatarTitleKeys[idx]);
                if (TxtAvatarTitle != null) TxtAvatarTitle.Text = title;
                if (TxtAvatarLevel != null)
                {
                    TxtAvatarLevel.IsVisible = _currentAvatarSet > 2;
                    if (_currentAvatarSet > 2) TxtAvatarLevel.Text = Loc.GetF("avatar_level_format", level);
                }
            }
        }

        public void RefreshCompanionDisplay()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshCompanionDisplay);
                return;
            }
            UpdateTitleDisplay(_settings?.Current?.PlayerLevel ?? 1);
        }

        private void UpdateNavigationArrows()
        {
            var sets = EffectiveAvatarSets();
            bool hasMultiple = sets.Length > 1;
            int idx = Array.IndexOf(sets, _currentAvatarSet);
            if (BtnPrevAvatar != null) BtnPrevAvatar.IsVisible = hasMultiple && idx > 0;
            if (BtnNextAvatar != null) BtnNextAvatar.IsVisible = hasMultiple && idx < sets.Length - 1;
        }

        /// <summary>
        /// The avatar sets the selector should cycle through. In portrait mode these are the manifest skins.
        /// </summary>
        public int[] EffectiveAvatarSets()
        {
            if (IsSingleEmoteAvatarMod(out int onlySet)) return new[] { onlySet };

            if (_portraitMode && _portraitSet != null && _portraitSet.SkinCount > 0)
            {
                var arr = new int[_portraitSet.SkinCount];
                for (int i = 0; i < arr.Length; i++) arr[i] = i + 1;
                return arr;
            }
            return GetUnlockedAvatarSets(_settings?.Current?.PlayerLevel ?? 1);
        }

        private void ApplyAvatarTransform(int setNumber)
        {
            if (AvatarBorder == null) return;

            if (_portraitMode)
            {
                AvatarBorder.RenderTransform = new TranslateTransform(PortraitShiftX, -PortraitRaisePx);
                AvatarBorder.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                return;
            }

            // Mod authors can supply a global avatar scale in the tube layout (e.g. Circe's 0.864).
            // Compose it with the per-set nudges used in the WPF app so the avatar sits in the glass.
            double layoutScale = EffAvatarScale();
            double setScale;
            double setOffsetX;

            if (setNumber > 1)
            {
                setScale = 1.12;
                setOffsetX = 10;
            }
            else if (_modService?.ActiveMod?.Id == BuiltInMods.LockedId)
            {
                // Locked's set 1 ("The Lure") art reads smaller than the other stages,
                // so give it a slight boost like the WPF app does.
                setScale = 1.06;
                setOffsetX = 0;
            }
            else
            {
                setScale = 1.0;
                setOffsetX = 0;
            }

            double finalScale = setScale * layoutScale;
            if (Math.Abs(finalScale - 1.0) > 0.001 || Math.Abs(setOffsetX) > 0.001)
            {
                var group = new TransformGroup();
                group.Children.Add(new ScaleTransform(finalScale, finalScale));
                if (Math.Abs(setOffsetX) > 0.001)
                    group.Children.Add(new TranslateTransform(setOffsetX * layoutScale, 0));
                AvatarBorder.RenderTransform = group;
                AvatarBorder.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            }
            else
            {
                AvatarBorder.RenderTransform = null;
            }
        }

        public void SwitchToCompanionAvatar(CompanionId companionId)
        {
            int target = GetAvatarSetForCompanion(companionId);
            int level = _settings?.Current?.PlayerLevel ?? 1;
            if (IsAvatarSetUnlocked(target, level))
                SwitchToAvatarSet(target, animate: true);
        }

        private void OnModChanged(object? sender, global::ConditioningControlPanel.Models.ModPackage e)
        {
            try
            {
                if (IsSingleEmoteAvatarMod(out int emoteOnlySet))
                {
                    _currentAvatarSet = _selectedAvatarSet = emoteOnlySet;
                }
                else
                {
                    var supportedSets = GetUnlockedAvatarSets(_settings?.Current?.PlayerLevel ?? 1);
                    if (supportedSets.Length > 0 && !supportedSets.Contains(_currentAvatarSet))
                    {
                        var oldSet = _currentAvatarSet;
                        _currentAvatarSet = supportedSets[0];
                        _selectedAvatarSet = _currentAvatarSet;
                        if (_settings?.Current != null)
                            _settings.Current.SelectedAvatarSet = _selectedAvatarSet;
                        _logger?.LogInformation("Avatar set {OldSet} not supported by new mod, switched to {NewSet}", oldSet, _currentAvatarSet);
                    }
                }

                _useAnimatedAvatar = HasAnimatedAvatar(_currentAvatarSet);

                // 2026-07-11 rebuild (board REBUILD SPEC invariant 6): theme/reskin restyles
                // IN PLACE. Clear every secondary avatar layer BEFORE the mode dispatch so a
                // residual layer from the previous mode (portrait crossfade B-layer, mist,
                // animated B-layer) can never keep rendering under/over the new avatar — the
                // owner-visible "old avatar + new smaller one over it" on theme switch.
                // Live bubbles die FIRST (obs #6 retest-2 fix 2: dispose-before-recreate) so
                // a reskin never leaves orphaned bubble windows/timers from the old skin.
                AvatarRandomBubble.DestroyAll();
                CancelCrossfade();
                if (ImgAvatarB != null) { ImgAvatarB.IsVisible = false; ImgAvatarB.Opacity = 0; }
                if (ImgAvatarAnimatedB != null) { ImgAvatarAnimatedB.IsVisible = false; ImgAvatarAnimatedB.Opacity = 0; }
                if (MistOverlay != null) MistOverlay.IsVisible = false;

                if (UsePortraitSystem())
                {
                    TryEnterPortraitMode();
                }
                else if (_useAnimatedAvatar)
                {
                    LoadAnimatedAvatar(_currentAvatarSet);
                }
                else
                {
                    LeavePortraitMode();

                    if (ImgAvatarAnimated != null)
                    {
                        ImgAvatarAnimated.IsVisible = false;
                        ImgAvatarAnimated.Source = null;
                    }
                    if (ImgAvatar != null) ImgAvatar.IsVisible = true;

                    _avatarPoses = LoadAvatarPoses(_currentAvatarSet);
                    _currentPoseIndex = 0;
                    if (ImgAvatar != null) ImgAvatar.Source = _avatarPoses.Length > 0 ? _avatarPoses[0] : null;

                    if (_avatarPoses.Length > 1 && !_portraitMode)
                        _poseTimer.Start();
                }

                ApplyAvatarTransform(_currentAvatarSet);
                UpdateNavigationArrows();
                TryUpdateCirceEmoteMode();
                UpdateTitleDisplay(_settings?.Current?.PlayerLevel ?? 1);

                // Refresh tube frame and layout offsets for the new mod's art.
                SetTubeStyle(!_isAttached);
                ApplyTubeLayoutOffsets();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to refresh resources after mod change");
            }
        }

        private void ApplyTubeLayoutOffsets()
        {
            if (AvatarBorder == null) return;

            // Avalonia does not have a WPF-style LayoutTransform property, so the mod's
            // TubeLayout avatar scale is not applied here. The offset math below matches
            // the WPF app exactly and keeps the avatar/glass alignment parity.

            // When the mod only overrides the attached tube image, force the attached
            // layout in detached state too — otherwise the avatar lands outside the
            // chamber the mod author drew.
            var useAttachedLayout = _isAttached || ModOverridesAttachedTubeOnly();

            if (useAttachedLayout)
            {
                var dx = EffAvatarOffsetX();
                var dy = EffAvatarOffsetY();
                AvatarBorder.Margin = new Thickness(5, 100, 126 - dx, 210 + dy);
                if (TitleBox != null) TitleBox.Margin = new Thickness(0, 0, 121 - dx, 180);
                if (InputPanel != null) InputPanel.Margin = new Thickness(0, 0, 126 - dx, 520);
                if (SpeechBubble != null) SpeechBubble.Margin = new Thickness(0, 0, 125 - dx, 550);
            }
            else
            {
                var dx = EffAvatarDetachedOffsetX();
                var dy = EffAvatarDetachedOffsetY();
                // Detached nudge: 20px higher (bottom margin +20, bottom-aligned) and net 5px left
                // (right margin +10 — element is HorizontalAlignment=Center, so offset is (L-R)/2).
                AvatarBorder.Margin = new Thickness(5, 100, 436 - dx, 228 + dy);
                if (TitleBox != null) TitleBox.Margin = new Thickness(0, 0, 416 - dx, 193);
                if (InputPanel != null) InputPanel.Margin = new Thickness(0, 0, 426 - dx, 520);
                if (SpeechBubble != null) SpeechBubble.Margin = new Thickness(0, 0, 425 - dx, 550);
            }
        }

        private void SetTubeStyle(bool useAlternative)
        {
            try
            {
                // If the active mod only ships a tube.png override, use it in both states
                // so the chamber stays consistent with the mod's art.
                if (useAlternative && ModOverridesAttachedTubeOnly())
                    useAlternative = false;

                var tubeName = useAlternative ? "tube2.png" : "tube.png";
                if (ImgTubeFrame != null && _resourceResolver != null)
                {
                    ImgTubeFrame.Source = _resourceResolver.ResolveBitmap(tubeName);
                    // Refresh the detached per-pixel hit-test cache alongside the art
                    // (obs #6 retest-2 fix 3b) - SetTubeStyle is the single Source writer.
                    CacheTubeArtPixels(tubeName);
                }
                _logger?.LogInformation("Tube style changed to: {Style}", tubeName);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to change tube style");
            }
        }

        private bool ModOverridesAttachedTubeOnly()
        {
            return _resourceResolver?.HasModOverride("tube.png") == true
                && _resourceResolver?.HasModOverride("tube2.png") != true;
        }

        internal double EffAvatarScale() => _modService?.GetAvatarScale() ?? 1.0;
        internal int EffAvatarOffsetX() => _modService?.GetAvatarOffsetX() ?? 0;
        internal int EffAvatarOffsetY() => _modService?.GetAvatarOffsetY() ?? 0;
        internal int EffAvatarDetachedOffsetX() => _modService?.GetAvatarDetachedOffsetX() ?? 0;
        internal int EffAvatarDetachedOffsetY() => _modService?.GetAvatarDetachedOffsetY() ?? 0;

        // ════════════════════════════════════════════════════════════════════════════════
        //  EMOTIVE PORTRAIT AVATAR
        // ════════════════════════════════════════════════════════════════════════════════

        // OWNER RULING 2026-07-11: portrait mode is DISABLED in the Avalonia AvatarTube. When
        // active it assigned AvatarBorder.RenderTransform = new TranslateTransform(PortraitShiftX=10,
        // -PortraitRaisePx=-30) (a RenderTransform, invisible to geometry checks, the "shifts up" half
        // of the owner repro bug), used a close-up portrait skin that fills the tube (the "enlarged"
        // half), and stopped pose cycling (_poseTimer only starts when !_portraitMode, the "freezes"
        // half). Owner wants the avatar to ALWAYS be the full-body, centered, pose-cycling legacy
        // avatar, so portrait mode is unreachable here and every portrait branch below is dead code.
        // The WPF head KEEPS portrait mode ON (UsePortraitSystem AvatarTubeWindow.Avatar.cs:899-902
        // returns Services.AvatarPortraitLoader.HasManifestForActiveMod()) - this is an intentional
        // cross-head divergence per the owner ruling, NOT a port regression.
        private bool UsePortraitSystem() => false;

        private void TryEnterPortraitMode()
        {
            try
            {
                var set = _portraitService?.Load();
                if (set == null) { LeavePortraitMode(); return; }

                _portraitSet = set;
                _portraitMode = true;
                _useAnimatedAvatar = false;

                if (_poseSeqTimer == null)
                {
                    _poseSeqTimer = new DispatcherTimer();
                    _poseSeqTimer.Tick += PoseSeqTimer_Tick;
                }
                if (_emotionReturnTimer == null)
                {
                    _emotionReturnTimer = new DispatcherTimer();
                    _emotionReturnTimer.Tick += EmotionReturnTimer_Tick;
                }

                _activeImg = ImgAvatar;
                _idleImg = ImgAvatarB;
                _skinIndex = _portraitSet.ClampSkin(_selectedAvatarSet - 1);
                _currentEmotion = _portraitSet.IdleEmotion;
                _emotionPoseIndex = 0;

                if (ImgAvatarAnimated != null)
                {
                    ImgAvatarAnimated.IsVisible = false;
                    ImgAvatarAnimated.Source = null;
                }
                if (ImgAvatar != null) ImgAvatar.IsVisible = true;
                if (ImgAvatarB != null) ImgAvatarB.IsVisible = true;

                CancelCrossfade();
                if (ImgAvatar != null) ImgAvatar.Opacity = 1.0;
                if (ImgAvatarB != null) ImgAvatarB.Opacity = 0.0;
                if (MistOverlay != null) MistOverlay.IsVisible = true;

                ApplyPortraitChrome();

                var bucket = _portraitSet.GetBucketPaths(_skinIndex, _currentEmotion);
                if (bucket.Count > 0 && _activeImg != null)
                    _activeImg.Source = LoadPortraitBitmap(bucket[0]);

                _poseTimer.Stop();

                UpdateTitleDisplay(_settings?.Current?.PlayerLevel ?? 1);
                UpdateNavigationArrows();
                StartPortraitAmbientAnimation();

                _logger?.LogInformation("Avatar portrait mode ON (skin {Skin}/{Count}, emotion '{Emo}')",
                    _skinIndex, _portraitSet.SkinCount, _currentEmotion);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "TryEnterPortraitMode failed; falling back to legacy avatar");
                LeavePortraitMode();
            }
        }

        private void ApplyPortraitChrome()
        {
            if (ImgAvatar != null)
            {
                ImgAvatar.MaxHeight = LegacyAvatarMaxHeight * PortraitSizeScale;
                ImgAvatar.MaxWidth = LegacyAvatarMaxWidth * PortraitSizeScale;
            }
            if (ImgAvatarB != null)
            {
                ImgAvatarB.MaxHeight = LegacyAvatarMaxHeight * PortraitSizeScale;
                ImgAvatarB.MaxWidth = LegacyAvatarMaxWidth * PortraitSizeScale;
            }
            if (AvatarBorder != null)
            {
                AvatarBorder.RenderTransform = new TranslateTransform(PortraitShiftX, -PortraitRaisePx);
                AvatarBorder.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            }
        }

        private void LeavePortraitMode()
        {
            _portraitMode = false;
            _portraitSet = null;
            _poseSeqTimer?.Stop();
            _emotionReturnTimer?.Stop();
            StopPortraitAmbientAnimation();
            CancelCrossfade();
            try
            {
                if (ImgAvatarB != null)
                {
                    ImgAvatarB.IsVisible = false;
                    ImgAvatarB.Opacity = 0.0;
                    ImgAvatarB.Source = null;
                }
                if (MistOverlay != null) MistOverlay.IsVisible = false;
                if (ImgAvatar != null) ImgAvatar.Opacity = 1.0;

                if (ImgAvatar != null)
                {
                    ImgAvatar.MaxHeight = LegacyAvatarMaxHeight;
                    ImgAvatar.MaxWidth = LegacyAvatarMaxWidth;
                }
                if (ImgAvatarB != null)
                {
                    ImgAvatarB.MaxHeight = LegacyAvatarMaxHeight;
                    ImgAvatarB.MaxWidth = LegacyAvatarMaxWidth;
                }

                ResetLayerTransforms();
            }
            catch { /* closing/teardown — non-fatal */ }
            _activeImg = null;
            _idleImg = null;
        }

        private void ReloadPortraitSkin()
        {
            if (_portraitSet == null) return;
            CancelCrossfade();
            _activeImg = ImgAvatar;
            _idleImg = ImgAvatarB;
            if (ImgAvatar != null) { ImgAvatar.IsVisible = true; ImgAvatar.Opacity = 1.0; }
            if (ImgAvatarB != null) { ImgAvatarB.IsVisible = true; ImgAvatarB.Opacity = 0.0; }
            if (MistOverlay != null) MistOverlay.IsVisible = true;
            ApplyPortraitChrome();

            var bucket = _portraitSet.GetBucketPaths(_skinIndex, _currentEmotion);
            if (bucket.Count > 0)
            {
                if (_emotionPoseIndex >= bucket.Count) _emotionPoseIndex = 0;
                if (_activeImg != null)
                    _activeImg.Source = LoadPortraitBitmap(bucket[_emotionPoseIndex]);
            }
        }

        private void CancelCrossfade()
        {
            _crossfadeTimer?.Stop();
            _crossfadeInFlight = false;
            if (_activeImg != null) _activeImg.Opacity = 1.0;
            if (_idleImg != null) _idleImg.Opacity = 0.0;
        }

        private void CrossfadeTo(Bitmap? next, bool preempt = false)
        {
            if (next == null || _activeImg == null || _idleImg == null) return;
            if (ReferenceEquals(_activeImg.Source, next)) return;

            if (_crossfadeInFlight)
            {
                if (!preempt) return;
                _crossfadeTimer?.Stop();
                var prevIn = _idleImg;
                var prevOut = _activeImg;
                prevIn.Opacity = 1.0;
                prevOut.Opacity = 0.0;
                _activeImg = prevIn;
                _idleImg = prevOut;
                _crossfadeInFlight = false;
            }

            var inImg = _idleImg;
            var outImg = _activeImg;
            inImg.Source = next;
            inImg.Opacity = 0.0;
            outImg.Opacity = 1.0;
            _crossfadeInFlight = true;

            int frames = _portraitSet?.Director.CrossfadeFrames ?? 4;
            double totalMs = Math.Max(60, frames * 38);
            const int stepMs = 16;
            int steps = Math.Max(1, (int)Math.Round(totalMs / stepMs));
            int current = 0;

            _crossfadeTimer?.Stop();
            _crossfadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(stepMs) };
            _crossfadeTimer.Tick += (_, _) =>
            {
                current++;
                double t = Math.Min(1.0, current / (double)steps);
                outImg.Opacity = 1.0 - t;
                inImg.Opacity = t;
                if (current >= steps)
                {
                    _crossfadeTimer.Stop();
                    inImg.Opacity = 1.0;
                    outImg.Opacity = 0.0;
                    _activeImg = inImg;
                    _idleImg = outImg;
                    _crossfadeInFlight = false;
                }
            };
            _crossfadeTimer.Start();
        }

        private Bitmap? LoadPortraitBitmap(string? path)
        {
            try
            {
                return string.IsNullOrEmpty(path) || !File.Exists(path) ? null : new Bitmap(path);
            }
            catch { return null; }
        }

        private void PlayEmotionForLine(string? emotionLineId, string? audioPath, string? text, string? mood = null)
        {
            if (_circeEmoteMode) { CircePlayEmote(emotionLineId, audioPath, text, mood); return; }
            if (!_portraitMode || _portraitSet == null) return;

            var emotion = _portraitSet.EmotionForLine(emotionLineId);
            if (string.IsNullOrEmpty(emotion))
                emotion = !string.IsNullOrWhiteSpace(mood)
                    ? _portraitSet.EmotionForMood(mood)
                    : PickAffirmationEmotion();

            double durationSec = audioPath != null ? AudioDurationSec(audioPath) : EstimateDurationSec(text);
            SetEmotionSequence(emotion!, PoseCountForDuration(durationSec), durationSec);
        }

        private string PickAffirmationEmotion() => AffirmationEmotions[_random.Next(AffirmationEmotions.Length)];

        private int PoseCountForDuration(double sec)
        {
            if (sec <= 0) return MinSpeakPoses;
            int n = (int)Math.Round(sec) - 1;
            return Math.Clamp(n, MinSpeakPoses, MaxSpeakPoses);
        }

        private double AudioDurationSec(string? path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            if (_audioDurCache.TryGetValue(path, out var cached)) return cached;
            double sec = 0;
            try
            {
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    // Best-effort MP3 heuristic: ~16 KB/s for typical CBR voice clips.
                    sec = Math.Clamp(info.Length / 16000.0, 1.0, 30.0);
                }
            }
            catch { sec = 0; }
            _audioDurCache[path] = sec;
            return sec;
        }

        private double EstimateDurationSec(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 2.5;
            int words = text!.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return Math.Clamp(0.45 * words + 0.8, 2.0, 7.0);
        }

        private void SetEmotionSequence(string emotion, int poseCount, double durationSec)
        {
            if (_portraitSet == null) return;
            var bucket = _portraitSet.GetBucketPaths(_skinIndex, emotion);
            if (bucket.Count == 0) return;

            _currentEmotion = emotion;
            poseCount = Math.Clamp(poseCount, MinSpeakPoses, MaxSpeakPoses);
            _seqOrder = BuildPoseOrder(bucket.Count, poseCount);
            _seqStep = 0;

            bool shortLine = durationSec > 0 && durationSec < ShortLineSec;
            _seqStepMs = shortLine ? (int)(PoseStepMs * ShortSpeedFactor) : PoseStepMs;
            _seqLastMs = shortLine ? (int)(LastPoseLingerMs * ShortSpeedFactor) : LastPoseLingerMs;

            _poseSeqTimer ??= new DispatcherTimer();
            _poseSeqTimer.Stop();
            _emotionReturnTimer?.Stop();
            _poseTimer.Stop();

            int first = _seqOrder.Length > 0 ? _seqOrder[0] : 0;
            _emotionPoseIndex = first;
            CrossfadeTo(LoadPortraitBitmap(bucket[first]), preempt: true);
            LogContentGeometry("emotion-seq");

            bool firstIsLast = _seqOrder.Length <= 1;
            _poseSeqTimer.Interval = TimeSpan.FromMilliseconds(firstIsLast ? _seqLastMs : _seqStepMs);
            _poseSeqTimer.Start();
        }

        private int[] BuildPoseOrder(int bucketLen, int n)
        {
            if (bucketLen <= 0) return Array.Empty<int>();
            var order = new List<int>(n);
            var pool = new List<int>();
            int last = -1;
            while (order.Count < n)
            {
                if (pool.Count == 0)
                {
                    for (int i = 0; i < bucketLen; i++) pool.Add(i);
                    for (int i = pool.Count - 1; i > 0; i--) { int j = _random.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
                    if (bucketLen > 1 && pool[0] == last) { (pool[0], pool[1]) = (pool[1], pool[0]); }
                }
                last = pool[0];
                order.Add(pool[0]);
                pool.RemoveAt(0);
            }
            return order.ToArray();
        }

        private void PoseSeqTimer_Tick(object? sender, EventArgs e)
        {
            _poseSeqTimer?.Stop();
            if (!_portraitMode || _portraitSet == null) return;

            _seqStep++;
            if (_seqStep >= _seqOrder.Length)
            {
                ReturnToIdleEmotion();
                return;
            }

            var bucket = _portraitSet.GetBucketPaths(_skinIndex, _currentEmotion);
            if (bucket.Count == 0) { ReturnToIdleEmotion(); return; }

            int idx = _seqOrder[_seqStep] % bucket.Count;
            _emotionPoseIndex = idx;
            CrossfadeTo(LoadPortraitBitmap(bucket[idx]), preempt: true);

            bool isLast = _seqStep == _seqOrder.Length - 1;
            _poseSeqTimer!.Interval = TimeSpan.FromMilliseconds(isLast ? _seqLastMs : _seqStepMs);
            _poseSeqTimer.Start();
        }

        private void EmotionReturnTimer_Tick(object? sender, EventArgs e)
        {
            _emotionReturnTimer?.Stop();
            ReturnToIdleEmotion();
        }

        private void ReturnToIdleEmotion()
        {
            if (!_portraitMode || _portraitSet == null) return;
            _poseSeqTimer?.Stop();
            _currentEmotion = _portraitSet.IdleEmotion;
            var bucket = _portraitSet.GetBucketPaths(_skinIndex, _currentEmotion);
            if (bucket.Count > 0)
            {
                int idx = _random.Next(bucket.Count);
                _emotionPoseIndex = idx;
                CrossfadeTo(LoadPortraitBitmap(bucket[idx]), preempt: true);
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════
        //  CONTINUOUS AMBIENT PORTRAIT ANIMATION
        // ════════════════════════════════════════════════════════════════════════════════

        private void StartPortraitAmbientAnimation()
        {
            if (_portraitAmbientTimer != null)
            {
                _portraitAmbientTimer.Start();
                return;
            }
            _portraitAmbientTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _portraitAmbientTimer.Tick += PortraitAmbientTimer_Tick;
            _portraitAmbientTimer.Start();
        }

        private void StopPortraitAmbientAnimation()
        {
            _portraitAmbientTimer?.Stop();
        }

        private void PortraitAmbientTimer_Tick(object? sender, EventArgs e)
        {
            const double dt = 0.033;
            _breathPhase += dt * 1.0;
            _wobblePhase += dt * 0.7;
            _mistPhase += dt * 0.5;

            bool speaking = _isGiggling;
            if (speaking)
            {
                _speakPhase += dt * 15.0;
                _speakEnvPhase += dt * 1.2;
            }
            else
            {
                _speakPhase = 0;
                _speakEnvPhase = 0;
            }

            double breathScale = 1.0 + Math.Sin(_breathPhase) * BreathAmplitude;
            double wobbleAngle = Math.Sin(_wobblePhase) * WobbleAmplitudeDeg;
            double speakEnv = speaking ? Math.Max(0, Math.Sin(_speakEnvPhase)) : 0;
            double speakWobble = speakEnv * SpeakWobbleDeg * Math.Sin(_speakPhase);
            double speakShake = speakEnv * SpeakShakePx * Math.Sin(_speakPhase * 1.3);

            ApplyLayerTransform(ImgAvatar, breathScale, wobbleAngle + speakWobble, speakShake);
            ApplyLayerTransform(ImgAvatarB, breathScale, wobbleAngle + speakWobble, speakShake);

            if (MistOverlay != null)
            {
                double mistScale = 1.0 + Math.Sin(_mistPhase) * 0.03;
                if (MistOverlay.RenderTransform is ScaleTransform st)
                {
                    st.ScaleX = mistScale;
                    st.ScaleY = mistScale;
                }
                else
                {
                    MistOverlay.RenderTransform = new ScaleTransform(mistScale, mistScale);
                }
            }
        }

        private static void ApplyLayerTransform(Image? img, double scale, double angle, double shakeX)
        {
            if (img?.RenderTransform is not TransformGroup group || group.Children.Count < 3) return;
            if (group.Children[0] is ScaleTransform st)
            {
                st.ScaleX = scale;
                st.ScaleY = scale;
            }
            if (group.Children[1] is RotateTransform rt)
                rt.Angle = angle;
            if (group.Children[2] is TranslateTransform tt)
                tt.X = shakeX;
        }

        private void ResetLayerTransforms()
        {
            ResetLayerTransform(ImgAvatar);
            ResetLayerTransform(ImgAvatarB);
            if (MistOverlay?.RenderTransform is ScaleTransform mist)
            {
                mist.ScaleX = 1.0;
                mist.ScaleY = 1.0;
            }
        }

        private static void ResetLayerTransform(Image? img)
        {
            if (img?.RenderTransform is not TransformGroup group || group.Children.Count < 3) return;
            if (group.Children[0] is ScaleTransform st)
            {
                st.ScaleX = 1.0;
                st.ScaleY = 1.0;
            }
            if (group.Children[1] is RotateTransform rt)
                rt.Angle = 0;
            if (group.Children[2] is TranslateTransform tt)
            {
                tt.X = 0;
                tt.Y = 0;
            }
        }

        private static void AnimateOpacity(Control? element, double from, double to, int ms, Action? completed)
        {
            if (element == null)
            {
                completed?.Invoke();
                return;
            }
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            long start = Environment.TickCount64;
            timer.Tick += (_, _) =>
            {
                long elapsed = Environment.TickCount64 - start;
                if (elapsed >= ms)
                {
                    timer.Stop();
                    element.Opacity = to;
                    completed?.Invoke();
                    return;
                }
                element.Opacity = from + (to - from) * (elapsed / (double)ms);
            };
            timer.Start();
        }
    }
}

#pragma warning restore CS0169
#pragma warning restore CS0414
#pragma warning restore CS0649
