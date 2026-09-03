// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ToolTipHygiene.cs (247 lines), and
// the port is one tenth the size because the hazard is the same and the framework is not.
//
// THE PROBLEM, unchanged: a tooltip is dismissed when the pointer leaves its owner, and nothing
// else. This app is navigated by clicks, by code (~25 ShowTab call sites) and, in the automated
// play-test harness, by accessibility automation - none of which move the cursor. So a tooltip
// opened by a stationary pointer outlives the tab it belongs to and hangs over the NEXT tab until
// the user physically twitches the mouse. It looks like a rendering bug and it shows up in every
// screenshot sweep.
//
// WHY THE WPF FILE IS 247 LINES AND THIS IS NOT. WPF's ToolTip is a Popup in its own HWND with its
// own PresentationSource, and ToolTipService.GetToolTip hands back either a ToolTip or raw content
// WPF wrapped in one - so the original had to sweep every PresentationSource on the thread to find
// the live popup whatever shape it was declared in, then synthesize a MouseLeave and a
// Mouse.Synchronize so PopupControlService dropped its own reference. None of that exists here:
//   ToolTipService.ToolTipOpening/Closing + a WeakReference owner
//        -> dropped. Avalonia records the open state ON THE OWNER, as the ToolTip.IsOpen attached
//           property, so the owner needs no tracking - it can be read back off the tree.
//   SweepOpenToolTips / FindToolTips over PresentationSource.CurrentSources
//        -> the pointer-over descent below. Avalonia opens at most one tooltip at a time and only
//           over a control the pointer is on, so the owner is always ON the hover chain: descend
//           from the window through children whose IsPointerOver is set and the first one with
//           ToolTip.IsOpen is it. That is a walk of the tree's DEPTH, not its size, so it is cheap
//           enough to run on every tab switch - which is what the WPF version's two bounded-sweep
//           constants (ToolTipSweepMaxDepth / ToolTipSweepMaxVisuals) were buying with a cap.
//   CloseToolTipOn / SyntheticMouseLeave / Mouse.Synchronize
//        -> ToolTip.SetIsOpen(owner, false). Avalonia's tooltip service reads the same property,
//           so there is no second bookkeeping copy to talk out of its belief.
//
// NOT WIRED BY THIS LAYER: the caller. WPF closes the stale tooltip from inside ShowTab, which is
// MainShellWindow.TabNavigation.cs - one line (`CloseStaleToolTip();` beside SwitchTabFx), in a
// file this layer does not own.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>How far down the hover chain to look before giving up. The shell nests deeply
        /// (rail -> door -> flyout -> row -> glyph), so this is generous where WPF's popup-root cap
        /// was 6 - but it is still a cap, so a cycle or a pathological tree cannot spin here.</summary>
        private const int ToolTipHoverMaxDepth = 64;

        /// <summary>
        /// Closes whatever tooltip is up. Never throws: this is meant to run inside ShowTab, and a
        /// cosmetic tidy-up must not be able to break navigation.
        /// </summary>
        internal void CloseStaleToolTip()
        {
            try
            {
                var owner = FindOpenToolTipOwner(this);
                if (owner is not null) ToolTip.SetIsOpen(owner, false);
            }
            catch (Exception ex) { Log.Debug("CloseStaleToolTip: {E}", ex.Message); }
        }

        /// <summary>
        /// The control whose tooltip is open, found by following the pointer-over chain down from
        /// <paramref name="root"/>. Null when nothing is up, which is the common case.
        ///
        /// <para>The chain is what makes this correct rather than merely cheap: a tooltip only ever
        /// opens over a control the pointer is on, and a code-driven tab switch moves no pointer, so
        /// the stale tooltip's owner is still flagged IsPointerOver when we come looking.</para>
        /// </summary>
        private static Control? FindOpenToolTipOwner(Visual root)
        {
            Visual? node = root;
            for (var depth = 0; node is not null && depth < ToolTipHoverMaxDepth; depth++)
            {
                if (node is Control control && ToolTip.GetIsOpen(control)) return control;
                node = node.GetVisualChildren()
                           .FirstOrDefault(v => v is InputElement { IsPointerOver: true });
            }
            return null;
        }
    }
}
