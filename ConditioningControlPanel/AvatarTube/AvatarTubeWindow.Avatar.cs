using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Moderation;
using XamlAnimatedGif;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    public partial class AvatarTubeWindow : Window
    {
        private readonly DispatcherTimer _poseTimer;
        private BitmapImage[] _avatarPoses;
        private int _currentPoseIndex = 0;
        private int _currentAvatarSet = 1; // Track which avatar set is loaded
        private int _selectedAvatarSet = 1; // User's manually selected avatar (can be lower than max unlocked)
        private int _maxUnlockedSet = 1; // Highest avatar set unlocked based on level
        private bool _useAnimatedAvatar = false; // Whether to use animated GIF

        // ── Emotive portrait avatar (mod-agnostic) ───────────────────────────────────────
        // Active only when the active mod ships an avatar_manifest.json (Sissy first). When on, the
        // skins ARE the avatar-set selector's choices — each the same 20-emotion portrait collection
        // in a different outfit; barks drive a per-line emotion (pose A → linger → pose B), with
        // continuous breathing/wobble/pink-mist. Mods without a manifest keep the legacy 4-pose path.
        private Services.AvatarPortraitSet? _portraitSet;
        private bool _portraitMode = false;
        private int _skinIndex = 0;
        private string _currentEmotion = "neutral";
        private int _emotionPoseIndex = 0;
        private System.Windows.Controls.Image? _activeImg;   // portrait layer currently shown
        private System.Windows.Controls.Image? _idleImg;     // the hidden layer we crossfade INTO
        private bool _crossfadeInFlight = false;
        private DispatcherTimer? _emotionReturnTimer;        // (legacy) created but no longer scheduled
        private DispatcherTimer? _poseSeqTimer;              // drives the speak pose sequence (1s steps, last 2s)
        private int[] _seqOrder = System.Array.Empty<int>(); // bucket-index order for the current spoken line
        private int _seqStep = 0;                            // index into _seqOrder of the pose now showing
        private int _seqStepMs = 1000;                       // per-sequence step/last timing (scaled by line length)
        private int _seqLastMs = 2000;
        private readonly Dictionary<string, double> _audioDurCache = new(); // mp3 length per path (poses ∝ length)
        private double _breathPhase = 0, _wobblePhase = 0, _mistPhase = 0;
        private const double BreathAmplitude = 0.01;     // ±1% scale (breathing)
        private const double WobbleAmplitudeDeg = 0.4;   // ±0.4° rotation (wobble)
        // Speak-time pose sequence: NO idle rotation. On speech we cycle 2–5 poses of the line's emotion,
        // each PoseStepMs, with the LAST held LastPoseLingerMs, then settle on a still idle pose. Pose count
        // scales with line length (longer line → more poses).
        private const int PoseStepMs = 1000;             // each non-final pose shows ~1s
        private const int LastPoseLingerMs = 2000;       // the final pose lingers ~2s before idle
        private const int MinSpeakPoses = 2;
        private const int MaxSpeakPoses = 5;
        // Short lines (≈4–5 words) finish before the 1s/2s cadence does, so their poses flip ~2x faster
        // and keep pace with the brief audio instead of stalling on the first/second pose.
        private const double ShortLineSec = 3.5;
        private const double ShortSpeedFactor = 0.5;
        // Extra "she's talking" motion + mist, applied ONLY while a spoken clip is playing. Kept subtle and
        // INTERMITTENT (a slow envelope gates the fast carrier) so it's a barely-there occasional shimmer.
        private double _speakPhase = 0;       // fast vibration carrier
        private double _speakEnvPhase = 0;    // slow envelope → bursts separated by calm
        private const double SpeakWobbleDeg = 0.175; // extra rotation at the peak of a burst (tiny)
        private const double SpeakShakePx = 0.25;    // horizontal jitter at the peak of a burst (tiny)
        private const double PortraitSizeScale = 0.88;   // portrait size vs legacy poses (0.70 → +15% → +10% per feedback)
        private const double PortraitRaisePx = 30;       // shift the portrait avatar UP by this many px (100→50→30 per feedback)
        private const double PortraitShiftX = 10;        // shift the portrait avatar RIGHT by this many px
        private const double LegacyAvatarMaxHeight = 306; // XAML defaults for ImgAvatar/ImgAvatarB
        private const double LegacyAvatarMaxWidth = 198;
        private System.Windows.Media.Effects.Effect? _savedEffectA; // original pink DropShadow (restored for legacy)
        private System.Windows.Media.Effects.Effect? _savedEffectB;
        private bool _avatarEffectsSaved = false;

        // Avatar set titles (localization keys)
        private static readonly string[] AvatarTitleKeys = new[]
        {
            "avatar_title_basic_bimbo",          // Set 1: Level 1-19
            "avatar_title_dumb_airhead",         // Set 2: Level 20-34
            "avatar_title_synthetic_blowdoll",   // Set 3: Level 35-49
            "avatar_title_perfect_fuckpuppet",   // Set 4: Level 50-124
            "avatar_title_brainwashed_slavedoll", // Set 5: Level 125-149
            "avatar_title_platinum_puppet",      // Set 6: Level 150+
            "avatar_title_bambi_cow"             // Set 7: Level 75+ (companion-only)
        };

        /// <summary>
        /// Feature level gating has been removed — every avatar set is available from level 1.
        /// The "max set" now just returns the largest base set (7) so navigation lands in the
        /// same place a level-200 user would.
        /// </summary>
        /// <param name="level">Player's current level (unused - kept for API compatibility)</param>
        /// <returns>Avatar set number</returns>
        public static int GetAvatarSetForLevel(int level)
        {
            return 7;
        }

        /// <summary>
        /// Feature level gating has been removed — every avatar set is always unlocked.
        /// </summary>
        public static bool IsAvatarSetUnlocked(int setNumber, int level)
        {
            return true;
        }

        /// <summary>
        /// Gets all unlocked avatar sets for the given level, in unlock-level order.
        /// Order: 1 (Lv1), 2 (Lv20), 3 (Lv35), 4 (Lv50), 7 (Lv75), 5 (Lv125), 6 (Lv150)
        /// </summary>
        public static int[] GetUnlockedAvatarSets(int level)
        {
            // Base sets in unlock-level order (not numerical order)
            int[] setsInOrder = { 1, 2, 3, 4, 7, 5, 6 };
            var unlocked = new System.Collections.Generic.List<int>();
            foreach (int set in setsInOrder)
            {
                if (IsAvatarSetUnlocked(set, level) && (App.Mods?.IsAvatarSetSupported(set) ?? true))
                    unlocked.Add(set);
            }

            // Append custom avatar sets (8+) sorted by unlock level
            var customSets = App.Mods?.GetCustomAvatarSets();
            if (customSets != null)
            {
                foreach (var cs in customSets.OrderBy(c => c.UnlockLevel))
                {
                    if (IsAvatarSetUnlocked(cs.SetNumber, level) && (App.Mods?.IsAvatarSetSupported(cs.SetNumber) ?? true))
                        unlocked.Add(cs.SetNumber);
                }
            }

            return unlocked.ToArray();
        }

        /// <summary>
        /// Updates the avatar to match the current player level
        /// Call this when the player levels up
        /// </summary>
        public void UpdateAvatarForLevel(int newLevel)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => UpdateAvatarForLevel(newLevel))); return; }
            int newMaxSet = GetAvatarSetForLevel(newLevel);

            // Update max unlocked (user may have unlocked a new avatar)
            if (newMaxSet > _maxUnlockedSet)
            {
                App.Logger?.Information("New avatar unlocked! Set {NewSet} at level {Level}", newMaxSet, newLevel);
                _maxUnlockedSet = newMaxSet;

                // Auto-switch to newly unlocked avatar
                _selectedAvatarSet = newMaxSet;
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.SelectedAvatarSet = _selectedAvatarSet;
                    App.Settings.Save();
                }

                SwitchToAvatarSet(newMaxSet, animate: true);
            }

            // Update title display
            UpdateTitleDisplay(newLevel);
            UpdateNavigationArrows();
        }

        /// <summary>
        /// Check if an avatar set has animated GIF version available
        /// File naming: animated{set}_1.gif (e.g., animated1_1.gif for set 1)
        /// </summary>
        private bool HasAnimatedAvatar(int setNumber)
        {
            try
            {
                // Check mod override first, then embedded resource
                if (Services.ModResourceResolver.HasModOverride($"animated{setNumber}_1.gif"))
                    return true;
                var uri = new Uri($"pack://application:,,,/Resources/animated{setNumber}_1.gif", UriKind.Absolute);
                var info = Application.GetResourceStream(uri);
                return info != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Load animated GIF avatar using XamlAnimatedGif
        /// File naming: animated{set}_1.gif (e.g., animated1_1.gif for set 1)
        /// </summary>
        private void LoadAnimatedAvatar(int setNumber)
        {
            try
            {
                // An animated set always wins over the emotive-portrait system.
                LeavePortraitMode();

                // Naming pattern: animated1_1.gif, animated2_1.gif, etc.
                var gifUri = new Uri(Services.ModResourceResolver.ResolveUri($"animated{setNumber}_1.gif"), UriKind.Absolute);

                // Hide static avatar, show animated
                ImgAvatar.Visibility = Visibility.Collapsed;
                ImgAvatarAnimated.Visibility = Visibility.Visible;

                // Set the animated GIF source
                AnimationBehavior.SetSourceUri(ImgAvatarAnimated, gifUri);
                AnimationBehavior.SetAutoStart(ImgAvatarAnimated, true);
                AnimationBehavior.SetRepeatBehavior(ImgAvatarAnimated, RepeatBehavior.Forever);

                // Stop pose timer (not needed for animated)
                _poseTimer.Stop();

                App.Logger?.Information("Loaded animated avatar: animated{Set}_1.gif", setNumber);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to load animated avatar {Set}: {Error}", setNumber, ex.Message);
                // Fall back to static
                _useAnimatedAvatar = false;
                ImgAvatar.Visibility = Visibility.Visible;
                ImgAvatarAnimated.Visibility = Visibility.Collapsed;
                if (_avatarPoses.Length > 0)
                {
                    ImgAvatar.Source = _avatarPoses[0];
                }
            }
        }

        /// <summary>
        /// Refresh the avatar animation to fix stuck animations
        /// </summary>
        private void RefreshAvatarAnimation()
        {
            if (!_useAnimatedAvatar) return;

            try
            {
                // Clear and reload the animation
                AnimationBehavior.SetSourceUri(ImgAvatarAnimated, null);

                var gifUri = new Uri(Services.ModResourceResolver.ResolveUri($"animated{_currentAvatarSet}_1.gif"), UriKind.Absolute);
                AnimationBehavior.SetSourceUri(ImgAvatarAnimated, gifUri);
                AnimationBehavior.SetAutoStart(ImgAvatarAnimated, true);
                AnimationBehavior.SetRepeatBehavior(ImgAvatarAnimated, RepeatBehavior.Forever);

                App.Logger?.Debug("Refreshed avatar animation");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to refresh avatar animation: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Pause the animated GIF to reduce CPU usage when not visible
        /// </summary>
        private void PauseAvatarGif()
        {
            if (_circeEmoteMode) { CircePause(); return; }
            if (!_useAnimatedAvatar) return;
            try
            {
                var animator = AnimationBehavior.GetAnimator(ImgAvatarAnimated);
                animator?.Pause();
            }
            catch { }
        }

        /// <summary>
        /// Resume the animated GIF when becoming visible again
        /// </summary>
        private void ResumeAvatarGif()
        {
            if (_circeEmoteMode) { CirceResume(); return; }
            if (!_useAnimatedAvatar) return;
            try
            {
                var animator = AnimationBehavior.GetAnimator(ImgAvatarAnimated);
                animator?.Play();
            }
            catch { }
        }

        /// <summary>Monotonic token; a rapid burst of set-switches collapses to the latest so a stale
        /// fade-completion can't repaint an intermediate set (the "ghost" avatar bug).</summary>
        private int _avatarSwitchGen;

        /// <summary>
        /// Switch to a specific avatar set (with optional animation)
        /// </summary>
        private void SwitchToAvatarSet(int setNumber, bool animate = true)
        {
            int playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
            if (!IsAvatarSetUnlocked(setNumber, playerLevel)) return;

            int gen = ++_avatarSwitchGen;
            _currentAvatarSet = setNumber;
            _selectedAvatarSet = setNumber;
            _useAnimatedAvatar = HasAnimatedAvatar(setNumber);

            // Save selection
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SelectedAvatarSet = setNumber;
                App.Settings.Save();
            }

            Action switchAction = () =>
            {
                // Link avatar sets 4+ to companions (v5.3). Done here (NOT in the synchronous prefix)
                // so a rapid swipe past intermediate sets doesn't fire SwitchCompanion -> repaint for
                // each one — only the settled set switches the companion (fixes the ghost avatar).
                //   Set 4: Lv50 → Perfect Fuckpuppet · Set 5: Lv125 → Brainwashed Slavedoll · Set 6: Lv150 → Platinum Puppet
                // In portrait mode the "set" picks a SKIN (outfit), not a companion — skip the coupling.
                if (!UsePortraitSystem())
                {
                    var companionId = GetCompanionForAvatarSet(setNumber);
                    if (companionId.HasValue && App.Companion != null)
                    {
                        App.Companion.SwitchCompanion(companionId.Value);
                    }
                }

                if (UsePortraitSystem())
                {
                    // Portrait mode: the selector picks the SKIN. Repoint and reload the buckets.
                    if (_portraitSet == null)
                    {
                        TryEnterPortraitMode();
                    }
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

                    // Hide animated, show static
                    ImgAvatarAnimated.Visibility = Visibility.Collapsed;
                    AnimationBehavior.SetSourceUri(ImgAvatarAnimated, null);
                    ImgAvatar.Visibility = Visibility.Visible;

                    _avatarPoses = LoadAvatarPoses(setNumber);
                    _currentPoseIndex = 0;
                    if (_avatarPoses.Length > 0)
                    {
                        ImgAvatar.Source = _avatarPoses[0];
                    }

                    // Restart pose timer for static avatars (never in portrait mode — no idle rotation there)
                    if (!_portraitMode) _poseTimer.Start();
                }

                // Update UI
                UpdateTitleDisplay(App.Settings?.Current?.PlayerLevel ?? 1);
                UpdateNavigationArrows();
                ApplyAvatarTransform(setNumber);

                // Circe's Lock: engage emotes only on the base set (pose 1), leave otherwise.
                TryUpdateCirceEmoteMode();
            };

            if (animate)
            {
                // Fade transition
                var target = _useAnimatedAvatar ? (UIElement)ImgAvatarAnimated : ImgAvatar;
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s, args) =>
                {
                    if (gen != _avatarSwitchGen)
                    {
                        // A newer swap superseded this one — its own fade restores the border opacity.
                        App.Logger?.Information("[AVATAR] swap to set {Set} superseded (gen {Gen}/{Cur})",
                            setNumber, gen, _avatarSwitchGen);
                        return;
                    }
                    switchAction();
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                    AvatarBorder.BeginAnimation(OpacityProperty, fadeIn);
                };
                AvatarBorder.BeginAnimation(OpacityProperty, fadeOut);
            }
            else
            {
                switchAction();
            }

            App.Logger?.Information("Switched to avatar set {Set} (animated: {Animated})", setNumber, _useAnimatedAvatar);
        }

        /// <summary>
        /// Gets the companion ID that corresponds to an avatar set.
        /// Returns null for sets 1-2 (pre-level 35 avatars without companions).
        /// </summary>
        private static Models.CompanionId? GetCompanionForAvatarSet(int setNumber)
        {
            return setNumber switch
            {
                3 => Models.CompanionId.OGBambiSprite,      // Level 50: Synthetic Blowdoll
                4 => Models.CompanionId.CultBunny,          // Level 100: Perfect Fuckpuppet
                5 => Models.CompanionId.BrainParasite,      // Level 125: Brainwashed Slavedoll
                6 => Models.CompanionId.BambiTrainer,       // Level 150: Platinum Puppet
                7 => Models.CompanionId.BimboCow,           // Level 75: Bambi Cow
                _ => null                                    // Sets 1-2 have no companion
            };
        }

        /// <summary>
        /// Gets the avatar set that corresponds to a companion.
        /// Used when switching companions from the UI to update the avatar.
        /// </summary>
        public static int GetAvatarSetForCompanion(Models.CompanionId companionId)
        {
            return companionId switch
            {
                Models.CompanionId.OGBambiSprite => 3,   // Synthetic Blowdoll
                Models.CompanionId.CultBunny => 4,       // Perfect Fuckpuppet
                Models.CompanionId.BrainParasite => 5,   // Brainwashed Slavedoll
                Models.CompanionId.BambiTrainer => 6,    // Platinum Puppet
                Models.CompanionId.BimboCow => 7,        // Bambi Cow
                _ => 1
            };
        }

        /// <summary>
        /// Update the title and level display.
        /// Shows companion name based on current avatar set (v5.3).
        /// </summary>
        private void UpdateTitleDisplay(int level)
        {
            // Portrait mode: the avatar-set selector picks a skin (outfit) — title from the manifest skin.
            if (_portraitMode && _portraitSet != null && _portraitSet.SkinCount > 0)
            {
                int si = _portraitSet.ClampSkin(_skinIndex);
                var skin = _portraitSet.Skins[si];
                var skinTitle = string.IsNullOrWhiteSpace(skin.Title) ? skin.Id : skin.Title;
                skinTitle = App.Mods?.MakeModAware(skinTitle) ?? skinTitle;
                TxtAvatarTitle.Text = (skinTitle ?? "").ToUpperInvariant();
                TxtAvatarLevel.Visibility = Visibility.Collapsed;
                return;
            }

            // v5.3: Show companion name based on current avatar set
            var companionId = GetCompanionForAvatarSet(_currentAvatarSet);

            if (companionId.HasValue && App.Companion != null)
            {
                var companionDef = Models.CompanionDefinition.GetById(companionId.Value);
                var companionProgress = App.Companion.GetProgress(companionId.Value);
                bool isSlutMode = App.Settings?.Current?.SlutModeEnabled ?? false;

                var displayName = companionDef.GetDisplayName(isSlutMode);
                displayName = App.Mods?.MakeModAware(displayName) ?? displayName;
                TxtAvatarTitle.Text = displayName.ToUpperInvariant();
                TxtAvatarLevel.Visibility = Visibility.Visible;
                TxtAvatarLevel.Text = companionProgress.IsMaxLevel
                    ? Loc.Get("avatar_level_max")
                    : Loc.GetF("avatar_level_format", companionProgress.Level);
            }
            else
            {
                // For sets 1-3 (pre-level 50), use legacy avatar titles
                int titleIndex = Math.Clamp(_currentAvatarSet - 1, 0, AvatarTitleKeys.Length - 1);
                var title = Loc.Get(AvatarTitleKeys[titleIndex]);
                title = App.Mods?.MakeModAware(title) ?? title;
                TxtAvatarTitle.Text = title;

                // Hide level for the first 2 generic sprites (sets 1-2) to avoid confusion with persona levels
                if (_currentAvatarSet <= 2)
                {
                    TxtAvatarLevel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TxtAvatarLevel.Visibility = Visibility.Visible;
                    TxtAvatarLevel.Text = Loc.GetF("avatar_level_format", level);
                }
            }
        }

        /// <summary>
        /// Refreshes the companion display when companion changes or levels up.
        /// Called from CompanionService events.
        /// </summary>
        public void RefreshCompanionDisplay()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshCompanionDisplay));   // async: a sync cross-thread Invoke can deadlock at shutdown
                return;
            }

            UpdateTitleDisplay(App.Settings?.Current?.PlayerLevel ?? 1);
        }

        /// <summary>
        /// Called when the active mod changes. Refreshes tube image, avatar poses, and titles.
        /// </summary>
        private void OnModChanged()
        {
            try
            {
                // Refresh tube frame
                SetTubeStyle(!_isAttached);

                // Apply tube layout offsets for new mod's tube glass position
                ApplyTubeLayoutOffsets();

                // Reload video links for companion speech bubbles
                ReloadVideoLinks();

                // Validate current avatar set is supported by the new mod — if not, fall back.
                int playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
                if (IsSingleEmoteAvatarMod(out int emoteOnlySet))
                {
                    // BambiSleep / Sissy: single animated avatar, no picker — lock to that set. Don't
                    // persist it, so the level selection is preserved for mods that still use the picker.
                    _currentAvatarSet = _selectedAvatarSet = emoteOnlySet;
                }
                else
                {
                    var supportedSets = GetUnlockedAvatarSets(playerLevel);
                    if (supportedSets.Length > 0 && !supportedSets.Contains(_currentAvatarSet))
                    {
                        var oldSet = _currentAvatarSet;
                        _currentAvatarSet = supportedSets[0];
                        _selectedAvatarSet = _currentAvatarSet;
                        if (App.Settings?.Current != null)
                        {
                            App.Settings.Current.SelectedAvatarSet = _selectedAvatarSet;
                        }
                        App.Logger?.Information("Avatar set {OldSet} not supported by new mod, switched to {NewSet}",
                            oldSet, _currentAvatarSet);
                    }
                }

                // Check if the new mod has an animated version for this set
                _useAnimatedAvatar = HasAnimatedAvatar(_currentAvatarSet);

                // Reload avatar from new mod. If the new mod ships a portrait manifest, switch into the
                // emotive-portrait system; otherwise tear it down and use the legacy poses/animated path.
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

                    // Hide animated, show static
                    ImgAvatarAnimated.Visibility = Visibility.Collapsed;
                    AnimationBehavior.SetSourceUri(ImgAvatarAnimated, null);
                    ImgAvatar.Visibility = Visibility.Visible;

                    _avatarPoses = LoadAvatarPoses(_currentAvatarSet);
                    _currentPoseIndex = 0;
                    if (_avatarPoses.Length > 0)
                    {
                        ImgAvatar.Source = _avatarPoses[0];
                    }

                    if (_avatarPoses.Length > 1 && !_portraitMode)
                        _poseTimer.Start();
                }

                // Update navigation arrows for supported sets
                ApplyAvatarTransform(_currentAvatarSet);
                UpdateNavigationArrows();

                // Circe's Lock pose-1: engage/leave animated WebP emotes after the normal setup.
                TryUpdateCirceEmoteMode();

                // Refresh voice lines from new mod
                _voiceLinesPath = Services.CompanionPhraseService.VoiceLineFolder;
                RefreshVoiceLines();

                // Refresh title (applies text replacements)
                UpdateTitleDisplay(App.Settings?.Current?.PlayerLevel ?? 1);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to refresh resources after mod change");
            }
        }

        /// <summary>
        /// Applies the active mod's tube layout offsets to avatar, title, input, and speech bubble positions.
        /// Mod tube images may have the glass cylinder in a different position than the default,
        /// so the offset shifts all UI elements horizontally to align with the glass.
        /// </summary>
        private void ApplyTubeLayoutOffsets()
        {
            // Apply avatar scale (emote-set override wins over the mod's global TubeLayout).
            var scale = EffAvatarScale();
            if (Math.Abs(scale - 1.0) > 0.001)
            {
                var scaleTransform = new System.Windows.Media.ScaleTransform(scale, scale);
                ImgAvatar.LayoutTransform = scaleTransform;
                ImgAvatarAnimated.LayoutTransform = scaleTransform;
                ImgAvatarAnimatedB.LayoutTransform = scaleTransform; // Circe emote crossfade layer must match
            }
            else
            {
                ImgAvatar.LayoutTransform = null;
                ImgAvatarAnimated.LayoutTransform = null;
                ImgAvatarAnimatedB.LayoutTransform = null;
            }

            // When the mod only overrides the attached tube image, force the attached
            // layout in detached state too — otherwise the avatar lands outside the
            // chamber the mod author drew (bug report #172).
            var useAttachedLayout = _isAttached || ModOverridesAttachedTubeOnly();

            if (useAttachedLayout)
            {
                var dx = EffAvatarOffsetX();
                var dy = EffAvatarOffsetY();
                AvatarBorder.Margin = new Thickness(5, 100, 126 - dx, 210 + dy);
                TitleBox.Margin = new Thickness(0, 0, 121 - dx, 180);
                InputPanel.Margin = new Thickness(0, 0, 126 - dx, 520);
                SpeechBubble.Margin = new Thickness(0, 0, 125 - dx, 550);
            }
            else
            {
                var dx = EffAvatarDetachedOffsetX();
                var dy = EffAvatarDetachedOffsetY();
                // Detached nudge: 20px higher (bottom margin +20, bottom-aligned) and net 5px left
                // (right margin +10 — element is HorizontalAlignment=Center, so offset is (L-R)/2).
                AvatarBorder.Margin = new Thickness(5, 100, 436 - dx, 228 + dy);
                TitleBox.Margin = new Thickness(0, 0, 416 - dx, 193);
                InputPanel.Margin = new Thickness(0, 0, 426 - dx, 520);
                SpeechBubble.Margin = new Thickness(0, 0, 425 - dx, 550);
            }
        }

        /// <summary>
        /// Switches to the avatar set corresponding to a companion.
        /// Called when user clicks a companion in the Companion tab.
        /// </summary>
        public void SwitchToCompanionAvatar(Models.CompanionId companionId)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SwitchToCompanionAvatar(companionId)));   // async: avoid shutdown deadlock
                return;
            }

            // A single-emote mod (BambiSleep/Sissy) has exactly one animated avatar and no picker. A
            // companion swap must NOT swap that animated avatar out for a static companion set: no roster
            // card maps back to it and the nav arrows are hidden, so it was unrecoverable without
            // reloading the mod (#442). Keep the emote avatar pinned; the persona/name/XP still switches.
            if (IsSingleEmoteAvatarMod(out _))
                return;

            int targetSet = GetAvatarSetForCompanion(companionId);

            // Only switch if set is unlocked
            int playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
            if (IsAvatarSetUnlocked(targetSet, playerLevel))
            {
                SwitchToAvatarSet(targetSet, animate: true);
            }
        }

        /// <summary>
        /// Update navigation arrow visibility based on unlocked avatars
        /// </summary>
        /// <summary>
        /// The avatar sets the selector should cycle through. In portrait mode these are the manifest
        /// skins (1..SkinCount); otherwise the mod-supported unlocked sets.
        /// </summary>
        private int[] EffectiveAvatarSets()
        {
            // Mods whose only avatar is a single animated emote (BambiSleep, Sissy): one fixed set,
            // no picker — overrides portrait skins and level-gated sets so the nav arrows hide.
            if (IsSingleEmoteAvatarMod(out int onlySet)) return new[] { onlySet };

            if (_portraitMode && _portraitSet != null && _portraitSet.SkinCount > 0)
            {
                var arr = new int[_portraitSet.SkinCount];
                for (int i = 0; i < arr.Length; i++) arr[i] = i + 1;
                return arr;
            }
            return GetUnlockedAvatarSets(App.Settings?.Current?.PlayerLevel ?? 1);
        }

        private void UpdateNavigationArrows()
        {
            var unlockedSets = EffectiveAvatarSets();
            bool hasMultiple = unlockedSets.Length > 1;
            int currentIndex = System.Array.IndexOf(unlockedSets, _currentAvatarSet);

            // Previous arrow: show if not at first unlocked set
            BtnPrevAvatar.Visibility = hasMultiple && currentIndex > 0
                ? Visibility.Visible : Visibility.Collapsed;

            // Next arrow: show if not at last unlocked set
            BtnNextAvatar.Visibility = hasMultiple && currentIndex < unlockedSets.Length - 1
                ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Apply size and position transforms for different avatar sets
        /// Sets 2, 3, 4 are 12% bigger and 10px to the right
        /// </summary>
        private void ApplyAvatarTransform(int setNumber)
        {
            // Portrait mode: all skins render at the same (already-reduced) size — skip the per-set
            // +12% border zoom so base/lingerie/beach/fishnet stay consistent, and raise the avatar.
            if (_portraitMode)
            {
                AvatarBorder.RenderTransform = new System.Windows.Media.TranslateTransform(PortraitShiftX, -PortraitRaisePx);
                AvatarBorder.RenderTransformOrigin = new Point(0.5, 0.5);
                return;
            }

            if (setNumber > 1)
            {
                // Sets 2, 3, 4: 12% bigger, 10px to the right
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(new ScaleTransform(1.12, 1.12));
                transformGroup.Children.Add(new TranslateTransform(10, 0));
                AvatarBorder.RenderTransform = transformGroup;
                AvatarBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            else if (App.Mods?.ActiveModId == Models.BuiltInMods.LockedId)
            {
                // Locked's set 1 ("The Lure") art reads smaller than the other stages
                // (which get the +12% above); nudge it 6% bigger to match.
                AvatarBorder.RenderTransform = new ScaleTransform(1.06, 1.06);
                AvatarBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            else
            {
                // Set 1 (Basic Bimbo): no transform
                AvatarBorder.RenderTransform = null;
            }
        }

        /// <summary>
        /// Navigate to previous avatar set
        /// </summary>
        private void BtnPrevAvatar_Click(object sender, MouseButtonEventArgs e)
        {
            var unlockedSets = EffectiveAvatarSets();
            int currentIndex = System.Array.IndexOf(unlockedSets, _currentAvatarSet);
            if (currentIndex > 0)
            {
                SwitchToAvatarSet(unlockedSets[currentIndex - 1]);
                // User-initiated appearance/skin change (the arrows also repoint the portrait skin).
                try { App.Bark?.NotifyAvatarChanged(); } catch { }
            }
        }

        /// <summary>
        /// Navigate to next avatar set
        /// </summary>
        private void BtnNextAvatar_Click(object sender, MouseButtonEventArgs e)
        {
            var unlockedSets = EffectiveAvatarSets();
            int currentIndex = System.Array.IndexOf(unlockedSets, _currentAvatarSet);
            if (currentIndex >= 0 && currentIndex < unlockedSets.Length - 1)
            {
                SwitchToAvatarSet(unlockedSets[currentIndex + 1]);
                // User-initiated appearance/skin change (the arrows also repoint the portrait skin).
                try { App.Bark?.NotifyAvatarChanged(); } catch { }
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════
        //  EMOTIVE PORTRAIT AVATAR (mod-agnostic; on only when the active mod ships a manifest)
        // ════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// True when the active mod ships an avatar_manifest.json. A portrait manifest WINS over the
        /// legacy animated GIF — a mod that ships emotive portraits wants them, not the generic GIF —
        /// so this intentionally does not check <see cref="_useAnimatedAvatar"/>.
        /// </summary>
        private bool UsePortraitSystem()
        {
            return Services.AvatarPortraitLoader.HasManifestForActiveMod();
        }

        /// <summary>
        /// Switch the avatar into the emotive-portrait system if the active mod ships a manifest;
        /// otherwise leave the legacy 4-pose path untouched. Idempotent — safe to call on ctor,
        /// mod-change, and set-switch.
        /// </summary>
        private void TryEnterPortraitMode()
        {
            try
            {
                var set = Services.AvatarPortraitLoader.Load();
                if (set == null) { LeavePortraitMode(); return; }

                _portraitSet = set;
                _portraitMode = true;
                _useAnimatedAvatar = false; // portraits replace the legacy animated GIF for this mod

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

                // Both portrait layers visible; A opaque, B transparent. Animated hidden.
                ImgAvatarAnimated.Visibility = Visibility.Collapsed;
                AnimationBehavior.SetSourceUri(ImgAvatarAnimated, null);
                ImgAvatar.Visibility = Visibility.Visible;
                ImgAvatarB.Visibility = Visibility.Visible;
                CancelCrossfade();
                ImgAvatar.Opacity = 1.0;
                ImgAvatarB.Opacity = 0.0;
                MistOverlay.Visibility = Visibility.Visible;
                ApplyPortraitChrome();

                var bucket = _portraitSet.GetBucket(_skinIndex, _currentEmotion);
                if (bucket.Length > 0) _activeImg.Source = bucket[0];

                // No idle rotation: the avatar holds a still pose until it speaks.
                _poseTimer.Stop();

                // Refresh title (skin name) + nav arrows now that portrait mode is active — the ctor ran
                // UpdateTitleDisplay before this, so without this they'd show the stale legacy set title.
                UpdateTitleDisplay(App.Settings?.Current?.PlayerLevel ?? 1);
                UpdateNavigationArrows();

                App.Logger?.Information("Avatar portrait mode ON (skin {Skin}/{Count}, emotion '{Emo}')",
                    _skinIndex, _portraitSet.SkinCount, _currentEmotion);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "TryEnterPortraitMode failed; falling back to legacy avatar");
                LeavePortraitMode();
            }
        }

        /// <summary>
        /// Portrait-mode chrome: shrink the avatar to <see cref="PortraitSizeScale"/> and drop the pink
        /// DropShadow glow (the deliberate mist overlay already supplies pink atmosphere; the glow read as
        /// an unwanted aura on the detailed portraits). Saves the original effects once for restore. Idempotent.
        /// </summary>
        private void ApplyPortraitChrome()
        {
            if (!_avatarEffectsSaved)
            {
                _savedEffectA = ImgAvatar.Effect;
                _savedEffectB = ImgAvatarB.Effect;
                _avatarEffectsSaved = true;
            }
            ImgAvatar.Effect = null;
            ImgAvatarB.Effect = null;
            ImgAvatar.MaxHeight = ImgAvatarB.MaxHeight = LegacyAvatarMaxHeight * PortraitSizeScale;
            ImgAvatar.MaxWidth = ImgAvatarB.MaxWidth = LegacyAvatarMaxWidth * PortraitSizeScale;
            // Uniform size across skins (no per-set zoom) + nudge the portrait up/right in the tube.
            AvatarBorder.RenderTransform = new System.Windows.Media.TranslateTransform(PortraitShiftX, -PortraitRaisePx);
            AvatarBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        /// <summary>Tear down the portrait system and restore the legacy single-image avatar. Idempotent.</summary>
        private void LeavePortraitMode()
        {
            _portraitMode = false;
            _portraitSet = null;
            _poseSeqTimer?.Stop();
            _emotionReturnTimer?.Stop();
            CancelCrossfade();
            try
            {
                if (ImgAvatarB != null)
                {
                    ImgAvatarB.BeginAnimation(OpacityProperty, null);
                    ImgAvatarB.Visibility = Visibility.Collapsed;
                    ImgAvatarB.Opacity = 0.0;
                    ImgAvatarB.Source = null;
                }
                if (MistOverlay != null) MistOverlay.Visibility = Visibility.Collapsed;
                if (AvatarScale != null) { AvatarScale.ScaleX = AvatarScale.ScaleY = 1.0; }
                if (AvatarRotate != null) AvatarRotate.Angle = 0;
                if (ImgAvatar != null) { ImgAvatar.BeginAnimation(OpacityProperty, null); ImgAvatar.Opacity = 1.0; }

                // Restore legacy chrome: original pink glow + full size.
                if (_avatarEffectsSaved)
                {
                    if (ImgAvatar != null) ImgAvatar.Effect = _savedEffectA;
                    if (ImgAvatarB != null) ImgAvatarB.Effect = _savedEffectB;
                }
                if (ImgAvatar != null) { ImgAvatar.MaxHeight = LegacyAvatarMaxHeight; ImgAvatar.MaxWidth = LegacyAvatarMaxWidth; }
                if (ImgAvatarB != null) { ImgAvatarB.MaxHeight = LegacyAvatarMaxHeight; ImgAvatarB.MaxWidth = LegacyAvatarMaxWidth; }
            }
            catch { /* closing/teardown — non-fatal */ }
            _activeImg = null;
            _idleImg = null;
        }

        /// <summary>Repoint the current skin (after the selector changes set) without re-parsing the manifest.</summary>
        private void ReloadPortraitSkin()
        {
            if (_portraitSet == null) return;
            CancelCrossfade();
            _activeImg = ImgAvatar;
            _idleImg = ImgAvatarB;
            ImgAvatar.Visibility = Visibility.Visible;
            ImgAvatarB.Visibility = Visibility.Visible;
            MistOverlay.Visibility = Visibility.Visible;
            ImgAvatar.Opacity = 1.0;
            ImgAvatarB.Opacity = 0.0;
            ApplyPortraitChrome();
            var bucket = _portraitSet.GetBucket(_skinIndex, _currentEmotion);
            if (bucket.Length > 0)
            {
                if (_emotionPoseIndex >= bucket.Length) _emotionPoseIndex = 0;
                _activeImg.Source = bucket[_emotionPoseIndex];
            }
        }

        /// <summary>Cancel any in-flight image crossfade animation and clear the guard.</summary>
        private void CancelCrossfade()
        {
            try
            {
                ImgAvatar?.BeginAnimation(OpacityProperty, null);
                ImgAvatarB?.BeginAnimation(OpacityProperty, null);
            }
            catch { }
            _crossfadeInFlight = false;
        }

        /// <summary>
        /// Crossfade the visible portrait to <paramref name="next"/> by fading layer A out and layer B in
        /// (then swapping their roles). Idle ticks no-op while a fade is in flight; an event with
        /// <paramref name="preempt"/> cancels the in-flight fade and switches cleanly.
        /// </summary>
        private void CrossfadeTo(BitmapImage? next, bool preempt = false)
        {
            if (next == null || _activeImg == null || _idleImg == null) return;

            if (_crossfadeInFlight)
            {
                if (!preempt) return;
                _activeImg.BeginAnimation(OpacityProperty, null);
                _idleImg.BeginAnimation(OpacityProperty, null);
                _activeImg.Opacity = 0.0;
                _idleImg.Opacity = 1.0;
                var swap = _activeImg; _activeImg = _idleImg; _idleImg = swap;
                _crossfadeInFlight = false;
            }

            if (ReferenceEquals(_activeImg.Source, next)) return; // already showing it

            var inImg = _idleImg;
            var outImg = _activeImg;
            inImg.Source = next;
            inImg.Opacity = 0.0;
            _crossfadeInFlight = true;

            int frames = _portraitSet?.Director.CrossfadeFrames ?? 4;
            var dur = TimeSpan.FromMilliseconds(Math.Max(60, frames * 38)); // 4 frames ≈ 150ms

            var fadeOut = new DoubleAnimation(1, 0, dur) { FillBehavior = FillBehavior.Stop };
            var fadeIn = new DoubleAnimation(0, 1, dur) { FillBehavior = FillBehavior.Stop };
            fadeIn.Completed += (s, e) =>
            {
                inImg.Opacity = 1.0;
                outImg.Opacity = 0.0;
                _activeImg = inImg;
                _idleImg = outImg;
                _crossfadeInFlight = false;
            };
            outImg.BeginAnimation(OpacityProperty, fadeOut);
            inImg.BeginAnimation(OpacityProperty, fadeIn);
        }

        /// <summary>
        /// A line is being spoken → drive the portrait through a short pose sequence of that line's emotion.
        /// Mapped bark lines use their manifest emotion; our event + idle affirmation lines (unmapped) get a
        /// seductive mix (mostly alluring, plus entrancing/dreamy/teasing). The number of poses scales with
        /// the line's length. No-op when the portrait system is off.
        /// </summary>
        private void PlayEmotionForLine(string? emotionLineId, string? audioPath, string? text, string? mood = null)
        {
            // Circe pose-1 animated emotes take over the spoken-line reaction (own WebP path).
            if (_circeEmoteMode) { CircePlayEmote(emotionLineId, audioPath, text, mood); return; }
            if (!_portraitMode || _portraitSet == null) return;
            var emotion = _portraitSet.EmotionForLine(emotionLineId);
            if (string.IsNullOrEmpty(emotion))
                // Bark lines carry a mood → map it to an emotion (base layer). Non-bark speech
                // (AI/trigger/canned, mood==null) keeps the alluring affirmation mix for variety.
                emotion = !string.IsNullOrWhiteSpace(mood)
                    ? _portraitSet.EmotionForMood(mood)
                    : PickAffirmationEmotion();
            double durationSec = audioPath != null ? AudioDurationSec(audioPath) : EstimateDurationSec(text);
            SetEmotionSequence(emotion!, PoseCountForDuration(durationSec), durationSec);
        }

        // Unmapped lines (our ~138 event + idle affirmation lines) read as affirmations: lean alluring,
        // mixed with entrancing/dreamy/teasing. GetBucket falls back if a mod lacks one of these.
        private static readonly string[] AffirmationEmotions =
            { "alluring", "alluring", "alluring", "entrancing", "dreamy", "teasing" };
        private string PickAffirmationEmotion() => AffirmationEmotions[_random.Next(AffirmationEmotions.Length)];

        /// <summary>Poses ∝ line length: ~1s each + a ~2s final hold should span the line. 4s ≈ 3 poses.</summary>
        private int PoseCountForDuration(double sec)
        {
            if (sec <= 0) return MinSpeakPoses;
            int n = (int)Math.Round(sec) - 1; // (n-1)*1s + 2s ≈ sec
            return Math.Clamp(n, MinSpeakPoses, MaxSpeakPoses);
        }

        /// <summary>Cached spoken-line length in seconds (NAudio), used to size the pose sequence.</summary>
        private double AudioDurationSec(string? path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            if (_audioDurCache.TryGetValue(path!, out var cached)) return cached;
            double sec = 0;
            try
            {
                if (System.IO.File.Exists(path))
                    using (var r = new NAudio.Wave.AudioFileReader(path)) sec = r.TotalTime.TotalSeconds;
            }
            catch { sec = 0; }
            _audioDurCache[path!] = sec;
            return sec;
        }

        /// <summary>Rough spoken length for text-only lines (no audio): slow, pause-heavy bimbo read.</summary>
        private double EstimateDurationSec(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 2.5;
            int words = text!.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return Math.Clamp(0.45 * words + 0.8, 2.0, 7.0);
        }

        /// <summary>
        /// Start the spoken pose sequence for <paramref name="emotion"/>: show the first pose now, then advance
        /// every <see cref="PoseStepMs"/> through <paramref name="poseCount"/> poses (drawn from this emotion's
        /// bucket), holding the last for <see cref="LastPoseLingerMs"/> before returning to a still idle pose.
        /// </summary>
        private void SetEmotionSequence(string emotion, int poseCount, double durationSec)
        {
            if (_portraitSet == null) return;
            var bucket = _portraitSet.GetBucket(_skinIndex, emotion);
            if (bucket.Length == 0) return;

            _currentEmotion = emotion;
            poseCount = Math.Clamp(poseCount, MinSpeakPoses, MaxSpeakPoses);
            _seqOrder = BuildPoseOrder(bucket.Length, poseCount);
            _seqStep = 0;

            // Short lines flip ~2x faster so the poses keep pace with the brief audio.
            bool shortLine = durationSec > 0 && durationSec < ShortLineSec;
            _seqStepMs = shortLine ? (int)(PoseStepMs * ShortSpeedFactor) : PoseStepMs;
            _seqLastMs = shortLine ? (int)(LastPoseLingerMs * ShortSpeedFactor) : LastPoseLingerMs;

            if (_poseSeqTimer == null)
            {
                _poseSeqTimer = new DispatcherTimer();
                _poseSeqTimer.Tick += PoseSeqTimer_Tick;
            }
            _poseSeqTimer.Stop();
            _emotionReturnTimer?.Stop();
            _poseTimer.Stop(); // belt-and-suspenders: never idle-rotate during a spoken sequence

            int first = _seqOrder.Length > 0 ? _seqOrder[0] : 0;
            _emotionPoseIndex = first;
            CrossfadeTo(bucket[first], preempt: true); // preempt so rapid lines switch cleanly

            bool firstIsLast = _seqOrder.Length <= 1;
            _poseSeqTimer.Interval = TimeSpan.FromMilliseconds(firstIsLast ? _seqLastMs : _seqStepMs);
            _poseSeqTimer.Start();
        }

        /// <summary>N-long order of bucket indices: shuffled, avoiding immediate repeats; cycles if N &gt; bucket size.</summary>
        private int[] BuildPoseOrder(int bucketLen, int n)
        {
            if (bucketLen <= 0) return System.Array.Empty<int>();
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
                ReturnToIdleEmotion(); // last pose's hold elapsed
                return;
            }

            var bucket = _portraitSet.GetBucket(_skinIndex, _currentEmotion);
            if (bucket.Length == 0) { ReturnToIdleEmotion(); return; }

            int idx = _seqOrder[_seqStep] % bucket.Length;
            _emotionPoseIndex = idx;
            CrossfadeTo(bucket[idx], preempt: true);

            bool isLast = _seqStep == _seqOrder.Length - 1;
            _poseSeqTimer!.Interval = TimeSpan.FromMilliseconds(isLast ? _seqLastMs : _seqStepMs);
            _poseSeqTimer.Start();
        }

        private void EmotionReturnTimer_Tick(object? sender, EventArgs e)
        {
            _emotionReturnTimer?.Stop();
            ReturnToIdleEmotion();
        }

        /// <summary>Settle on a still idle pose (one crossfade, then NO further rotation until the next line).</summary>
        private void ReturnToIdleEmotion()
        {
            if (!_portraitMode || _portraitSet == null) return;
            _poseSeqTimer?.Stop();
            _currentEmotion = _portraitSet.IdleEmotion;
            var bucket = _portraitSet.GetBucket(_skinIndex, _currentEmotion);
            if (bucket.Length > 0)
            {
                int idx = _random.Next(bucket.Length); // tiny variety per return; not a continuous rotation
                _emotionPoseIndex = idx;
                CrossfadeTo(bucket[idx], preempt: true);
            }
        }

        /// <summary>
        /// Load avatar poses for a specific set
        /// </summary>
        /// <param name="setNumber">1 = default, 2 = level 20, 3 = level 35, 4 = level 50, 5 = level 125, 6 = level 150</param>
        private BitmapImage[] LoadAvatarPoses(int setNumber = 1)
        {
            var poses = new BitmapImage[4];

            // Determine the resource path based on set number
            // Set 1: avatar_pose1.png - avatar_pose4.png (original)
            // Set 2: avatar2_pose1.png - avatar2_pose4.png (level 20)
            // Set 3: avatar3_pose1.png - avatar3_pose4.png (level 35)
            // Set 4: avatar4_pose1.png - avatar4_pose4.png (level 50)
            // Set 5: avatar5_pose1.png - avatar5_pose4.png (level 125)
            // Set 6: avatar6_pose1.png - avatar6_pose4.png (level 150)
            string prefix = setNumber == 1 ? "avatar_pose" : $"avatar{setNumber}_pose";
            
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    var resolved = Services.ModResourceResolver.ResolveImage($"{prefix}{i + 1}.png");
                    if (resolved is BitmapImage bmp)
                    {
                        poses[i] = bmp.IsFrozen ? bmp : bmp.Clone();
                        if (!poses[i].IsFrozen) poses[i].Freeze();
                    }
                    else
                    {
                        var uri = new Uri($"pack://application:,,,/Resources/{prefix}{i + 1}.png", UriKind.Absolute);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = uri;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        poses[i] = bitmap;
                    }
                    
                    App.Logger?.Debug("Loaded avatar pose: {Prefix}{Index}.png", prefix, i + 1);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("Failed to load avatar pose {Prefix}{Index}: {Error}", prefix, i + 1, ex.Message);
                    
                    // Try to fall back to default avatar set if a higher set fails to load
                    if (setNumber > 1)
                    {
                        try
                        {
                            var fallbackResolved = Services.ModResourceResolver.ResolveImage($"avatar_pose{i + 1}.png");
                            if (fallbackResolved is BitmapImage fbmp)
                            {
                                poses[i] = fbmp.IsFrozen ? fbmp : fbmp.Clone();
                                if (!poses[i].IsFrozen) poses[i].Freeze();
                            }
                            else
                            {
                                var fallbackUri = new Uri($"pack://application:,,,/Resources/avatar_pose{i + 1}.png", UriKind.Absolute);
                                var fallbackBitmap = new BitmapImage();
                                fallbackBitmap.BeginInit();
                                fallbackBitmap.UriSource = fallbackUri;
                                fallbackBitmap.CacheOption = BitmapCacheOption.OnLoad;
                                fallbackBitmap.EndInit();
                                fallbackBitmap.Freeze();
                                poses[i] = fallbackBitmap;
                            }
                            App.Logger?.Debug("Fell back to default avatar pose {Index}", i + 1);
                        }
                        catch
                        {
                            poses[i] = new BitmapImage();
                        }
                    }
                    else
                    {
                        poses[i] = new BitmapImage();
                    }
                }
            }
            
            return poses;
        }

        private void PoseTimer_Tick(object? sender, EventArgs e)
        {
            if (_portraitMode) return; // portrait mode never rotates on idle (poses change only while speaking)

            if (_avatarPoses.Length == 0) return;

            _currentPoseIndex = (_currentPoseIndex + 1) % _avatarPoses.Length;

            // Use FillBehavior.Stop to prevent animations from holding onto the property
            var fadeOut = new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(150))
            {
                FillBehavior = FillBehavior.Stop
            };
            fadeOut.Completed += (s, args) =>
            {
                ImgAvatar.Source = _avatarPoses[_currentPoseIndex];
                ImgAvatar.Opacity = 1.0; // Reset opacity after fade out completes
            };
            ImgAvatar.BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
