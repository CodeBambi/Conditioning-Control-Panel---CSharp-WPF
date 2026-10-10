// Global using directives
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;
global using System.Windows;

// Rules that live once in CCP.Core and are shared with the Avalonia head. Core keeps them in
// ConditioningControlPanel.Nav / .Services.Billboard; these per-type aliases let the WPF files
// keep their short names without importing the whole Core namespace (which would collide with
// WPF's own Controls.NavRail.NavStripRules / NavRailRules and Services.UI.NavBadges).
global using NavTabKind = ConditioningControlPanel.Nav.NavTabKind;
global using NavTab = ConditioningControlPanel.Nav.NavTab;
global using NavSection = ConditioningControlPanel.Nav.NavSection;
global using NavSections = ConditioningControlPanel.Nav.NavSections;
global using WindowFitRule = ConditioningControlPanel.Nav.WindowFitRule;
global using DashboardBillboard = ConditioningControlPanel.Services.Billboard.DashboardBillboard;
global using DeckCard = ConditioningControlPanel.Services.Billboard.DeckCard;
global using BillboardDeck = ConditioningControlPanel.Services.Billboard.BillboardDeck;
