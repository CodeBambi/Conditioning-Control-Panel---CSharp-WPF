// STILL A STUB from ConditioningControlPanel/MainWindow/MainWindow.Possession.cs (673 lines) - the
// window as a haunted room: two overlay canvases, the registry of controls that opted in via
// poss:Possession.Role, and the walk that turns the visual tree into possession targets.
//
// Head-side, and not for want of a seam: the CONTRACTS THEMSELVES ARE WPF TYPES.
// ConditioningControlPanel/Services/Possession/PossessionContracts.cs declares IPossessionHost,
// PossessionTarget and PossessionRole against FrameworkElement, and this partial implements
// IPossessionHost - a Core-side twin would have to be re-declared against Avalonia's Control
// before any of this walk could be ported. Also missing: App.Possession (the director),
// Services/Possession/PossessionPointer.Attach, and the attached property the opt-in reads.
//
// The walk itself is the portable half, and it is large: PassesSizeAndPlacement, InferLeafRole,
// IsInsideScrollBar / IsInteractiveRole / IsCardBorder and DisplayNameFor are pure geometry and
// naming rules over a visual tree, and Avalonia has both a visual tree and TranslatePoint. They
// belong in the same layer that re-declares the contracts, not before it.
//
// Members still absent (31), unchanged from the WPF file: the two canvases and their hook flag,
// the ConditionalWeakTable of targets, the cache quartet (_possessionTargetCache, _possessionCacheAt,
// _possessionCacheDirty, _possessionLayoutSeenAt) and its floors, PossessionNeverNames,
// MaxAutoLabels, MinTargetPx, InitializePossessionHost, EnsurePossessionLayers,
// HookPossessionInvalidation, GetPossessionTargets, TrimLabels, PossessionSubtree, WalkPossession,
// TargetFor, InferLeafRole, IsInsideScrollBar, IsInteractiveRole, IsCardBorder,
// PassesSizeAndPlacement, TryWindowBounds, DisplayNameFor, Tidy, FallbackDisplayName.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
