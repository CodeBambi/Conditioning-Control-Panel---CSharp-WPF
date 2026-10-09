using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Z8 — the Workshop drawer. See the XAML header for the visual spec.
    /// </summary>
    public partial class WorkshopAccordion : UserControl
    {
        public WorkshopAccordion()
        {
            // InitializeComponent(), not AvaloniaXamlLoader.Load(this): the generated method loads
            // the XAML AND assigns the x:Name fields. Load() alone leaves Drawer and CellHost null,
            // which compiles and then NREs the first time ExpandAndReveal runs.
            InitializeComponent();

            // Composition, the thing that was missing: WPF builds the six cells in
            // CompanionRoomRuntimeVm and hands them to this drawer. That runtime layer hangs off
            // ICompanionRoomVm and is still in the head, so — like every other zone on this page —
            // the drawer seeds itself instead. Without this the six finished cells in
            // Runtime/ are constructed by nothing and the drawer opens empty.
            var vm = new WorkshopRuntimeVm(ExpandAndReveal);
            DataContext = vm;
            WireCellHostActions(vm.Parts);
        }

        /// <summary>
        /// The cells hoist their outbound actions as events for a host to wire — the port's
        /// replacement for WPF's <c>Window.GetWindow(this) is MainWindow mw</c> forward. This is
        /// that host. Only the actions whose targets exist on this head are connected; the rest
        /// stay unsubscribed on purpose rather than being faked (see the note below).
        /// </summary>
        private void WireCellHostActions(WorkshopShelfParts parts)
        {
            parts.Behavior.ChatShortcutRequested += async (_, _) => await RebindChatShortcutAsync();

            // WPF MainWindow.CompanionTab.cs CompanionCard_Click: switch the companion, repaint the
            // five cards, then move the tube to that companion's look.
            parts.Roster.CompanionCardClicked += (_, index) => SwitchCompanionFromCard(parts.Roster, index);

            // NOT wired, and each control is HIDDEN in its cell so nothing visible is dead:
            //
            //  · Behavior.CameraShortcutRequested: no webcam engine or hotkey on this head.
            //  · Behavior.PauseBrowserChanged: the port's browser has no mute or suspend hook.
            //  · Roster.PersonalityAssignRequested and Community.Browse/Import/Export/Refresh:
            //    CommunityPromptService is still in the WPF head.
        }

        internal static void SwitchCompanionFromCard(WorkshopRosterCell roster, int index)
        {
            try
            {
                if (index < 0 || index > 4) return;
                var id = (Models.CompanionId)index;
                if (CoreMods.Service?.IsCompanionSupported(id) == false) return;
                if (!Services.Companion.CompanionCore.SwitchCompanion(id)) return;
                roster.Refresh();
                AvatarTube.AvatarTubeWindow.Live?.SwitchToCompanionAvatar(id);
                Log.Information("Switched to companion: {Name}", Models.CompanionDefinition.GetById(id).Name);
            }
            catch (Exception ex) { Log.Warning(ex, "Workshop: companion switch failed"); }
        }

        /// <summary>
        /// Port of WPF's MainWindow.SessionIO.cs:1202 <c>BtnChatShortcut_Click</c>, reached here
        /// from the Behavior cell's shortcut pill. Identical to the twin already restored on
        /// DevicesSettingsSection — the same settings write, the same two re-applications — because
        /// WPF's one handler serves both surfaces and repaints both labels
        /// (MainWindow.SessionIO.cs:1326/1332).
        ///
        /// <para>The system-wide half of the toggle (GlobalHotkeyService, a Win32 RegisterHotKey)
        /// stays in the WPF head; the flag is still stored, since it is one settings file across
        /// both heads, and the in-window binding applied here is exactly what WPF falls back to
        /// when the flag is off.</para>
        /// </summary>
        private async System.Threading.Tasks.Task RebindChatShortcutAsync()
        {
            try
            {
                // Avalonia throws on a non-visible owner, and the shell can be loaded-but-hidden in
                // the tray. No visible window means no modal to show and nothing to capture.
                if (TopLevel.GetTopLevel(this) is not Window owner || !owner.IsVisible) return;
                var prompt = CoreSettings.Current.CompanionPrompt;
                if (prompt == null) return;

                var dlg = new Dialogs.ChatShortcutCaptureDialog { GlobalHotkey = prompt.ChatShortcutGlobal };
                if (!await dlg.ShowDialogSafe<bool>(owner)) return;

                if (dlg.ResetToDefault)
                {
                    prompt.ChatShortcutKey = "T";
                    prompt.ChatShortcutModifiers = "Control";
                }
                else
                {
                    prompt.ChatShortcutKey = dlg.CapturedKey.ToString();
                    // Serialises "Windows", not Avalonia's "Meta", so a file written here still
                    // parses on the WPF head.
                    prompt.ChatShortcutModifiers =
                        AvatarTube.AvatarTubeWindow.SerializeModifiers(dlg.CapturedModifiers);
                }
                prompt.ChatShortcutGlobal = dlg.GlobalHotkey;
                CoreSettings.Save();

                AvatarTube.AvatarTubeWindow.ApplyChatShortcutTo(owner);
                AvatarTube.AvatarTubeWindow.ApplyChatShortcutTo(AvatarTube.AvatarTubeWindow.Live);
                (DataContext as WorkshopRuntimeVm)?.Parts.Behavior.RefreshChatShortcutLabel();
                Log.Information("Chat shortcut rebound to {Combo}",
                                AvatarTube.AvatarTubeWindow.FormatChatShortcut());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Workshop: chat shortcut rebind failed");
            }
        }

        /// <summary>
        /// The view's own strings. Separate from <see cref="DataContext"/> on purpose: the drawer
        /// header and the cell-heading tooltip are the view's chrome, not the host viewmodel's
        /// data, and the WPF original reads both straight from {loc:Str}.
        /// </summary>
        public WorkshopAccordionViewModel Strings { get; } = new();

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public IWorkshopAccordionVm? ViewModel
        {
            get => DataContext as IWorkshopAccordionVm;
            set => DataContext = value;
        }

        /// <summary>
        /// Opens the drawer and scrolls it into view — what the hero's Switch chip and Z5's
        /// "fine-tuning ↓" link both call.
        ///
        /// <para>Deferred one dispatcher turn at <see cref="DispatcherPriority.Normal"/> so the
        /// body is measured before the scroll, and never at Loaded priority (starved here).</para>
        /// </summary>
        public void ExpandAndReveal() => ExpandAndReveal(null);

        /// <summary>
        /// The same deep link, but landing on a named pigeonhole: the hero's Switch chip asks for
        /// the roster, Z5's fine-tuning link asks for the awareness cell.
        ///
        /// <para><paramref name="cellTitle"/> is matched against <see cref="IWorkshopCellVm.Key"/> —
        /// the anchor, not the heading. Before the wiring pass split the two, a localized Workshop
        /// heading would have quietly broken every deep link on the page. An unknown key is not an
        /// error: the drawer still opens and the caller gets the drawer's own scroll, which is the
        /// useful half of the job.</para>
        ///
        /// <para>The WPF original bails when <c>Dispatcher.HasShutdownStarted</c>. Avalonia's
        /// Dispatcher exposes no such flag (only a ShutdownStarted event), so the guard is gone:
        /// a Post onto a shut-down dispatcher simply never runs, which is the outcome the guard
        /// was buying.</para>
        /// </summary>
        public void ExpandAndReveal(string? cellTitle)
        {
            var vm = ViewModel;
            if (vm != null) vm.IsExpanded = true;
            else Drawer.IsExpanded = true;

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var target = FindCellContainer(cellTitle);
                    if (target != null) target.BringIntoView();
                    else this.BringIntoView();
                }
                catch (InvalidOperationException) { /* torn down mid-scroll */ }
            }, DispatcherPriority.Normal);
        }

        /// <summary>
        /// Resolves the container for a cell anchor key, or null when there is no match (or the
        /// containers have not been generated because the drawer is still collapsed).
        /// </summary>
        private Control? FindCellContainer(string? cellKey)
        {
            if (string.IsNullOrWhiteSpace(cellKey)) return null;

            var cells = ViewModel?.Cells;
            if (cells == null || cells.Count == 0) return null;

            // The Expander only realises its body when expanded; force the pass so the generator
            // has containers to hand back on this same dispatcher turn.
            CellHost.UpdateLayout();

            foreach (var cell in cells)
            {
                if (cell == null) continue;
                if (!string.Equals(cell.Key, cellKey, StringComparison.OrdinalIgnoreCase)) continue;
                return CellHost.ContainerFromItem(cell);
            }
            return null;
        }
    }

    /// <summary>Strings from CCP.Core's Loc. See the porting notes in the repo-root CLAUDE.md
    /// for why {loc:Str} becomes a binding.</summary>
    public sealed class WorkshopAccordionViewModel
    {
        public string LocHeader => Loc.Get("companion_workshop_header");
        public string LocFocusTip => Loc.Get("companion_workshop_focus_tip");
    }
}
