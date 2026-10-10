// Types the WPF app keeps its OWN implementation of while CCP.Core carries a different one under
// the same name (the windowless half, a probe-only stub, or the port's stricter variant). The WPF
// suite pins the WPF one, as GoonHostRulesTests does for the Goon host.
extern alias wpf;
global using DescentMigration = wpf::ConditioningControlPanel.Services.Descent.DescentMigration;
global using EmiVox = wpf::ConditioningControlPanel.Services.EmiDesk.EmiVox;
global using ArcademyHostService = wpf::ConditioningControlPanel.Services.Arcademy.ArcademyHostService;
global using AnimatedWebp = wpf::ConditioningControlPanel.Services.AnimatedWebp;
global using CornerGifMedia = wpf::ConditioningControlPanel.Services.CornerGifMedia;
global using MergedAccountRecovery = wpf::ConditioningControlPanel.Services.MergedAccountRecovery;
global using MergedAccountResponse = wpf::ConditioningControlPanel.Services.MergedAccountResponse;
global using MergedSwapDecision = wpf::ConditioningControlPanel.Services.MergedSwapDecision;
global using MergedSwapPolicy = wpf::ConditioningControlPanel.Services.MergedSwapPolicy;
global using LauncherShortcuts = wpf::ConditioningControlPanel.Services.Launcher.LauncherShortcuts;
global using GoonShareCard = wpf::ConditioningControlPanel.Services.GoonGame.GoonShareCard;
global using FirstShowLayout = wpf::ConditioningControlPanel.Services.FirstShow.FirstShowLayout;
// Possession: Core carries the port contracts (platform-neutral targets, a host class); the WPF haunt keeps its own.
global using PossessionRung = wpf::ConditioningControlPanel.Services.Possession.PossessionRung;
global using PossessionIntensity = wpf::ConditioningControlPanel.Services.Possession.PossessionIntensity;
global using PossessionRole = wpf::ConditioningControlPanel.Services.Possession.PossessionRole;
global using PossessionTarget = wpf::ConditioningControlPanel.Services.Possession.PossessionTarget;
global using PossessionContext = wpf::ConditioningControlPanel.Services.Possession.PossessionContext;
global using PossessionTargetMeta = wpf::ConditioningControlPanel.Services.Possession.PossessionTargetMeta;
global using PossessionEffectMeta = wpf::ConditioningControlPanel.Services.Possession.PossessionEffectMeta;
global using PossessionDeck = wpf::ConditioningControlPanel.Services.Possession.PossessionDeck;
global using IPossessionEffect = wpf::ConditioningControlPanel.Services.Possession.IPossessionEffect;
global using PossessionBarkTriggers = wpf::ConditioningControlPanel.Services.Possession.PossessionBarkTriggers;
// EMI desk machines: the Core copies read the head through probes, the WPF ones read App directly.
global using EmiKnockMachine = wpf::ConditioningControlPanel.Services.EmiDesk.EmiKnockMachine;
global using IEmiKnockWorld = wpf::ConditioningControlPanel.Services.EmiDesk.IEmiKnockWorld;
global using EmiNudgeMachine = wpf::ConditioningControlPanel.Services.EmiDesk.EmiNudgeMachine;
global using IEmiNudgeWorld = wpf::ConditioningControlPanel.Services.EmiDesk.IEmiNudgeWorld;
global using EmiLineEngine = wpf::ConditioningControlPanel.Services.EmiDesk.EmiLineEngine;
global using PossessionDirector = wpf::ConditioningControlPanel.Services.Possession.PossessionDirector;
global using PossessionOffLimits = wpf::ConditioningControlPanel.Services.Possession.PossessionOffLimits;
global using EmiKnockPopulation = wpf::ConditioningControlPanel.Services.EmiDesk.EmiKnockPopulation;
global using EmiOffers = wpf::ConditioningControlPanel.Services.EmiDesk.EmiOffers;
// Haptics, Fyp menu, Remote name, Showcase cache and provider, Sparkle awards, What Moved: WPF keeps its App-reading copy.
global using DtrhHapticDirector = wpf::ConditioningControlPanel.Services.Haptics.DtrhHapticDirector;
global using LockdownDoseKeeper = wpf::ConditioningControlPanel.Services.Haptics.LockdownDoseKeeper;
global using FypFileMenu = wpf::ConditioningControlPanel.Services.Fyp.FypFileMenu;
global using RemoteControllerName = wpf::ConditioningControlPanel.Services.Remote.RemoteControllerName;
global using ShowcaseCache = wpf::ConditioningControlPanel.Services.Billboard.Showcase.ShowcaseCache;
global using ShowcaseClipArt = wpf::ConditioningControlPanel.Services.Billboard.Showcase.ShowcaseClipArt;
global using ShowcaseProvider = wpf::ConditioningControlPanel.Services.Billboard.Showcase.ShowcaseProvider;
global using SparklePointAward = wpf::ConditioningControlPanel.Services.SparklePointAward;
global using SparklePointRewards = wpf::ConditioningControlPanel.Services.SparklePointRewards;
global using SparklePointSource = wpf::ConditioningControlPanel.Services.SparklePointSource;
