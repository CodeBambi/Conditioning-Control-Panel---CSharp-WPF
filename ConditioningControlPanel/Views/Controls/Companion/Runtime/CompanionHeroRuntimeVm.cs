using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Z0 — the header band, wired to the real entitlement state.
    ///
    /// <para>The plate is a glance, never a wall: <see cref="HasAiAccess"/> only dims it and shows
    /// the ribbon, and both are buttons into the existing upgrade flow (<c>ShowTab("patreon")</c>,
    /// which this app routes to the App Info popup that owns account/upgrade today).</para>
    /// </summary>
    internal sealed class CompanionHeaderRuntimeVm : CompanionObservable, ICompanionHeaderVm
    {
        private readonly CompanionRuntimeContext _ctx;
        private bool _hasAiAccess;

        public CompanionHeaderRuntimeVm(CompanionRuntimeContext ctx)
        {
            _ctx = ctx;
            TutorialCommand = new CompanionRelayCommand(
                () => _ctx.WithWindow(w => w.BtnCompanionTutorial_Click(this, CompanionRuntimeContext.Routed())));
            OpenPatreonCommand = new CompanionRelayCommand(
                () => _ctx.WithWindow(w => w.ShowTab("patreon")));
            Sync();
        }

        public string Title => CompanionLocStaging.Resolve("companion_header_title");
        public string Subtitle => CompanionLocStaging.Resolve("companion_header_subtitle");
        public string TutorialLabel => CompanionLocStaging.Resolve("companion_header_tutorial");
        public string AiPlateLabel => CompanionLocStaging.Resolve("companion_header_plate_ai");
        public string NextTierPlateLabel => CompanionLocStaging.Resolve("companion_header_plate_next");
        public string TeaserRibbonLabel => CompanionLocStaging.Resolve("companion_header_teaser");

        public bool HasAiAccess
        {
            get => _hasAiAccess;
            private set => Set(ref _hasAiAccess, value);
        }

        public ICommand TutorialCommand { get; }
        public ICommand OpenPatreonCommand { get; }

        /// <summary>Re-reads entitlement. Called from the room's Sync, which MainWindow drives.</summary>
        public void Sync() => HasAiAccess = App.Patreon?.HasAiAccess == true;
    }

    /// <summary>
    /// Z1 — the Companion Card against the live services.
    ///
    /// <para>Sources, one per line of the design's disposition table:</para>
    /// <list type="bullet">
    ///   <item>name / description / level / XP — <c>App.Companion</c>, the same
    ///   <see cref="CompanionProgress"/> the old hero strip read. The "Companion XP bar" in the
    ///   design is the COMPANION's progression, not the player's; sourcing it from the player's
    ///   level would have been a silent behaviour change dressed as a reskin.</item>
    ///   <item>mod chip — <c>App.Mods.ActiveMod</c>.</item>
    ///   <item>portrait — the old <c>RefreshHeroAvatar</c> pipeline, moved here whole.</item>
    ///   <item>pills — <c>AiChatEnabled</c> + provider + <c>AwarenessModeEnabled</c>, exactly what
    ///   <c>UpdateAiBrainPills</c> read.</item>
    /// </list>
    ///
    /// <para>The mood token and the constellation stay dormant: Train 4 owns both, and the zone
    /// already ships designed sleeping states for them.</para>
    /// </summary>
    internal sealed class CompanionHeroRuntimeVm : CompanionObservable, ICompanionHeroCardVm
    {
        private readonly CompanionRuntimeContext _ctx;

        private string _name = string.Empty;
        private string _modName = string.Empty;
        private string _flavor = string.Empty;
        private ImageSource? _portrait;
        private bool _isCompanionEnabled = true;
        private bool _isAiLive;
        private bool _isAiLocked;
        private bool _isAwarenessOpen;
        private string _aiPillText = string.Empty;
        private string _awarenessPillText = string.Empty;
        private int _level = 1;
        private double _xpFraction;
        private string _xpLabel = string.Empty;
        private string _nextLevelLabel = string.Empty;
        private string _chatShortcutHint = string.Empty;
        private bool _isMuted;
        private bool _isCompanionShown = true;

        public CompanionHeroRuntimeVm(CompanionRuntimeContext ctx)
        {
            _ctx = ctx;
            Header = new CompanionHeaderRuntimeVm(ctx);
            Constellation = new RelationshipConstellationRuntimeVm();

            ChatCommand = new CompanionRelayCommand(
                () => CompanionRuntimeContext.Guarded(() => App.AvatarWindow?.OpenChatInput(), "open chat"));
            SwitchCommand = new CompanionRelayCommand(
                () => _ctx.Navigator?.RevealWorkshop(CompanionRoomAnchors.WorkshopRosterCell));
            DetachCommand = new CompanionRelayCommand(
                () => _ctx.WithWindow(w => w.BtnDetachCompanion_Click(this, CompanionRuntimeContext.Routed())));
            ToggleMuteCommand = new CompanionRelayCommand(
                () => _ctx.WithWindow(w => { w.SetAvatarMuted(!IsMuted); Sync(); }));
            ToggleShownCommand = new CompanionRelayCommand(
                () => _ctx.WithWindow(w => { w.SetAvatarEnabled(!IsCompanionShown); Sync(); }));
            WakeCommand = new CompanionRelayCommand(
                () => _ctx.WithWindow(w => { w.SetAvatarEnabled(true); Sync(); }));
            OpenEngineRoomCommand = new CompanionRelayCommand(() => _ctx.Navigator?.RevealEngineRoom());
            FocusAwarenessCommand = new CompanionRelayCommand(() => _ctx.Navigator?.FocusAwareness());

            Sync();
        }

        // ---- identity ----
        public string Name { get => _name; private set => Set(ref _name, value); }
        public string ModName { get => _modName; private set => Set(ref _modName, value); }
        public string Flavor { get => _flavor; private set => Set(ref _flavor, value); }
        public ImageSource? Portrait { get => _portrait; private set => Set(ref _portrait, value); }

        // ---- state ----
        public bool IsCompanionEnabled { get => _isCompanionEnabled; private set => Set(ref _isCompanionEnabled, value); }
        public bool IsAiLive { get => _isAiLive; private set => Set(ref _isAiLive, value); }
        public bool IsAiLocked { get => _isAiLocked; private set => Set(ref _isAiLocked, value); }
        public bool IsAwarenessOpen { get => _isAwarenessOpen; private set => Set(ref _isAwarenessOpen, value); }
        public string AiPillText { get => _aiPillText; private set => Set(ref _aiPillText, value); }
        public string AwarenessPillText { get => _awarenessPillText; private set => Set(ref _awarenessPillText, value); }
        public string AsleepCopy => CompanionLocStaging.Resolve("companion_hero_asleep_copy");

        // ---- mood token: Train 4 ----
        public bool IsMoodLive => false;
        public string MoodGlyph => "✧";
        public string MoodWord => CompanionLocStaging.Resolve("companion_hero_mood_asleep");
        public string MoodCaption => CompanionLocStaging.Resolve("companion_hero_mood_caption_dormant");

        // ---- progression ----
        public int Level { get => _level; private set => Set(ref _level, value); }
        public double XpFraction { get => _xpFraction; private set => Set(ref _xpFraction, value); }
        public string XpLabel { get => _xpLabel; private set => Set(ref _xpLabel, value); }
        public string NextLevelLabel { get => _nextLevelLabel; private set => Set(ref _nextLevelLabel, value); }

        // ---- quick actions ----
        public string ChatShortcutHint { get => _chatShortcutHint; private set => Set(ref _chatShortcutHint, value); }
        public bool IsMuted { get => _isMuted; private set => Set(ref _isMuted, value); }
        public bool IsCompanionShown { get => _isCompanionShown; private set => Set(ref _isCompanionShown, value); }

        public ICommand ChatCommand { get; }
        public ICommand SwitchCommand { get; }
        public ICommand DetachCommand { get; }
        public ICommand ToggleMuteCommand { get; }
        public ICommand ToggleShownCommand { get; }
        public ICommand OpenEngineRoomCommand { get; }
        public ICommand FocusAwarenessCommand { get; }
        public ICommand WakeCommand { get; }

        public IRelationshipConstellationVm Constellation { get; }
        ICompanionHeaderVm? ICompanionHeroCardVm.Header => Header;

        /// <summary>The header band, typed, so the room can sync it without a cast.</summary>
        public CompanionHeaderRuntimeVm Header { get; }

        // =====================================================================================

        /// <summary>
        /// Re-reads everything the card shows. Cheap and idempotent — MainWindow calls it from the
        /// same places it used to call <c>UpdateCompanionCardsUI</c> / <c>UpdateAiBrainPills</c>.
        /// </summary>
        public void Sync()
        {
            Header.Sync();
            CompanionRuntimeContext.Guarded(SyncCore, "hero sync");
        }

        private void SyncCore()
        {
            var settings = App.Settings?.Current;
            bool slutMode = settings?.SlutModeEnabled == true;

            // ---- identity ----
            var activeId = App.Companion?.ActiveCompanion ?? CompanionId.OGBambiSprite;
            var def = CompanionDefinition.GetById(activeId);
            var display = def.GetDisplayName(slutMode);
            Name = App.Mods?.MakeModAware(display) ?? display;
            ModName = App.Mods?.ActiveMod?.Name ?? string.Empty;
            Flavor = App.Mods?.MakeModAware(def.Description) ?? def.Description;
            Portrait = LoadPortrait();

            // ---- progression (the COMPANION's bar, as the old hero strip showed) ----
            var progress = App.Companion?.ActiveProgress;
            if (progress != null)
            {
                Level = progress.Level;
                XpFraction = progress.IsMaxLevel ? 1.0 : Clamp01(progress.LevelProgress);
                XpLabel = progress.IsMaxLevel
                    ? CompanionLocStaging.Resolve("companion_hero_xp_complete")
                    : $"{progress.CurrentXP:F0} / {progress.XPForNextLevel:F0} XP";
                NextLevelLabel = progress.IsMaxLevel
                    ? CompanionLocStaging.Resolve("companion_hero_max_level")
                    : CompanionLocStaging.ResolveF("companion_hero_next_level_fmt", progress.Level + 1);
            }

            // ---- her switches ----
            IsCompanionShown = settings?.AvatarEnabled ?? true;
            IsCompanionEnabled = IsCompanionShown;
            IsMuted = settings?.AvatarMuted ?? false;
            ChatShortcutHint = SafeShortcut();

            // ---- the pills ----
            bool aiOn = settings?.AiChatEnabled == true;
            bool entitled = App.Patreon?.HasAiAccess == true || App.HasCloudIdentity;
            var provider = settings?.CompanionPrompt?.AiProvider ?? AiProviderType.Cloud;
            bool cloudish = provider == AiProviderType.Cloud;

            // Locked is only meaningful for the CLOUD provider: a local Ollama or a BYO endpoint is
            // the user's own machine and no entitlement gates it. Saying "unlock her voice" to
            // someone running their own model would be a lie with a price tag on it.
            IsAiLocked = aiOn && cloudish && !entitled;
            IsAiLive = aiOn && !IsAiLocked;

            AiPillText = !aiOn
                ? CompanionLocStaging.Resolve("companion_hero_pill_ai_off")
                : IsAiLocked
                    ? CompanionLocStaging.Resolve("companion_hero_pill_ai_locked")
                    : provider switch
                    {
                        AiProviderType.Local => Localization.Loc.Get("label_ai_status_pill_local"),
                        AiProviderType.OpenAiCompatible => Localization.Loc.Get("label_ai_status_pill_custom"),
                        _ => CompanionLocStaging.Resolve("companion_hero_pill_ai_cloud")
                    };

            IsAwarenessOpen = settings?.AwarenessModeEnabled == true;
            AwarenessPillText = IsAwarenessOpen
                ? CompanionLocStaging.Resolve("companion_hero_pill_eyes_broad")
                : CompanionLocStaging.Resolve("companion_hero_pill_eyes_closed");
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        private static string SafeShortcut()
        {
            try { return AvatarTubeWindow.FormatChatShortcut(); }
            catch (Exception) { return string.Empty; }
        }

        /// <summary>
        /// The old <c>RefreshHeroAvatar</c>, moved whole: the active avatar set's pose-1 bitmap,
        /// resolved through the mod resolver first so a themed mod's bust wins.
        /// </summary>
        private static ImageSource? LoadPortrait()
        {
            try
            {
                var setNumber = App.Settings?.Current?.SelectedAvatarSet ?? 1;
                if (setNumber < 1)
                {
                    var playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
                    setNumber = AvatarTubeWindow.GetAvatarSetForLevel(playerLevel);
                }
                var prefix = setNumber == 1 ? "avatar_pose" : $"avatar{setNumber}_pose";
                var resourceName = $"{prefix}1.png";

                var resolved = Services.ModResourceResolver.ResolveImage(resourceName);
                if (resolved != null) return resolved;

                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri($"pack://application:,,,/Resources/{resourceName}", UriKind.Absolute);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Companion hero: portrait load failed");
                return null;
            }
        }
    }

    /// <summary>
    /// Z1's bottom band. Train 4 owns the stages; until then the zone renders its designed dormant
    /// state — names visible, nodes outlined, the promise copy underneath and no lock icon, because
    /// this was never a paywall.
    /// </summary>
    internal sealed class RelationshipConstellationRuntimeVm : CompanionObservable, IRelationshipConstellationVm
    {
        public RelationshipConstellationRuntimeVm()
        {
            var modId = SafeModId();
            var nodes = new List<IConstellationNodeVm>(ConstellationMath.StageCount);
            for (int i = 0; i < ConstellationMath.StageCount; i++)
            {
                nodes.Add(new CompanionConstellationNode
                {
                    Index = i,
                    Name = ResolveStage(i, modId),
                    Glyph = "✧",
                    Description = CompanionLocStaging.Resolve($"companion_stage_{i}_blurb"),
                    State = ConstellationMath.StateFor(i, 0, isLive: false)
                });
            }
            Nodes = nodes;
            NodeCommand = CompanionRelayCommand.NoOp("constellation.node");
        }

        public bool IsLive => false;
        public int CurrentStage => 0;
        public IReadOnlyList<IConstellationNodeVm> Nodes { get; }
        public string FlavorLine => CompanionLocStaging.Resolve("companion_constellation_flavor_new");
        public string FlavorAccent => CompanionLocStaging.Resolve("companion_constellation_flavor_new_accent");
        public string DormantCopy => CompanionLocStaging.Resolve("companion_constellation_dormant");
        public ICommand NodeCommand { get; }

        /// <summary>A mod may reflavor a stage name; the base key is the fallback.</summary>
        private static string ResolveStage(int index, string? modId)
        {
            if (!string.IsNullOrEmpty(modId))
            {
                var modKey = ConstellationMath.StageKey(index, modId);
                var modName = CompanionLocStaging.Resolve(modKey);
                if (!string.Equals(modName, modKey, StringComparison.Ordinal)) return modName;
            }
            return CompanionLocStaging.Resolve(ConstellationMath.StageKey(index));
        }

        private static string? SafeModId()
        {
            try { return App.Mods?.ActiveModId; }
            catch (Exception) { return null; }
        }
    }
}
