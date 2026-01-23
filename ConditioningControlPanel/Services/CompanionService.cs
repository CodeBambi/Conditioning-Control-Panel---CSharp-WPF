using System;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Source of XP for tracking companion bonuses.
    /// </summary>
    public enum XPSource
    {
        Flash,
        Video,
        Subliminal,
        Bubble,
        LockCard,
        Session,
        BubbleCount,
        BouncingText,
        AvatarInteraction,
        Other
    }

    /// <summary>
    /// Context for XP calculation - provides info about how the XP was earned.
    /// </summary>
    public class XPContext
    {
        /// <summary>Whether this XP was triggered by autonomy mode.</summary>
        public bool TriggeredByAutonomy { get; set; }

        /// <summary>Whether strict mode was enabled when earning this XP.</summary>
        public bool IsStrictMode { get; set; }

        /// <summary>Whether panic key is disabled (No Escape mode).</summary>
        public bool IsNoEscapeMode { get; set; }

        /// <summary>Whether attention checks are enabled.</summary>
        public bool AttentionChecksEnabled { get; set; }

        /// <summary>Current pink filter opacity (0-50).</summary>
        public int PinkFilterOpacity { get; set; }

        /// <summary>
        /// Creates a context from current app settings.
        /// </summary>
        public static XPContext FromCurrentSettings()
        {
            var settings = App.Settings?.Current;
            return new XPContext
            {
                TriggeredByAutonomy = App.Autonomy?.IsActionInProgress == true,
                IsStrictMode = settings?.StrictLockEnabled ?? false,
                IsNoEscapeMode = settings?.PanicKeyEnabled == false,
                AttentionChecksEnabled = settings?.AttentionChecksEnabled ?? false,
                PinkFilterOpacity = settings?.PinkFilterEnabled == true ? (settings?.PinkFilterOpacity ?? 0) : 0
            };
        }
    }

    /// <summary>
    /// Manages companion switching, XP routing, and companion-specific mechanics.
    /// Each companion has their own level that only increases when they're active.
    /// </summary>
    public class CompanionService : IDisposable
    {
        // Events
        public event EventHandler<(CompanionId Companion, int NewLevel)>? CompanionLevelUp;
        public event EventHandler<CompanionId>? CompanionSwitched;
        public event EventHandler<double>? XPDrained; // For Brain Parasite
        public event EventHandler<(CompanionId Companion, double Amount, double Modifier)>? XPAwarded;

        // XP Drain timer (for Brain Parasite)
        private DispatcherTimer? _drainTimer;
        private const double DRAIN_XP_PER_MINUTE = 5.0;
        private const double DRAIN_INTERVAL_SECONDS = 60.0;

        // Active time tracking
        private DateTime _lastActiveTimeUpdate = DateTime.Now;
        private DispatcherTimer? _activeTimeTimer;

        private bool _disposed;

        /// <summary>
        /// Gets the currently active companion ID.
        /// </summary>
        public CompanionId ActiveCompanion =>
            (CompanionId)(App.Settings?.Current?.ActiveCompanionId ?? 0);

        /// <summary>
        /// Gets the definition for the active companion.
        /// </summary>
        public CompanionDefinition ActiveCompanionDef =>
            CompanionDefinition.GetById(ActiveCompanion);

        /// <summary>
        /// Gets the progress for the active companion.
        /// </summary>
        public CompanionProgress ActiveProgress =>
            GetProgress(ActiveCompanion);

        public CompanionService()
        {
            // Start active time tracking
            _activeTimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _activeTimeTimer.Tick += OnActiveTimeTick;
            _activeTimeTimer.Start();
            _lastActiveTimeUpdate = DateTime.Now;

            // Initialize drain timer if Brain Parasite is active
            UpdateDrainTimer();

            App.Logger?.Information("CompanionService initialized. Active companion: {Companion}",
                ActiveCompanionDef.Name);
        }

        /// <summary>
        /// Gets the progress for a specific companion.
        /// Creates default progress if not yet tracked.
        /// </summary>
        public CompanionProgress GetProgress(CompanionId id)
        {
            var settings = App.Settings?.Current;
            if (settings == null)
                return CompanionProgress.CreateNew(id);

            if (!settings.CompanionProgressData.TryGetValue((int)id, out var progress))
            {
                progress = CompanionProgress.CreateNew(id);
                settings.CompanionProgressData[(int)id] = progress;
            }
            return progress;
        }

        /// <summary>
        /// Switches to a different companion. No cooldown - free switching.
        /// Requires player to have reached the companion's unlock level.
        /// </summary>
        public bool SwitchCompanion(CompanionId newCompanion)
        {
            var def = CompanionDefinition.GetById(newCompanion);
            var playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;

            // Validate player level for unlock requirement
            if (playerLevel < def.RequiredLevel)
            {
                App.Logger?.Warning("Cannot switch to {Companion} - requires Level {Level}, player is Level {PlayerLevel}",
                    def.Name, def.RequiredLevel, playerLevel);
                return false;
            }

            var oldCompanion = ActiveCompanion;
            if (oldCompanion == newCompanion)
                return true; // Already active

            // Update active time for old companion
            UpdateActiveTime();

            // Switch
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.ActiveCompanionId = (int)newCompanion;
                App.Settings.Save();
            }

            // Mark first activation if needed
            var progress = GetProgress(newCompanion);
            if (progress.FirstActivated == DateTime.MinValue)
            {
                progress.FirstActivated = DateTime.Now;
            }

            // Restart/stop drain timer based on new companion
            UpdateDrainTimer();

            // Auto-switch to companion's assigned prompt if one exists
            ApplyCompanionPrompt(newCompanion);

            CompanionSwitched?.Invoke(this, newCompanion);
            App.Logger?.Information("Switched companion: {Old} -> {New}",
                CompanionDefinition.GetById(oldCompanion).Name, def.Name);

            return true;
        }

        /// <summary>
        /// Calculates the XP modifier based on active companion and context.
        /// </summary>
        public double CalculateXPModifier(XPSource source, XPContext context)
        {
            var companion = ActiveCompanionDef;
            double modifier = 1.0;

            switch (companion.BonusType)
            {
                case CompanionBonusType.PinkFilterBonus:
                    // OG: Bonus based on pink filter opacity (0-50%)
                    // At 50% opacity: 1.5x multiplier
                    if (context.PinkFilterOpacity > 0)
                    {
                        modifier = 1.0 + (context.PinkFilterOpacity / 100.0);
                    }
                    break;

                case CompanionBonusType.AutonomyBonus:
                    // Bunny: +50% when autonomy triggered the action
                    if (context.TriggeredByAutonomy)
                    {
                        modifier = 1.5;
                    }
                    break;

                case CompanionBonusType.StrictModeBonus:
                    // Trainer: Complex modifiers
                    if (!context.IsStrictMode)
                    {
                        // -50% without strict mode
                        modifier = 0.5;
                    }
                    else if (context.IsNoEscapeMode && context.AttentionChecksEnabled)
                    {
                        // +100% for "No Escape" (strict + attention + no panic)
                        modifier = 2.0;
                    }
                    // else 1.0x with just strict mode
                    break;

                case CompanionBonusType.XPDrain:
                    // Parasite doesn't modify XP gain - only drains via timer
                    modifier = 1.0;
                    break;
            }

            return modifier;
        }

        /// <summary>
        /// Adds XP to the active companion with appropriate modifiers.
        /// </summary>
        public void AddCompanionXP(double baseAmount, XPSource source, XPContext? context = null)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var companionId = ActiveCompanion;
            var progress = GetProgress(companionId);

            // Don't award XP at max level
            if (progress.IsMaxLevel)
            {
                App.Logger?.Debug("Companion {Companion} is max level, XP not awarded", companionId);
                return;
            }

            // Build context from current settings if not provided
            context ??= XPContext.FromCurrentSettings();

            // Apply companion modifier
            var modifier = CalculateXPModifier(source, context);
            var finalAmount = baseAmount * modifier;

            // Add XP
            progress.CurrentXP += finalAmount;
            progress.TotalXPEarned += finalAmount;

            App.Logger?.Debug("Companion XP: {Companion} +{Amount:F1} (base: {Base}, modifier: {Modifier:F2}x, source: {Source})",
                companionId, finalAmount, baseAmount, modifier, source);

            // Check level up
            while (progress.CurrentXP >= progress.XPForNextLevel && !progress.IsMaxLevel)
            {
                progress.CurrentXP -= progress.XPForNextLevel;
                progress.Level++;

                App.Logger?.Information("Companion {Companion} leveled up to {Level}!",
                    ActiveCompanionDef.Name, progress.Level);

                CompanionLevelUp?.Invoke(this, (companionId, progress.Level));

                // Trigger haptics for level up
                _ = App.Haptics?.LevelUpPatternAsync();
            }

            // Fire XP awarded event for UI updates
            XPAwarded?.Invoke(this, (companionId, finalAmount, modifier));

            App.Settings.Save();
        }

        /// <summary>
        /// Called when attention check is failed - applies Trainer penalty.
        /// </summary>
        public void OnAttentionCheckFailed()
        {
            if (ActiveCompanionDef.BonusType != CompanionBonusType.StrictModeBonus)
                return;

            var progress = ActiveProgress;
            var penalty = 25.0;

            // Can't go below 0 XP
            progress.CurrentXP = Math.Max(0, progress.CurrentXP - penalty);
            App.Settings?.Save();

            App.Logger?.Information("Trainer penalty: -{Penalty} XP for attention check fail. Current XP: {XP:F1}",
                penalty, progress.CurrentXP);
        }

        /// <summary>
        /// Gets a summary of the companion's status for UI display.
        /// </summary>
        public string GetCompanionStatusText()
        {
            var def = ActiveCompanionDef;
            var progress = ActiveProgress;

            if (progress.IsMaxLevel)
                return $"{def.Name} - MAX LEVEL!";

            return $"{def.Name} - Lv.{progress.Level} ({progress.LevelProgress:P0})";
        }

        /// <summary>
        /// Checks if a companion is unlocked based on current player level.
        /// </summary>
        public bool IsCompanionUnlocked(CompanionId id)
        {
            var def = CompanionDefinition.GetById(id);
            var playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
            return playerLevel >= def.RequiredLevel;
        }

        /// <summary>
        /// Checks if a companion is unlocked based on current player level.
        /// </summary>
        public bool IsCompanionUnlocked(int id)
        {
            return IsCompanionUnlocked((CompanionId)id);
        }

        #region Drain Timer (Brain Parasite)

        private void UpdateDrainTimer()
        {
            _drainTimer?.Stop();
            _drainTimer = null;

            if (ActiveCompanionDef.BonusType == CompanionBonusType.XPDrain)
            {
                _drainTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(DRAIN_INTERVAL_SECONDS)
                };
                _drainTimer.Tick += OnDrainTick;
                _drainTimer.Start();

                App.Logger?.Information("Brain Parasite drain timer started ({DrainRate} XP/min)",
                    DRAIN_XP_PER_MINUTE);
            }
        }

        private void OnDrainTick(object? sender, EventArgs e)
        {
            var progress = ActiveProgress;

            // Can't drain below 0 or at max level
            if (progress.CurrentXP <= 0 || progress.IsMaxLevel)
                return;

            progress.CurrentXP = Math.Max(0, progress.CurrentXP - DRAIN_XP_PER_MINUTE);
            App.Settings?.Save();

            XPDrained?.Invoke(this, DRAIN_XP_PER_MINUTE);

            App.Logger?.Debug("Brain Parasite drained {Amount} XP. Current: {XP:F1}",
                DRAIN_XP_PER_MINUTE, progress.CurrentXP);
        }

        #endregion

        #region Active Time Tracking

        private void OnActiveTimeTick(object? sender, EventArgs e)
        {
            UpdateActiveTime();
        }

        private void UpdateActiveTime()
        {
            var progress = ActiveProgress;
            var elapsed = DateTime.Now - _lastActiveTimeUpdate;
            progress.TotalActiveTime += elapsed;
            _lastActiveTimeUpdate = DateTime.Now;
        }

        #endregion

        #region Migration

        /// <summary>
        /// Migrates existing users from the old single-level system.
        /// Called on first load when CompanionProgressData is empty.
        /// </summary>
        public static void MigrateFromLegacy(AppSettings settings)
        {
            if (settings.CompanionProgressData.Count > 0)
                return; // Already migrated

            // Give existing users a head start with OG companion
            // based on their user level (half their level, max 50)
            var startingLevel = Math.Min(50, settings.PlayerLevel / 2);
            startingLevel = Math.Max(1, startingLevel); // Minimum level 1

            var ogProgress = CompanionProgress.CreateNew(CompanionId.OGBambiSprite);
            ogProgress.Level = startingLevel;
            ogProgress.FirstActivated = DateTime.Now;

            settings.CompanionProgressData[(int)CompanionId.OGBambiSprite] = ogProgress;
            settings.ActiveCompanionId = (int)CompanionId.OGBambiSprite;

            App.Logger?.Information("Migrated user to companion system. OG Bambi Sprite starting at level {Level}",
                startingLevel);
        }

        #endregion

        #region Companion-Prompt Association

        /// <summary>
        /// Applies the prompt assigned to a companion (if any).
        /// Called automatically when switching companions.
        /// </summary>
        private void ApplyCompanionPrompt(CompanionId companion)
        {
            try
            {
                var promptId = App.Settings?.Current?.GetCompanionPromptId((int)companion);

                if (string.IsNullOrEmpty(promptId))
                {
                    // No prompt assigned - could optionally revert to default here
                    // For now, we leave the current prompt active
                    App.Logger?.Debug("Companion {Companion} has no assigned prompt", companion);
                    return;
                }

                // Activate the assigned prompt
                App.CommunityPrompts?.ActivatePrompt(promptId);
                App.Logger?.Information("Activated prompt '{PromptId}' for companion {Companion}",
                    promptId, CompanionDefinition.GetById(companion).Name);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to apply companion prompt");
            }
        }

        /// <summary>
        /// Gets the name of the prompt assigned to a companion, or null if none.
        /// </summary>
        public static string? GetAssignedPromptName(CompanionId companion)
        {
            var promptId = App.Settings?.Current?.GetCompanionPromptId((int)companion);
            if (string.IsNullOrEmpty(promptId))
                return null;

            var prompt = App.CommunityPrompts?.GetInstalledPrompt(promptId);
            return prompt?.Name;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Update final active time
            UpdateActiveTime();

            _drainTimer?.Stop();
            _drainTimer = null;

            _activeTimeTimer?.Stop();
            _activeTimeTimer = null;
        }
    }
}
