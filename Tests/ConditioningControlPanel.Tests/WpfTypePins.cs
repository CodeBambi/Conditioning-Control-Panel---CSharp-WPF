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
