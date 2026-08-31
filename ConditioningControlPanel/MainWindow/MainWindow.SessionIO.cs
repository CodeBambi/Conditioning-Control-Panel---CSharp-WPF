using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Session import/export: serialize and load session configurations.
    public partial class MainWindow
    {
        #region Session Import/Export

        private Services.SessionManager? _sessionManager;
        private Services.SessionFileService? _sessionFileService;
        private Services.AssetImportService? _assetImportService;

        private void InitializeSessionManager()
        {
            _sessionFileService = new Services.SessionFileService();
            _sessionManager = new Services.SessionManager();
            _sessionManager.SessionAdded += OnSessionAdded;
            _sessionManager.SessionRemoved += OnSessionRemoved;
            // SessionsReloaded had no subscriber before the rack: the list was rebuilt by hand at
            // each of the two LoadAllSessions call sites, which is exactly how a third one would
            // have shipped without a list refresh. One repaint, one event.
            _sessionManager.SessionsReloaded += OnSessionsReloaded;
            _sessionManager.LoadAllSessions();

            // ONE list, every source. Built-in, custom and imported rows all come out of the
            // registry now - see RepaintSessionRack.
            RepaintSessionRack();
        }

        private void OnSessionsReloaded()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(OnSessionsReloaded));
                return;
            }
            RepaintSessionRack();
        }

        /// <summary>
        /// Register a session that was written to disk by something OTHER than the session
        /// editor with the LIVE <see cref="Services.SessionManager"/>, so the Sessions tab
        /// lists it immediately.
        ///
        /// BUG #614: the Graded Intake drafts a session and auto-saves it straight into the
        /// CustomSessions folder (see IntakeHostService.OnQuizResult - deliberately no
        /// SaveFileDialog). Nothing told the running SessionManager about the new file, and
        /// <see cref="Services.SessionManager.LoadAllSessions"/> only ever runs once, from
        /// <see cref="InitializeSessionManager"/> at startup. So the session existed on disk
        /// but was invisible in the UI until the next app launch - which read, to the user,
        /// as "the intake didn't create a session at all".
        ///
        /// This reuses the existing AddNewSession path (the one the editor's "save as new"
        /// uses) rather than inventing a parallel one: it sets Source/SourceFilePath, resolves
        /// an Id collision, re-writes the file at <paramref name="filePath"/> (idempotent - the
        /// caller already wrote identical bytes there) and raises SessionAdded, which lands in
        /// <see cref="OnSessionAdded"/> and builds the card + selects it.
        ///
        /// Safe to call before the manager exists (it is created on demand) and safe to call
        /// off the UI thread. It NEVER throws at the caller: the file is already safely on disk
        /// by the time we get here, and a failure to refresh a list must not be reported as a
        /// failure to draft the session.
        /// </summary>
        public bool RegisterExternallySavedSession(Models.Session session, string filePath)
        {
            if (session == null || string.IsNullOrWhiteSpace(filePath)) return false;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => RegisterExternallySavedSession(session, filePath)));
                return true;
            }

            try
            {
                if (_sessionManager == null) InitializeSessionManager();
                if (_sessionManager == null) return false;

                // Already known (e.g. a LoadAllSessions raced us, or a double-register)?
                // Adding twice would leave two identical cards in the Sessions tab.
                //
                // But KNOWN-TO-THE-MANAGER IS NOT VISIBLE-IN-THE-TAB. If the card build ever
                // failed (AddNewSession registers the session BEFORE it raises SessionAdded),
                // this branch would keep reporting success forever on a list nobody can see -
                // #614 all over again. Reconcile the cards instead of trusting the registry.
                if (_sessionManager.GetSession(session.Id) != null)
                {
                    if (!HasSessionRackRow(session.Id)) RepaintSessionRack();
                    return true;
                }

                _sessionManager.AddNewSession(session, filePath);

                // AddNewSession -> SessionAdded -> OnSessionAdded -> RepaintSessionRack is the
                // only thing between the file and the tab, and the caller swallows anything that
                // goes wrong in it. Confirm the row landed; repaint from the manager if not.
                //
                // A row can also be missing because a FILTER is hiding it (a drafted session
                // while the rack is showing "Built-in", say). That is not the #614 failure and a
                // repaint would not fix it either - the row is genuinely not meant to be on
                // screen - but repainting is cheap and idempotent, so this does not try to tell
                // the two cases apart.
                if (!HasSessionRackRow(session.Id)) RepaintSessionRack();
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RegisterExternallySavedSession failed for '{Name}'", session.Name);
                return false;
            }
        }

        private void OnSessionAdded(Models.Session session)
        {
            Dispatcher.Invoke(() =>
            {
                App.Logger?.Information("Session imported: {Name}", session.Name);

                // Selection BEFORE the repaint, not after: the repaint decides which row wears
                // SdSessionRowSelected and moves the ambient sheen onto it. Selecting afterwards
                // would leave the new row painted as an ordinary one until the next rebuild.
                SelectSession(session);
                RepaintSessionRack();

                ShowDropZoneStatus($"Session loaded: {session.Name}", isError: false);
            });
        }

        private void OnSessionRemoved(Models.Session session)
        {
            Dispatcher.Invoke(() =>
            {
                App.Logger?.Information("Session removed: {Name}", session.Name);
                RepaintSessionRack();
            });
        }

        // ============================== THE SESSION RACK ==============================
        //
        // One list for every session the app knows about - built-in, custom, imported - built
        // here rather than in XAML, 44px per row, sorted/filtered/searched by the toolbar above
        // it. Replaces the four hard-coded XAML cards plus the separate "Your Sessions" panel
        // that AddCustomSessionCard used to fill (Session Rack, 2026-08-16).
        //
        // The whole surface is ONE repaint. RepaintSessionRack clears SessionRackPanel and
        // rebuilds it from the registry every time anything changes - a session added, removed,
        // imported, renamed, a toolbar control touched, a mod switched. ~100 lightweight rows is
        // well inside a frame, and a single rebuild path is what stops the list and the registry
        // drifting apart the way the two-panel split did.
        //
        // NOT MVVM, deliberately: this page is code-built UI in MainWindow partials and an
        // ItemsControl here would need a view-model layer that nothing else on the page has.

        /// <summary>Source filter token. Matches the chip Tags and AppSettings.SessionRackSourceFilter.</summary>
        private const string RackSourceAll = "all";
        private const string RackSourceBuiltIn = "builtin";
        private const string RackSourceYours = "yours";
        private const string RackSourceCatalogue = "catalogue";

        private string _rackSourceFilter = RackSourceAll;
        private string _rackSort = "recent";
        private string _rackSearch = "";
        private readonly HashSet<Models.SessionDifficulty> _rackDifficulties = new()
        {
            Models.SessionDifficulty.Easy,
            Models.SessionDifficulty.Medium,
            Models.SessionDifficulty.Hard,
            Models.SessionDifficulty.Extreme
        };

        private readonly List<ToggleButton> _rackSourceChips = new();
        private readonly List<ToggleButton> _rackDifficultyChips = new();
        private bool _rackToolbarBuilt;

        /// <summary>Set while code is writing the toolbar controls, so their own change events
        /// do not read a half-applied state back out and repaint against it.</summary>
        private bool _rackToolbarSyncing;

        /// <summary>
        /// Rebuild the whole session list against the current filter + sort + search.
        ///
        /// <para>Safe to call from anywhere and as often as you like: it is idempotent, it raises
        /// no events, and it never throws at the caller - a list that failed to repaint must not
        /// take down the import/draft/delete that asked for it.</para>
        /// </summary>
        internal void RepaintSessionRack()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RepaintSessionRack));
                return;
            }

            try
            {
                var panel = PresetsTab?.SessionRackPanel;
                if (panel == null) return;   // the tab has not been built yet

                EnsureSessionRackToolbar();

                var all = EnumerateRackSessions();
                var shown = SortRackSessions(all.Where(RackAccepts).ToList(), all);

                panel.Children.Clear();
                foreach (var session in shown)
                    panel.Children.Add(BuildSessionRackRow(session));

                if (shown.Count == 0)
                {
                    var empty = new TextBlock
                    {
                        Text = LocOr("rack_empty", "No sessions match - clear a filter.")
                    };
                    var emptyStyle = TryFindTabStyle("SdRackEmpty");
                    if (emptyStyle != null) empty.Style = emptyStyle;
                    else
                    {
                        empty.Foreground = Application.Current.Resources["TextDimBrush"] as Brush;
                        empty.FontSize = 12;
                        empty.HorizontalAlignment = HorizontalAlignment.Center;
                        empty.Margin = new Thickness(0, 26, 0, 0);
                    }
                    // A TextBlock, not a Border: the FX sweep and the selected-card lookup in
                    // MainWindow.TabFxPresetsQuestsAchievements walk Borders only, so the empty
                    // line cannot be staggered in as a row or mistaken for one.
                    panel.Children.Add(empty);
                }

                UpdateRackToolbarCounts(all, shown.Count);

                // Every row is a NEW object, so the ambient sheen is parked on a Border that is no
                // longer in the tree.
                RefreshCardSheen();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "RepaintSessionRack failed"); }
        }

        /// <summary>The registry, in registry order. Falls back to the hard-coded set for the
        /// same reason <see cref="GetSessionById"/> does - the manager is created lazily and a
        /// repaint can beat it.</summary>
        private List<Models.Session> EnumerateRackSessions()
        {
            if (_sessionManager != null && _sessionManager.AllSessions.Count > 0)
                return _sessionManager.AllSessions.ToList();
            return Models.Session.GetAllSessions();
        }

        private bool RackAccepts(Models.Session session)
        {
            switch (_rackSourceFilter)
            {
                case RackSourceBuiltIn:
                    if (session.Source != Models.SessionSource.BuiltIn) return false;
                    break;
                case RackSourceYours:
                    if (session.Source != Models.SessionSource.Custom) return false;
                    break;
                case RackSourceCatalogue:
                    if (session.Source != Models.SessionSource.Imported) return false;
                    break;
            }

            if (!_rackDifficulties.Contains(session.Difficulty)) return false;

            if (_rackSearch.Length > 0)
            {
                // Mode-aware text, because that is what is ON the row: searching "bambi" on a
                // drone build should find nothing rather than find rows that do not say it.
                var name = session.GetModeAwareName() ?? "";
                var desc = session.GetModeAwareDescription() ?? "";
                if (name.IndexOf(_rackSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                    desc.IndexOf(_rackSearch, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Order the visible rows. <paramref name="registryOrder"/> is the unfiltered list and is
        /// the tie-breaker for every mode - without it a sort on a field half the sessions share
        /// (three 45-minute Easy runs) would shuffle on each repaint.
        /// </summary>
        private List<Models.Session> SortRackSessions(List<Models.Session> rows, List<Models.Session> registryOrder)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < registryOrder.Count; i++)
                index[registryOrder[i].Id ?? ""] = i;

            int Idx(Models.Session s) => index.TryGetValue(s.Id ?? "", out var i) ? i : int.MaxValue;

            switch (_rackSort)
            {
                case "name":
                    return rows.OrderBy(s => s.GetModeAwareName() ?? "", StringComparer.OrdinalIgnoreCase)
                               .ThenBy(Idx).ToList();
                case "easiest":
                    return rows.OrderBy(s => (int)s.Difficulty)
                               .ThenBy(s => s.DurationMinutes)
                               .ThenBy(Idx).ToList();
                case "hardest":
                    return rows.OrderByDescending(s => (int)s.Difficulty)
                               .ThenByDescending(s => s.DurationMinutes)
                               .ThenBy(Idx).ToList();
                case "shortest":
                    return rows.OrderBy(s => s.DurationMinutes).ThenBy(Idx).ToList();
                case "xp":
                    return rows.OrderByDescending(s => s.BonusXP).ThenBy(Idx).ToList();
                default:
                {
                    // RECENT. Newest file first; anything without a real file on disk (the
                    // hard-coded built-ins, which only exist when assets/sessions is missing)
                    // sinks to the bottom in registry order rather than pretending to a date.
                    // The stamps are read ONCE - a Comparer that hits the filesystem per
                    // comparison would stat the same file O(n log n) times.
                    var stamps = new Dictionary<string, DateTime?>(StringComparer.Ordinal);
                    foreach (var s in rows) stamps[s.Id ?? ""] = RackFileStamp(s);

                    DateTime? Stamp(Models.Session s) => stamps.TryGetValue(s.Id ?? "", out var t) ? t : null;

                    return rows.OrderByDescending(s => Stamp(s).HasValue)
                               .ThenByDescending(s => Stamp(s) ?? DateTime.MinValue)
                               .ThenBy(Idx).ToList();
                }
            }
        }

        /// <summary>Last-write time of a session's backing file, or null when it has none (or the
        /// path is stale/unreadable). Never throws: a locked or vanished file must degrade to
        /// "undated", not break the sort.</summary>
        private static DateTime? RackFileStamp(Models.Session session)
        {
            try
            {
                var path = session.SourceFilePath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                return File.GetLastWriteTime(path);
            }
            catch { return null; }
        }

        // ------------------------------ one row ------------------------------

        /// <summary>
        /// One 44px rack row.
        ///
        /// <para><b>Tag is the session id and that is contractual</b> - SessionCard_Click,
        /// FindSelectedCard (the ambient sheen), StaggerPresetCards and RevealSessionInLibrary all
        /// match on it.</para>
        /// </summary>
        private Border BuildSessionRackRow(Models.Session session)
        {
            var isSelected = _selectedSession?.Id == session.Id;

            var border = new Border { Tag = session.Id };
            var rowStyle = TryFindTabStyle(isSelected ? "SdSessionRowSelected" : "SdSessionRow");
            if (rowStyle != null) border.Style = rowStyle;
            else
            {
                // The tab's dictionary is unreachable (the view was never loaded). Theme brushes,
                // never literals - this page's whole restyle was about not painting itself.
                border.Height = 44;
                border.Background = Application.Current.Resources[isSelected ? "TransparentPink20Brush" : "SurfaceBgBrush"] as Brush;
                border.BorderBrush = Application.Current.Resources[isSelected ? "PinkBrush" : "PanelAccentBrush"] as Brush;
                border.BorderThickness = new Thickness(1);
                border.CornerRadius = new CornerRadius(10);
                border.Padding = new Thickness(0);
                border.Margin = new Thickness(0, 0, 0, 5);
                border.Cursor = Cursors.Hand;
            }

            border.MouseLeftButtonUp += SessionCard_Click;
            PrepareSessionRowFx(border);

            var grid = new Grid();
            // A Border does NOT clip its child to its own CornerRadius (0813 finding), and the
            // difficulty stripe below runs full-bleed into the row's rounded left edge - so the
            // content root carries its own clip, the same RectangleGeometry-on-SizeChanged
            // pattern FeatureCard.OnContentRootSizeChanged uses. Radius 9 = the row's 10 minus
            // its 1px stroke, so the clip lands INSIDE the border rather than eating it.
            AttachRoundedClip(grid, 9);

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // 0 stripe
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // 1 icon
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // 2 name
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // 3 blurb
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // 4 difficulty
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // 5 duration
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // 6 XP
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // 7 badges
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // 8 actions

            var (diffSolid, diffWash) = RackDifficultyKeys(session.Difficulty);

            // 0. The stripe. Full height, full bleed, 4px - the one part of the row you can read
            // at a glance while scrolling.
            var stripe = new Border { Width = 4, VerticalAlignment = VerticalAlignment.Stretch };
            stripe.SetResourceReference(Border.BackgroundProperty, diffSolid);
            Grid.SetColumn(stripe, 0);
            grid.Children.Add(stripe);

            // 1. The session's own glyph.
            var icon = new Helpers.EmojiTextBlock
            {
                Text = string.IsNullOrWhiteSpace(session.Icon) ? "\U0001F3AC" : session.Icon,
                Margin = new Thickness(7, 0, 0, 0)
            };
            var iconStyle = TryFindTabStyle("SdRackIcon");
            if (iconStyle != null) icon.Style = iconStyle;
            else
            {
                icon.Width = 28;
                icon.FontSize = 15;
                icon.TextAlignment = TextAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
            }
            Grid.SetColumn(icon, 1);
            grid.Children.Add(icon);

            // 2. Name. MaxWidth rather than a star column: a long custom name must not push the
            // blurb off the row, and an Auto column will not trim without one.
            var nameText = new Helpers.EmojiTextBlock
            {
                Text = session.GetModeAwareName(),
                MaxWidth = 210,
                Margin = new Thickness(7, 0, 0, 0)
            };
            var nameStyle = TryFindTabStyle("SdRowTitle");
            if (nameStyle != null) nameText.Style = nameStyle;
            else
            {
                nameText.Foreground = Application.Current.Resources["TextLightBrush"] as Brush ?? Brushes.White;
                nameText.FontWeight = FontWeights.SemiBold;
                nameText.FontSize = 13;
                nameText.VerticalAlignment = VerticalAlignment.Center;
                nameText.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            Grid.SetColumn(nameText, 2);
            grid.Children.Add(nameText);

            // 3. The SPOILER-FREE blurb. First line only, ellipsised, full text on the tooltip -
            // this is the authored description, never GenerateFeatureDescription, which is what
            // the Reveal Details gate on the right exists to withhold.
            var blurb = string.IsNullOrWhiteSpace(session.Description)
                ? LocOr("label_custom_session", "Custom session")
                : session.GetModeAwareDescription() ?? "";
            var descText = new TextBlock
            {
                Text = blurb.Split('\n')[0].Trim(),
                ToolTip = string.IsNullOrWhiteSpace(blurb) ? null : blurb
            };
            var descStyle = TryFindTabStyle("SdRowBlurb");
            if (descStyle != null) descText.Style = descStyle;
            else
            {
                descText.Foreground = Application.Current.Resources["TextMutedBrush"] as Brush;
                descText.FontSize = 12;
                descText.Margin = new Thickness(10, 0, 0, 0);
                descText.VerticalAlignment = VerticalAlignment.Center;
                descText.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            Grid.SetColumn(descText, 3);
            grid.Children.Add(descText);

            // 4. Difficulty pill.
            var diffPill = MakeRackPill(session.GetDifficultyText(), diffWash, diffSolid);
            Grid.SetColumn(diffPill, 4);
            grid.Children.Add(diffPill);

            // 5/6. Duration and reward, mono and right-aligned so they form two columns down the
            // list instead of drifting with each row's name length.
            var duration = MakeRackMeta(
                string.Format(LocOr("rack_duration", "{0} min"), session.DurationMinutes), 56);
            Grid.SetColumn(duration, 5);
            grid.Children.Add(duration);

            var xp = MakeRackMeta(
                string.Format(LocOr("rack_xp", "+{0} XP"), session.BonusXP), 66);
            Grid.SetColumn(xp, 6);
            grid.Children.Add(xp);

            // 7. Provenance badge, plus the catalogue share status when this session has been
            // submitted (kept from the old custom rows - RefreshCatalogueShareBadges repaints the
            // rack specifically to update it).
            var badges = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            var (srcLabel, srcSolid, srcWash) = RackSourceKeys(session.Source);
            badges.Children.Add(MakeRackPill(srcLabel, srcWash, srcSolid, "SdRackBadgeText"));

            var sessKey = string.IsNullOrEmpty(session.SourceFilePath) ? null : CanonicalCataloguePathKey(session.SourceFilePath);
            var sessRec = sessKey != null ? GetCatalogueRecord(CatalogueKindSessions, sessKey) : null;
            var statusBadge = CreateCatalogueStatusBadge(sessRec);
            if (statusBadge != null) badges.Children.Add(statusBadge);

            Grid.SetColumn(badges, 7);
            grid.Children.Add(badges);

            // 8. Actions, hidden until the row is hovered (SdRackActions). Exactly the actions the
            // old cards carried: edit + export on everything, share + delete only where they
            // could ever succeed - SessionManager.DeleteSession refuses a built-in outright.
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            var actionsStyle = TryFindTabStyle("SdRackActions");
            if (actionsStyle != null) buttonPanel.Style = actionsStyle;

            buttonPanel.Children.Add(CreateSessionActionButton("✎", Loc.Get("tooltip_edit_session"), session.Id, SessionBtn_Edit));
            buttonPanel.Children.Add(CreateSessionActionButton("↗", Loc.Get("tooltip_export_session"), session.Id, SessionBtn_Export));
            if (session.Source != Models.SessionSource.BuiltIn)
            {
                buttonPanel.Children.Add(CreateSessionActionButton("☁", Loc.Get("tooltip_share_to_catalogue"), session.Id, SessionBtn_Share));
                buttonPanel.Children.Add(CreateSessionDeleteButton("\U0001F5D1", Loc.Get("tooltip_delete_session"), session.Id, SessionBtn_Delete));
            }

            // Pad the LEFT margin out to four buttons' worth (SdRowAction is 28px wide with a 3px
            // margin) so a two-button built-in row reserves the same width as a four-button custom
            // one. Without it the duration / XP / badge columns would step sideways every time the
            // list crossed from one kind of row to the other.
            buttonPanel.Margin = new Thickness(8 + (4 - buttonPanel.Children.Count) * 31, 0, 4, 0);

            Grid.SetColumn(buttonPanel, 8);
            grid.Children.Add(buttonPanel);

            border.Child = grid;
            return border;
        }

        /// <summary>Duration / reward cell. MinWidth is what turns them into columns.</summary>
        private TextBlock MakeRackMeta(string text, double minWidth)
        {
            var block = new TextBlock { Text = text, MinWidth = minWidth };
            var style = TryFindTabStyle("SdRackMeta");
            if (style != null) block.Style = style;
            else
            {
                block.Foreground = Application.Current.Resources["TextSecondaryBrush"] as Brush;
                block.FontFamily = new FontFamily("Consolas, Courier New");
                block.FontSize = 11;
                block.VerticalAlignment = VerticalAlignment.Center;
                block.TextAlignment = TextAlignment.Right;
                block.Margin = new Thickness(10, 0, 0, 0);
            }
            return block;
        }

        /// <summary>(solid key, wash key) for a difficulty. See Resources/Theme/Colors.xaml.</summary>
        private static (string solid, string wash) RackDifficultyKeys(Models.SessionDifficulty difficulty)
            => difficulty switch
            {
                Models.SessionDifficulty.Medium => ("SessionDiffMediumBrush", "SessionDiffMediumWashBrush"),
                Models.SessionDifficulty.Hard => ("SessionDiffHardBrush", "SessionDiffHardWashBrush"),
                Models.SessionDifficulty.Extreme => ("SessionDiffExtremeBrush", "SessionDiffExtremeWashBrush"),
                _ => ("SessionDiffEasyBrush", "SessionDiffEasyWashBrush")
            };

        /// <summary>(badge text, solid key, wash key) for a provenance.</summary>
        private static (string label, string solid, string wash) RackSourceKeys(Models.SessionSource source)
            => source switch
            {
                Models.SessionSource.Custom =>
                    (LocOr("rack_src_yours", "YOURS"), "SessionSrcCustomBrush", "SessionSrcCustomWashBrush"),
                Models.SessionSource.Imported =>
                    (LocOr("rack_src_catalogue", "CAT"), "SessionSrcImportedBrush", "SessionSrcImportedWashBrush"),
                _ =>
                    (LocOr("rack_src_builtin", "BUILT-IN"), "SessionSrcBuiltInBrush", "SessionSrcBuiltInWashBrush")
            };

        /// <summary>
        /// Clip a content root to a rounded rectangle. WPF's Border paints a rounded frame but
        /// does NOT clip what is inside it, so a full-bleed child (here: the difficulty stripe)
        /// draws square corners over the row's rounded ones unless the root clips itself.
        /// </summary>
        private static void AttachRoundedClip(FrameworkElement element, double radius)
        {
            element.SizeChanged += (_, _) =>
            {
                try
                {
                    double w = element.ActualWidth, h = element.ActualHeight;
                    if (w <= 0 || h <= 0) { element.Clip = null; return; }
                    var clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
                    clip.Freeze();
                    element.Clip = clip;
                }
                catch (Exception ex) { App.Logger?.Debug("AttachRoundedClip: {E}", ex.Message); }
            };
        }

        // ------------------------------ the toolbar ------------------------------

        /// <summary>
        /// Build the source chips and difficulty dots once, and restore the persisted sort +
        /// source filter into the controls. Idempotent; the first
        /// <see cref="RepaintSessionRack"/> after the tab exists does it.
        /// </summary>
        private void EnsureSessionRackToolbar()
        {
            if (_rackToolbarBuilt) return;
            var tab = PresetsTab;
            if (tab?.RackSourceChips == null || tab.RackDifficultyChips == null) return;
            _rackToolbarBuilt = true;

            // Restore BEFORE building, so the chips come out already in the right state and the
            // first paint is not a flicker from "All" to whatever was saved.
            var settings = App.Settings?.Current;
            _rackSourceFilter = settings?.SessionRackSourceFilter ?? RackSourceAll;
            _rackSort = settings?.SessionRackSort ?? "recent";

            _rackToolbarSyncing = true;
            try
            {
                var chipStyle = TryFindTabStyle("SdRackChip");
                void AddSourceChip(string key)
                {
                    var chip = new ToggleButton
                    {
                        Tag = key,
                        Content = RackSourceChipLabel(key, 0),
                        IsChecked = key == _rackSourceFilter
                    };
                    if (chipStyle != null) chip.Style = chipStyle;
                    chip.Checked += RackSourceChip_Changed;
                    chip.Unchecked += RackSourceChip_Changed;
                    _rackSourceChips.Add(chip);
                    tab.RackSourceChips.Children.Add(chip);
                }
                AddSourceChip(RackSourceAll);
                AddSourceChip(RackSourceBuiltIn);
                AddSourceChip(RackSourceYours);
                AddSourceChip(RackSourceCatalogue);

                var dotStyle = TryFindTabStyle("SdRackDot");
                void AddDot(Models.SessionDifficulty difficulty)
                {
                    var (solid, _) = RackDifficultyKeys(difficulty);
                    var dot = new ToggleButton
                    {
                        Tag = difficulty,
                        Content = "●",
                        IsChecked = true,   // all four on by default, and NOT persisted - see AppSettings
                        ToolTip = RackDifficultyLabel(difficulty)
                    };
                    if (dotStyle != null) dot.Style = dotStyle;
                    dot.SetResourceReference(Control.ForegroundProperty, solid);
                    dot.Checked += RackDifficultyChip_Changed;
                    dot.Unchecked += RackDifficultyChip_Changed;
                    _rackDifficultyChips.Add(dot);
                    tab.RackDifficultyChips.Children.Add(dot);
                }
                AddDot(Models.SessionDifficulty.Easy);
                AddDot(Models.SessionDifficulty.Medium);
                AddDot(Models.SessionDifficulty.Hard);
                AddDot(Models.SessionDifficulty.Extreme);

                // The sort combo is declared in XAML; only its selection is restored here. Match
                // on Tag, never on index - see the note beside it in PresetsTabView.xaml.
                if (tab.CmbRackSort != null)
                {
                    foreach (var item in tab.CmbRackSort.Items.OfType<ComboBoxItem>())
                    {
                        if ((item.Tag as string) == _rackSort) { item.IsSelected = true; break; }
                    }
                    if (tab.CmbRackSort.SelectedItem == null && tab.CmbRackSort.Items.Count > 0)
                        tab.CmbRackSort.SelectedIndex = 0;
                }

                if (tab.TxtRackSearchHint != null)
                    tab.TxtRackSearchHint.Visibility = string.IsNullOrEmpty(tab.TxtRackSearch?.Text)
                        ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "EnsureSessionRackToolbar failed"); }
            finally { _rackToolbarSyncing = false; }
        }

        private static string RackSourceLabel(string key) => key switch
        {
            RackSourceBuiltIn => LocOr("rack_source_builtin", "Built-in"),
            RackSourceYours => LocOr("rack_source_yours", "Yours"),
            RackSourceCatalogue => LocOr("rack_source_catalogue", "Catalogue"),
            _ => LocOr("rack_source_all", "All")
        };

        private static string RackSourceChipLabel(string key, int count)
            => $"{RackSourceLabel(key)}  {count}";

        private static string RackDifficultyLabel(Models.SessionDifficulty difficulty) => difficulty switch
        {
            Models.SessionDifficulty.Medium => LocOr("rack_diff_medium", "Medium"),
            Models.SessionDifficulty.Hard => LocOr("rack_diff_hard", "Hard"),
            Models.SessionDifficulty.Extreme => LocOr("rack_diff_extreme", "Extreme"),
            _ => LocOr("rack_diff_easy", "Easy")
        };

        /// <summary>Live counts on the source chips + the "n of m" hint in the zone header. Counts
        /// ignore the difficulty dots and the search box on purpose: a chip that said "Yours 0"
        /// because a dot was off would read as "my sessions are gone".</summary>
        private void UpdateRackToolbarCounts(List<Models.Session> all, int shownCount)
        {
            foreach (var chip in _rackSourceChips)
            {
                var key = chip.Tag as string ?? RackSourceAll;
                int n = key switch
                {
                    RackSourceBuiltIn => all.Count(s => s.Source == Models.SessionSource.BuiltIn),
                    RackSourceYours => all.Count(s => s.Source == Models.SessionSource.Custom),
                    RackSourceCatalogue => all.Count(s => s.Source == Models.SessionSource.Imported),
                    _ => all.Count
                };
                chip.Content = RackSourceChipLabel(key, n);
            }

            if (PresetsTab?.TxtRackCount != null)
                PresetsTab.TxtRackCount.Text = shownCount == all.Count
                    ? string.Format(LocOr("rack_count_all", "{0} sessions"), all.Count)
                    : string.Format(LocOr("rack_count_filtered", "{0} of {1}"), shownCount, all.Count);
        }

        /// <summary>Source chips behave as a radio group, enforced here rather than with
        /// RadioButtons so they can share the chip shape (the Assets tab does the same).</summary>
        private void RackSourceChip_Changed(object sender, RoutedEventArgs e)
        {
            if (_rackToolbarSyncing) return;
            if (sender is not ToggleButton btn) return;

            _rackToolbarSyncing = true;
            try
            {
                // Clicking the active chip again must not clear the filter - there would be no
                // chip lit and no way to tell what the list is showing.
                if (btn.IsChecked != true) { btn.IsChecked = true; return; }
                foreach (var chip in _rackSourceChips) chip.IsChecked = ReferenceEquals(chip, btn);
            }
            finally { _rackToolbarSyncing = false; }

            var key = btn.Tag as string ?? RackSourceAll;
            if (key == _rackSourceFilter) return;
            _rackSourceFilter = key;

            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SessionRackSourceFilter = key;
                App.Settings.Save();
            }
            RepaintSessionRack();
        }

        private void RackDifficultyChip_Changed(object sender, RoutedEventArgs e)
        {
            if (_rackToolbarSyncing) return;
            if (sender is not ToggleButton btn || btn.Tag is not Models.SessionDifficulty difficulty) return;

            if (btn.IsChecked == true) _rackDifficulties.Add(difficulty);
            else _rackDifficulties.Remove(difficulty);

            RepaintSessionRack();
        }

        internal void CmbRackSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_rackToolbarSyncing) return;
            var key = (PresetsTab?.CmbRackSort?.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrEmpty(key) || key == _rackSort) return;
            _rackSort = key;

            if (App.Settings?.Current != null)
            {
                App.Settings.Current.SessionRackSort = key;
                App.Settings.Save();
            }
            RepaintSessionRack();
        }

        internal void TxtRackSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = PresetsTab?.TxtRackSearch?.Text ?? "";

            // The watermark is a plain sibling TextBlock, so hiding it is this handler's job.
            if (PresetsTab?.TxtRackSearchHint != null)
                PresetsTab.TxtRackSearchHint.Visibility = text.Length == 0
                    ? Visibility.Visible : Visibility.Collapsed;

            if (_rackToolbarSyncing) return;

            var trimmed = text.Trim();
            if (trimmed == _rackSearch) return;   // whitespace-only edits are not a new filter
            _rackSearch = trimmed;
            RepaintSessionRack();
        }

        /// <summary>
        /// Drop every VIEW filter back to "show everything" and repaint - source chips, all four
        /// difficulty dots, the search box. Touches no session and no file.
        ///
        /// <para>The source filter is persisted, so this writes the reset through to settings:
        /// leaving the chip lit while the list ignores it is worse than forgetting the
        /// preference.</para>
        /// </summary>
        private void ClearSessionRackFilters()
        {
            try
            {
                _rackToolbarSyncing = true;
                try
                {
                    _rackSourceFilter = RackSourceAll;
                    foreach (var chip in _rackSourceChips)
                        chip.IsChecked = (chip.Tag as string) == RackSourceAll;

                    // Re-add from the enum, not from the chips: if the toolbar has not been built
                    // yet the chip list is empty, and clearing off it would leave EVERY
                    // difficulty filtered out - an empty rack from a method whose whole job is to
                    // un-empty it.
                    _rackDifficulties.Clear();
                    foreach (Models.SessionDifficulty d in Enum.GetValues(typeof(Models.SessionDifficulty)))
                        _rackDifficulties.Add(d);
                    foreach (var dot in _rackDifficultyChips) dot.IsChecked = true;

                    _rackSearch = "";
                    if (PresetsTab?.TxtRackSearch != null) PresetsTab.TxtRackSearch.Text = "";
                }
                finally { _rackToolbarSyncing = false; }

                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.SessionRackSourceFilter = RackSourceAll;
                    App.Settings.Save();
                }
                RepaintSessionRack();
            }
            catch (Exception ex) { App.Logger?.Debug("ClearSessionRackFilters: {E}", ex.Message); }
        }

        /// <summary>
        /// Move the selected-row styling without rebuilding the list.
        ///
        /// <para>Called from SelectSession / SelectPreset, which fire on every click: a full
        /// RepaintSessionRack there would throw away the hovered row's transform mid-hover and
        /// restart the entrance stagger. Swapping two Styles is enough - neither carries a local
        /// value the row sets itself.</para>
        /// </summary>
        private void RefreshSessionRackSelection()
        {
            try
            {
                var panel = PresetsTab?.SessionRackPanel;
                if (panel == null) return;

                var selectedStyle = TryFindTabStyle("SdSessionRowSelected");
                var normalStyle = TryFindTabStyle("SdSessionRow");
                if (selectedStyle == null || normalStyle == null) return;

                foreach (var row in panel.Children.OfType<Border>())
                {
                    var want = _selectedSession != null && (row.Tag as string) == _selectedSession.Id
                        ? selectedStyle : normalStyle;
                    if (!ReferenceEquals(row.Style, want)) row.Style = want;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshSessionRackSelection: {E}", ex.Message); }
        }

        /// <summary>True when the rack already shows a row for <paramref name="sessionId"/>. Rows
        /// carry their session id in Tag - see <see cref="BuildSessionRackRow"/>.</summary>
        private bool HasSessionRackRow(string sessionId)
        {
            try
            {
                var panel = PresetsTab?.SessionRackPanel;
                if (panel == null || string.IsNullOrEmpty(sessionId)) return false;
                return panel.Children.OfType<Border>().Any(b => (b.Tag as string) == sessionId);
            }
            catch { return false; }
        }

        /// <summary>
        /// One tinted chip on a rack row - the difficulty pill and the provenance badge are the
        /// same object with different keys.
        ///
        /// <para>Takes resource KEYS, not brushes, and binds them with
        /// <see cref="FrameworkElement.SetResourceReference"/> - the code-behind equivalent of a
        /// DynamicResource. A brush resolved once and assigned would freeze the colour code at
        /// whatever the dictionary held when the row was built, which is exactly what a mod
        /// override (or a theme swap mid-session) needs it not to do.</para>
        ///
        /// <para>The colours themselves are the static SessionDiff* / SessionSrc* families in
        /// Resources/Theme: they encode difficulty and provenance, NOT brand, so they must not
        /// follow the mod accent - a drone-green build would otherwise paint EASY and EXTREME the
        /// same green.</para>
        ///
        /// <para><paramref name="pillStyleKey"/> exists for the preset rail: a 34px chip needs the
        /// tighter <c>SdChipTag</c> geometry, because <c>SdPill</c>'s padding and 9px lead are
        /// tuned for a 44px rack row and overflow a pill sitting inside another pill.</para>
        /// </summary>
        private Border MakeRackPill(string text, string washKey, string solidKey,
                                    string? textStyleKey = null, string pillStyleKey = "SdPill")
        {
            var pill = new Border();
            var pillStyle = TryFindTabStyle(pillStyleKey);
            if (pillStyle != null) pill.Style = pillStyle;
            else
            {
                pill.CornerRadius = new CornerRadius(5);
                pill.Padding = new Thickness(7, 1.5, 7, 1.5);
                pill.Margin = new Thickness(9, 0, 0, 0);
                pill.VerticalAlignment = VerticalAlignment.Center;
            }
            // Background as a LOCAL value is right here: SdPill deliberately sets none, so there
            // is no setter to fight.
            pill.SetResourceReference(Border.BackgroundProperty, washKey);

            var label = new TextBlock { Text = text };
            var labelStyle = TryFindTabStyle(textStyleKey ?? "SdPillText");
            if (labelStyle != null) label.Style = labelStyle;
            else
            {
                label.FontSize = 9.5;
                label.FontWeight = FontWeights.Bold;
            }
            label.SetResourceReference(TextBlock.ForegroundProperty, solidKey);

            pill.Child = label;
            return pill;
        }

        /// <summary>
        /// A glyph button on a custom session row.
        ///
        /// <para>Was ~20 lines of hand-built <see cref="FrameworkElementFactory"/> per button,
        /// twice over, painting #353555 with a pink hover. It is now the tab's <c>SdRowAction</c>
        /// style, so a custom row's buttons are the same object as a built-in row's and re-tint on
        /// a mod switch. FrameworkElementFactory is also the obsolete half of the templating API -
        /// there was no reason to keep two copies of it alive for a rounded rectangle.</para>
        /// </summary>
        private Button CreateSessionActionButton(string content, string tooltip, string tag, RoutedEventHandler handler)
            => CreateSessionRowButton(content, tooltip, tag, handler, "SdRowAction");

        /// <summary>Same button, red hover. Destructive actions do not wear the brand colour.</summary>
        private Button CreateSessionDeleteButton(string content, string tooltip, string tag, RoutedEventHandler handler)
            => CreateSessionRowButton(content, tooltip, tag, handler, "SdRowActionDanger");

        private Button CreateSessionRowButton(string content, string tooltip, string tag,
                                              RoutedEventHandler handler, string styleKey)
        {
            var btn = new Button
            {
                Content = content,
                ToolTip = tooltip,
                Tag = tag
            };
            var style = TryFindTabStyle(styleKey);
            if (style != null) btn.Style = style;
            else
            {
                btn.Width = 28;
                btn.Height = 28;
                btn.Background = Brushes.Transparent;
                btn.Foreground = Application.Current.Resources["TextDimBrush"] as Brush;
                btn.BorderThickness = new Thickness(0);
                btn.FontSize = 12.5;
                btn.Cursor = System.Windows.Input.Cursors.Hand;
                btn.Margin = new Thickness(3, 0, 0, 0);
            }
            btn.Click += handler;
            return btn;
        }

        private void SelectSession(Models.Session session)
        {
            _selectedSession = session;

            // Clear preset selection
            _selectedPreset = null;
            RefreshPresetsList();

            // Hide preset panel, show session panel
            PresetsTab.PresetDetailScroller.Visibility = Visibility.Collapsed;
            PresetsTab.PresetButtonsPanel.Visibility = Visibility.Collapsed;
            PresetsTab.SessionDetailScroller.Visibility = Visibility.Visible;
            PresetsTab.SessionButtonsPanel.Visibility = Visibility.Visible;
            PresetsTab.SessionSpoilerPanel.Visibility = Visibility.Collapsed;
            PresetsTab.BtnRevealSpoilers.Content = Loc.Get("btn_reveal_details");

            PresetsTab.TxtDetailTitle.Text = $"{session.Icon} {session.GetModeAwareName()}";
            PresetsTab.TxtDetailSubtitle.Text = GenerateSessionTimelineDescription(session);
            PresetsTab.TxtSessionDuration.Text = $"{session.DurationMinutes} minutes";

            // Apply level-based XP multiplier for display
            var level = App.Settings?.Current?.PlayerLevel ?? 1;
            var multiplier = App.Progression?.GetSessionXPMultiplier(level) ?? 1.0;
            var scaledXP = (int)Math.Round(session.BonusXP * multiplier);
            if (multiplier > 1.0)
                PresetsTab.TxtSessionXP.Text = $"+{scaledXP} XP ({multiplier:F1}x)";
            else
                PresetsTab.TxtSessionXP.Text = $"+{scaledXP} XP";

            PresetsTab.TxtSessionDifficulty.Text = session.GetDifficultyText();

            // Show manual description + auto-generated feature summary (mode-aware)
            var description = session.GetModeAwareDescription() ?? "";
            var featureSummary = session.GenerateFeatureDescription();
            if (!string.IsNullOrWhiteSpace(description))
                PresetsTab.TxtSessionDescription.Text = description + "\n\n─────────────────\n\n" + featureSummary;
            else
                PresetsTab.TxtSessionDescription.Text = featureSummary;

            // Update XP color based on difficulty
            PresetsTab.TxtSessionXP.Foreground = session.Difficulty switch
            {
                Models.SessionDifficulty.Easy => new SolidColorBrush(Color.FromRgb(144, 238, 144)),
                Models.SessionDifficulty.Medium => new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                Models.SessionDifficulty.Hard => new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                Models.SessionDifficulty.Extreme => new SolidColorBrush(Color.FromRgb(255, 99, 71)),
                _ => new SolidColorBrush(Color.FromRgb(144, 238, 144))
            };

            // Hide corner GIF option for custom sessions
            PresetsTab.CornerGifOptionPanel.Visibility = session.HasCornerGifOption ? Visibility.Visible : Visibility.Collapsed;

            // Populate spoiler details
            PresetsTab.TxtSessionFlash.Text = session.GetSpoilerFlash();
            PresetsTab.TxtSessionSubliminal.Text = session.GetSpoilerSubliminal();
            PresetsTab.TxtSessionAudio.Text = session.GetSpoilerAudio();
            PresetsTab.TxtSessionOverlays.Text = session.GetSpoilerOverlays();
            PresetsTab.TxtSessionExtras.Text = session.GetSpoilerInteractive();
            PresetsTab.TxtSessionTimeline.Text = session.GetSpoilerTimeline();

            PresetsTab.BtnStartSession.IsEnabled = session.IsAvailable;
            PresetsTab.BtnStartSession.Content = session.IsAvailable ? "▶ Start Session" : "🔒 Coming Soon";
            PresetsTab.BtnExportSession.IsEnabled = true;

            // Move the selected livery onto the new row. A style swap, NOT a RepaintSessionRack:
            // this runs on every click, and rebuilding the list here would drop the hovered row's
            // transform mid-hover and re-run the entrance stagger.
            RefreshSessionRackSelection();

            // Move the tab's one ambient loop onto the row that is now selected.
            RefreshCardSheen();
        }

        private string GenerateSessionTimelineDescription(Models.Session session)
        {
            var parts = new List<string>();

            if (session.Settings.FlashEnabled)
                parts.Add($"⚡ Flashes ({session.Settings.FlashPerHour}/hr)");
            if (session.Settings.SubliminalEnabled)
                parts.Add($"💭 Subliminals ({session.Settings.SubliminalPerMin}/min)");
            if (session.Settings.AudioWhispersEnabled)
                parts.Add("🔊 Audio Whispers");
            if (session.Settings.PinkFilterEnabled)
                parts.Add("💗 Pink Filter");
            if (session.Settings.SpiralEnabled)
                parts.Add("🌀 Spiral");
            if (session.Settings.BouncingTextEnabled)
                parts.Add("📝 Bouncing Text");
            if (session.Settings.BubblesEnabled)
                parts.Add("🫧 Bubbles");
            if (session.Settings.LockCardEnabled)
                parts.Add("🔒 Lock Cards");
            if (session.Settings.MandatoryVideosEnabled)
                parts.Add("🎬 Videos");
            if (session.Settings.MindWipeEnabled)
                parts.Add("🧠 Mind Wipe");

            if (parts.Count == 0)
                return "";

            return string.Join(" • ", parts);
        }

        internal void SessionDropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length == 1 && files[0].EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                {
                    e.Effects = DragDropEffects.Copy;
                    PresetsTab.SessionDropZone.BorderBrush = FindResource("PinkBrush") as SolidColorBrush;
                    PresetsTab.DropZoneIcon.Text = "📥";
                    PresetsTab.DropZoneIcon.Foreground = FindResource("PinkBrush") as SolidColorBrush;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                    PresetsTab.SessionDropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                    PresetsTab.DropZoneIcon.Text = "❌";
                    PresetsTab.DropZoneIcon.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        internal void SessionDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length == 1 && files[0].EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        internal void SessionDropZone_DragLeave(object sender, DragEventArgs e)
        {
            PresetsTab.SessionDropZone.BorderBrush = Application.Current.Resources["PanelAccentBrush"] as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(64, 64, 96));
            PresetsTab.DropZoneIcon.Text = "📂";
            PresetsTab.DropZoneIcon.Foreground = new SolidColorBrush(Color.FromRgb(112, 112, 144));
            PresetsTab.DropZoneStatus.Visibility = Visibility.Collapsed;
        }

        // Global window drag-drop handlers
        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var dropType = DetectDropType(files);

                if (dropType != DropType.None)
                {
                    e.Effects = DragDropEffects.Copy;
                    UpdateDropOverlay(dropType, files);
                    // Hide browser to avoid WebView2 airspace issue (renders on top of WPF)
                    SettingsTab.BrowserContainer.Visibility = Visibility.Hidden;
                    GlobalDropOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>
        /// Ctrl+T from the main window — open the avatar chat input panel.
        /// Bound via Window.InputBindings + Window.CommandBindings using
        /// AvatarTubeWindow.OpenChatCommand.
        /// </summary>
        private void OpenAvatarChat_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            App.AvatarWindow?.OpenChatInput();
            e.Handled = true;
        }

        /// <summary>
        /// Click on the Companion-tab "Chat: Ctrl+T" pill — opens the capture dialog,
        /// saves the new shortcut to settings, then re-applies the binding to both
        /// MainWindow and AvatarTubeWindow without restart.
        /// </summary>
        internal void BtnChatShortcut_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings?.Current?.CompanionPrompt;
            if (settings == null) return;

            var dlg = new ChatShortcutCaptureDialog
            {
                Owner = this,
                GlobalHotkey = settings.ChatShortcutGlobal,
            };
            var ok = dlg.ShowDialog();
            if (ok != true) return;

            if (dlg.ResetToDefault)
            {
                settings.ChatShortcutKey = "T";
                settings.ChatShortcutModifiers = "Control";
            }
            else
            {
                settings.ChatShortcutKey = dlg.CapturedKey.ToString();
                settings.ChatShortcutModifiers = AvatarTubeWindow.SerializeModifiers(dlg.CapturedModifiers);
            }
            settings.ChatShortcutGlobal = dlg.GlobalHotkey;
            App.Settings?.Save();

            // Re-apply on both windows so the new shortcut takes effect immediately.
            AvatarTubeWindow.ApplyChatShortcutTo(this);
            if (App.AvatarWindow != null) AvatarTubeWindow.ApplyChatShortcutTo(App.AvatarWindow);

            // Re-arm (or unregister) the system-wide hotkey based on the toggle.
            ApplyGlobalChatHotkey();

            RefreshChatShortcutLabel();
        }

        /// <summary>
        /// Reads the user's saved chat-shortcut combo and registers it as a system-wide
        /// hotkey. Falls back silently if the OS rejects the combo (already taken).
        /// </summary>
        private void ApplyGlobalChatHotkey()
        {
            var s = App.Settings?.Current?.CompanionPrompt;

            // Honor the user's toggle. When off, we still rely on the in-window KeyBinding
            // (registered by AvatarTubeWindow.ApplyChatShortcutTo) so the shortcut works
            // when one of our windows has focus — but it won't steal focus from a browser
            // or other foreground app.
            if (s?.ChatShortcutGlobal == false)
            {
                Services.GlobalHotkeyService.Unregister(Services.GlobalHotkeyService.ChatHotkeyId);
                return;
            }

            var keyName = string.IsNullOrWhiteSpace(s?.ChatShortcutKey) ? "T" : s!.ChatShortcutKey;
            var modsName = s?.ChatShortcutModifiers ?? "Control";

            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) key = Key.T;
            var mods = ModifierKeys.None;
            if (!string.IsNullOrWhiteSpace(modsName))
            {
                foreach (var part in modsName.Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Enum.TryParse<ModifierKeys>(part, ignoreCase: true, out var mk)) mods |= mk;
                }
            }
            if (mods == ModifierKeys.None) mods = ModifierKeys.Control;

            Services.GlobalHotkeyService.Register(Services.GlobalHotkeyService.ChatHotkeyId, this, mods, key, () =>
            {
                // Marshal to UI thread — Win32 hotkeys arrive on the message-pump thread
                // (which is the dispatcher in WPF, but the helper API doesn't enforce it).
                Dispatcher.BeginInvoke(new Action(BringToForegroundAndOpenChat));
            });
        }

        /// <summary>
        /// Full "wake up the app" sequence for the global chat hotkey: un-minimize
        /// MainWindow, bring it to the foreground, re-show the avatar tube (which
        /// auto-hides while the main window is minimized in attached mode), then
        /// open the chat input. Used when the shortcut is pressed from another app.
        /// </summary>
        private void BringToForegroundAndOpenChat()
        {
            try
            {
                // 1. Restore MainWindow if minimized.
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Show();

                // 2. Force MainWindow to the foreground. Topmost flicker is the WPF idiom
                //    that bypasses focus-stealing prevention without Win32 antics.
                Activate();
                Topmost = true;
                Topmost = false;

                // 3. Re-show the avatar tube. When MainWindow minimizes, attached avatars
                //    are hidden by HideAvatarTube — so without this the chat input would
                //    open on a hidden window.
                ShowAvatarTube();

                // 4. Open chat input. AvatarTubeWindow.OpenChatInput does its own
                //    AttachThreadInput dance to claim keyboard focus reliably.
                App.AvatarWindow?.OpenChatInput();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BringToForegroundAndOpenChat failed");
            }
        }

        /// <summary>
        /// Updates every label that quotes the saved chat shortcut. Two surfaces launch the same
        /// capture dialog since Phase 2 — the Companion Workshop pill and the Shortcuts row in
        /// Settings → Devices — and the dialog remains the single editor, so both labels are
        /// repainted from the same call. Each is guarded on its own: a Settings view that hasn't
        /// been realized must not stop the Companion pill from updating (or vice versa), and the
        /// old blanket catch would have swallowed the second write silently.
        /// </summary>
        public void RefreshChatShortcutLabel()
        {
            var text = AvatarTubeWindow.FormatChatShortcut();
            try
            {
                if (CompanionTab?.TxtChatShortcutLabel != null)
                    CompanionTab.TxtChatShortcutLabel.Text = text;
            }
            catch { /* Tab not yet realized, fine */ }
            try
            {
                if (AppSettingsTab?.TxtChatShortcutLabelDevices != null)
                    AppSettingsTab.TxtChatShortcutLabelDevices.Text = text;
            }
            catch { /* Settings view not yet realized, fine */ }
        }

        // ---------------------------------------------------------------------
        // Camera start/stop shortcut (suggestion #674). Mirrors the chat shortcut
        // above: a system-wide Win32 hotkey plus an in-window KeyBinding fallback.
        // The in-window binding lives on MainWindow only (that's where the toggle
        // command handler is) so it fires whenever the main window has focus.
        // ---------------------------------------------------------------------

        /// <summary>Routed command bound to the camera-shortcut KeyBinding.</summary>
        public static readonly RoutedUICommand ToggleCameraCommand =
            new RoutedUICommand("Toggle Camera Tracking", "ToggleCamera", typeof(MainWindow));

        private bool _cameraCommandBound;

        /// <summary>
        /// Rebuilds the in-window camera-shortcut KeyBinding from the user's setting.
        /// Ensures the backing CommandBinding is wired once. Mirrors
        /// <see cref="AvatarTubeWindow.ApplyChatShortcutTo"/> but for this window only.
        /// </summary>
        private void ApplyCameraShortcutTo()
        {
            // Wire the command handler exactly once.
            if (!_cameraCommandBound)
            {
                CommandBindings.Add(new CommandBinding(ToggleCameraCommand, (_, e) =>
                {
                    ToggleWebcamFromHotkey();
                    e.Handled = true;
                }));
                _cameraCommandBound = true;
            }

            var s = App.Settings?.Current?.CompanionPrompt;
            var keyName = string.IsNullOrWhiteSpace(s?.CameraShortcutKey) ? "K" : s!.CameraShortcutKey;
            var modsName = s?.CameraShortcutModifiers ?? "Control,Alt";

            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) key = Key.K;
            var mods = ParseModifiers(modsName, ModifierKeys.Control | ModifierKeys.Alt);

            // Remove any existing camera-shortcut bindings so repeated calls don't stack.
            for (int i = InputBindings.Count - 1; i >= 0; i--)
            {
                if (InputBindings[i] is KeyBinding kb && kb.Command == ToggleCameraCommand)
                    InputBindings.RemoveAt(i);
            }

            // KeyGesture rejects a handful of unusual combos; fall back to Ctrl+Alt+K
            // rather than throwing out of the load/settings-change path.
            try
            {
                InputBindings.Add(new KeyBinding(ToggleCameraCommand, key, mods));
            }
            catch (NotSupportedException)
            {
                App.Logger?.Warning("ApplyCameraShortcutTo: rejected combo {Mods}+{Key}, falling back to Ctrl+Alt+K", mods, key);
                try
                {
                    InputBindings.Add(new KeyBinding(ToggleCameraCommand, Key.K, ModifierKeys.Control | ModifierKeys.Alt));
                }
                catch { }
            }
        }

        /// <summary>
        /// Reads the saved camera-shortcut combo and registers it as a system-wide
        /// hotkey. Honours the per-shortcut Global toggle; falls back silently to the
        /// in-window KeyBinding when off or when the OS rejects the combo. Mirrors
        /// <see cref="ApplyGlobalChatHotkey"/>.
        /// </summary>
        private void ApplyGlobalCameraHotkey()
        {
            var s = App.Settings?.Current?.CompanionPrompt;

            if (s?.CameraShortcutGlobal == false)
            {
                Services.GlobalHotkeyService.Unregister(Services.GlobalHotkeyService.CameraHotkeyId);
                return;
            }

            var keyName = string.IsNullOrWhiteSpace(s?.CameraShortcutKey) ? "K" : s!.CameraShortcutKey;
            var modsName = s?.CameraShortcutModifiers ?? "Control,Alt";

            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) key = Key.K;
            var mods = ParseModifiers(modsName, ModifierKeys.Control | ModifierKeys.Alt);

            Services.GlobalHotkeyService.Register(Services.GlobalHotkeyService.CameraHotkeyId, this, mods, key, () =>
            {
                // Win32 hotkeys arrive on the message-pump thread — marshal to the UI thread.
                Dispatcher.BeginInvoke(new Action(ToggleWebcamFromHotkey));
            });
        }

        /// <summary>Shared modifier-string parser for the shortcut combos.</summary>
        private static ModifierKeys ParseModifiers(string? modsName, ModifierKeys fallback)
        {
            var mods = ModifierKeys.None;
            if (!string.IsNullOrWhiteSpace(modsName))
            {
                foreach (var part in modsName.Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Enum.TryParse<ModifierKeys>(part, ignoreCase: true, out var mk)) mods |= mk;
                }
            }
            return mods == ModifierKeys.None ? fallback : mods;
        }

        /// <summary>"Ctrl+Alt+K" — for the camera-shortcut pill label.</summary>
        private static string FormatCameraShortcut()
        {
            var s = App.Settings?.Current?.CompanionPrompt;
            var keyName = string.IsNullOrWhiteSpace(s?.CameraShortcutKey) ? "K" : s!.CameraShortcutKey;
            var modsName = s?.CameraShortcutModifiers ?? "Control,Alt";

            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) key = Key.K;
            var mods = ParseModifiers(modsName, ModifierKeys.Control | ModifierKeys.Alt);

            var parts = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        /// <summary>Updates both camera-shortcut labels (Companion pill + Settings → Devices row).</summary>
        public void RefreshCameraShortcutLabel()
        {
            var text = FormatCameraShortcut();
            try
            {
                if (CompanionTab?.TxtCameraShortcutLabel != null)
                    CompanionTab.TxtCameraShortcutLabel.Text = text;
            }
            catch { /* Tab not yet realized, fine */ }
            try
            {
                if (AppSettingsTab?.TxtCameraShortcutLabelDevices != null)
                    AppSettingsTab.TxtCameraShortcutLabelDevices.Text = text;
            }
            catch { /* Settings view not yet realized, fine */ }
        }

        /// <summary>
        /// Click on the Companion-tab camera-shortcut pill — opens the same capture
        /// dialog the chat shortcut uses, saves the new combo, then re-applies both the
        /// in-window binding and the system-wide hotkey without a restart.
        /// </summary>
        internal void BtnCameraShortcut_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings?.Current?.CompanionPrompt;
            if (settings == null) return;

            var dlg = new ChatShortcutCaptureDialog
            {
                Owner = this,
                GlobalHotkey = settings.CameraShortcutGlobal,
            };
            var ok = dlg.ShowDialog();
            if (ok != true) return;

            if (dlg.ResetToDefault)
            {
                settings.CameraShortcutKey = "K";
                settings.CameraShortcutModifiers = "Control,Alt";
            }
            else
            {
                settings.CameraShortcutKey = dlg.CapturedKey.ToString();
                settings.CameraShortcutModifiers = AvatarTubeWindow.SerializeModifiers(dlg.CapturedModifiers);
            }
            settings.CameraShortcutGlobal = dlg.GlobalHotkey;
            App.Settings?.Save();

            ApplyCameraShortcutTo();
            ApplyGlobalCameraHotkey();
            RefreshCameraShortcutLabel();
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var dropType = DetectDropType(files);
                e.Effects = dropType != DropType.None ? DragDropEffects.Copy : DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            GlobalDropOverlay.Visibility = Visibility.Collapsed;
            SettingsTab.BrowserContainer.Visibility = Visibility.Visible;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            GlobalDropOverlay.Visibility = Visibility.Collapsed;
            SettingsTab.BrowserContainer.Visibility = Visibility.Visible;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            // Single-file media drop: prompt Play / Edit / Add-to-Library.
            // Multi-file, folder, and .zip drops fall through to the existing flow.
            if (files.Length == 1 && File.Exists(files[0]) && IsDeeperPlayableMedia(files[0]))
            {
                var choice = ShowMediaDropChoiceDialog(files[0]);
                switch (choice)
                {
                    case MediaDropChoice.Play: OpenInDeeperPlayer(files[0]); return;
                    case MediaDropChoice.Edit: OpenInDeeperEditorForMedia(files[0]); return;
                    case MediaDropChoice.Library: await HandleAssetDropAsync(files); return;
                    case MediaDropChoice.Cancel: return;
                }
            }

            var dropType = DetectDropType(files);

            switch (dropType)
            {
                case DropType.Session:
                    HandleSessionDrop(files[0]);
                    break;

                case DropType.Preset:
                    HandlePresetDrop(files[0]);
                    break;

                case DropType.Enhancement:
                    ImportEnhancementFiles(files);
                    break;

                case DropType.Mod:
                    await HandleModDropAsync(files[0]);
                    break;

                case DropType.Assets:
                case DropType.Zip:
                case DropType.Folder:
                    await HandleAssetDropAsync(files);
                    break;
            }
        }

        private enum DropType { None, Session, Preset, Assets, Zip, Folder, Enhancement, Mod }

        private static readonly HashSet<string> AssetVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".mpeg", ".mpg", ".3gp"
        };

        private static readonly HashSet<string> AssetImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif"
        };

        // Deeper-playable subsets — narrower than AssetVideoExtensions because the
        // player's WebView2 + NAudio backends only handle these. Used by the
        // "Open with CCP" file association and the single-file drop prompt.
        private static readonly HashSet<string> DeeperVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v"
        };

        private static readonly HashSet<string> DeeperAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg"
        };

        private enum MediaDropChoice { Cancel, Play, Edit, Library }

        private static bool IsDeeperPlayableMedia(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = Path.GetExtension(path);
            return DeeperVideoExtensions.Contains(ext) || DeeperAudioExtensions.Contains(ext);
        }

        private static DropType DetectDropType(string[] files)
        {
            if (files.Length == 0) return DropType.None;

            // Single session file
            if (files.Length == 1 && files[0].EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                return DropType.Session;

            // Single preset file
            if (files.Length == 1 && files[0].EndsWith(".preset.json", StringComparison.OrdinalIgnoreCase))
                return DropType.Preset;

            // Single mod package. Must be detected before the generic zip/asset
            // branches — a .ccpmod IS a zip, but it installs, not extracts.
            if (files.Length == 1 && files[0].EndsWith(".ccpmod", StringComparison.OrdinalIgnoreCase))
                return DropType.Mod;

            // Deeper enhancement project file(s). Accept the canonical *.ccpenh.json
            // double-suffix or a plain *.json (the serializer rejects non-enhancement
            // JSON on import), but never a *.session.json / *.preset.json — those are
            // handled above and would otherwise be swallowed by the plain-*.json rule.
            if (files.All(IsImportableEnhancementPath)
                && !files.Any(f => f.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                && !files.Any(f => f.EndsWith(".preset.json", StringComparison.OrdinalIgnoreCase)))
                return DropType.Enhancement;

            // Single folder
            if (files.Length == 1 && Directory.Exists(files[0]))
                return DropType.Folder;

            // Check for ZIP files or asset files
            var hasZip = false;
            var hasAssets = false;

            foreach (var file in files)
            {
                if (Directory.Exists(file))
                {
                    hasAssets = true;
                    continue;
                }

                var ext = Path.GetExtension(file);
                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    hasZip = true;
                else if (AssetVideoExtensions.Contains(ext) || AssetImageExtensions.Contains(ext))
                    hasAssets = true;
            }

            if (hasZip) return DropType.Zip;
            if (hasAssets) return DropType.Assets;

            return DropType.None;
        }

        private void UpdateDropOverlay(DropType dropType, string[] files)
        {
            switch (dropType)
            {
                case DropType.Session:
                    DropOverlayIcon.Text = "📋";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_import_session");
                    DropOverlaySubtitle.Text = Path.GetFileName(files[0]);
                    break;

                case DropType.Preset:
                    DropOverlayIcon.Text = "🎛️";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_import_preset");
                    DropOverlaySubtitle.Text = Path.GetFileName(files[0]);
                    break;

                case DropType.Enhancement:
                    DropOverlayIcon.Text = "🌊";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_import_enhancement");
                    DropOverlaySubtitle.Text = files.Length == 1
                        ? Path.GetFileName(files[0])
                        : $"{files.Length} enhancements";
                    break;

                case DropType.Mod:
                    DropOverlayIcon.Text = "🧩";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_install_mod");
                    DropOverlaySubtitle.Text = Path.GetFileName(files[0]);
                    break;

                case DropType.Zip:
                    DropOverlayIcon.Text = "📦";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_extract_assets");
                    var zipCount = files.Count(f => Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase));
                    DropOverlaySubtitle.Text = zipCount == 1
                        ? Path.GetFileName(files.First(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                        : $"{zipCount} ZIP files";
                    break;

                case DropType.Folder:
                    DropOverlayIcon.Text = "📁";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_import_folder");
                    DropOverlaySubtitle.Text = $"Scan for images & videos";
                    break;

                case DropType.Assets:
                    DropOverlayIcon.Text = "🖼️";
                    DropOverlayTitle.Text = Loc.Get("label_drop_to_import_assets");
                    DropOverlaySubtitle.Text = files.Length == 1
                        ? Path.GetFileName(files[0])
                        : $"{files.Length} files";
                    break;
            }
        }

        private void HandleSessionDrop(string filePath)
        {
            // Validate and import session
            if (_sessionFileService == null)
            {
                _sessionFileService = new Services.SessionFileService();
            }

            if (!_sessionFileService.ValidateSessionFile(filePath, out var errorMessage))
            {
                ShowDropZoneStatus($"Invalid: {errorMessage}", isError: true);
                return;
            }

            if (_sessionManager == null)
            {
                InitializeSessionManager();
            }

            var result = _sessionManager!.ImportSession(filePath);
            if (result.success)
            {
                ShowDropZoneStatus($"Session loaded: {result.session?.Name}", isError: false);
                App.Logger?.Information("Session imported via drag-drop: {Name}", result.session?.Name);
            }
            else
            {
                ShowDropZoneStatus($"Failed: {result.message}", isError: true);
            }
        }

        private async Task HandleAssetDropAsync(string[] paths)
        {
            try
            {
                _assetImportService ??= new Services.AssetImportService();

                var progress = new Progress<Services.ImportProgress>(p =>
                {
                    // Could update a progress indicator here if needed
                    App.Logger?.Debug("Import progress: {Current}/{Total} - {File}", p.Current, p.Total, p.CurrentFile);
                });

                var result = await Task.Run(() => _assetImportService.ImportAsync(paths, progress));

                // Refresh the asset lists if any were imported
                if (result.ImagesImported > 0)
                {
                    App.Flash?.RefreshImagesPath();
                    RefreshImagesList();
                }

                if (result.VideosImported > 0)
                {
                    App.Video?.RefreshVideosPath();
                    RefreshVideosList();
                }

                RefreshAssetTree();
                ShowTab("assets");

                App.Logger?.Information("Asset import complete: {Summary}", result.GetSummary());
                MessageBox.Show(result.GetSummary(), Loc.Get("title_import_complete"), MessageBoxButton.OK,
                    result.TotalImported > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Asset import failed");
                MessageBox.Show(Loc.GetF("msg_import_failed_0", ex.Message), Loc.Get("title_import_error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshImagesList()
        {
            // The FlashService manages its own file list internally
            // RefreshImagesPath() already clears and reloads the cache
            App.Logger?.Debug("Images list refreshed after import");
        }

        private void RefreshVideosList()
        {
            // The VideoService manages its own file list internally
            // RefreshVideosPath() already clears and reloads the cache
            App.Logger?.Debug("Videos list refreshed after import");
        }

        // Session action button handlers
        internal void SessionBtn_Edit(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sessionId)
            {
                var session = GetSessionById(sessionId);
                if (session == null) return;

                var editor = new SessionEditorWindow(session);
                editor.Owner = this;
                if (editor.ShowDialog() == true && editor.ResultSession != null)
                {
                    if (_sessionFileService == null) _sessionFileService = new Services.SessionFileService();
                    if (_sessionManager == null) InitializeSessionManager();

                    var editedSession = editor.ResultSession;

                    if (session.Source == Models.SessionSource.BuiltIn)
                    {
                        // Editing a built-in session creates a new custom session
                        editedSession.Id = Guid.NewGuid().ToString(); // New ID

                        var dialog = new Microsoft.Win32.SaveFileDialog
                        {
                            Filter = "Session Files (*.session.json)|*.session.json",
                            Title = Loc.Get("title_save_as_new_custom_session"),
                            InitialDirectory = SessionFileService.CustomSessionsFolder,
                            FileName = SessionFileService.GetExportFileName(editedSession)
                        };

                        if (dialog.ShowDialog() == true)
                        {
                            _sessionManager.AddNewSession(editedSession, dialog.FileName);
                            MessageBox.Show(Loc.Get("msg_built_in_session_saved_as_a_new_custom_sessio"), Loc.Get("title_success"), MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else // Custom session
                    {
                        // Preserve original ID, source, and file path to save over existing file
                        editedSession.Id = session.Id;
                        editedSession.Source = session.Source;
                        editedSession.SourceFilePath = session.SourceFilePath;
                        _sessionManager.UpdateCustomSession(editedSession);
                        
                        SelectSession(editedSession);
                        ShowDropZoneStatus($"Session updated: {editedSession.Name}", isError: false);
                    }
                }
            }
            e.Handled = true;
        }

        internal void SessionBtn_Export(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sessionId)
            {
                var session = GetSessionById(sessionId);
                if (session != null)
                {
                    ExportSessionToFile(session);
                }
            }
            e.Handled = true;
        }

        private async void SessionBtn_Share(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn || btn.Tag is not string sessionId) return;
            var session = GetSessionById(sessionId);
            if (session == null) return;
            await ShareSessionToCatalogueAsync(session);
        }

        /// <summary>
        /// Reconcile the Sessions tab with the CustomSessions folder.
        ///
        /// <see cref="Services.SessionManager.LoadAllSessions"/> runs exactly ONCE per launch
        /// (<see cref="InitializeSessionManager"/>), so a .session.json written behind its back -
        /// the Graded Intake's auto-draft above all (IntakeHostService.OnQuizResult) - is invisible
        /// until relaunch whenever its single registration hop fails or never happens. That hop is
        /// best-effort by design (it must never fail the draft), so the list needs a second chance
        /// rather than a stricter hop.
        ///
        /// Cheap, idempotent and event-free: it does nothing unless the folder holds a file the
        /// manager has never seen, it reuses LoadAllSessions + RepaintSessionRack rather
        /// than inventing a second loading path, and it raises NO SessionAdded events - so nothing
        /// downstream (XP, punch card, achievements) can fire twice off a refresh.
        /// </summary>
        private void SyncCustomSessionsFromDisk()
        {
            try
            {
                if (_sessionManager == null) return;

                var folder = SessionFileService.CustomSessionsFolder;
                if (!Directory.Exists(folder)) return;

                var known = new HashSet<string>(
                    _sessionManager.CustomSessions
                        .Select(s => s.SourceFilePath)
                        .Where(p => !string.IsNullOrEmpty(p))
                        .Select(Path.GetFullPath),
                    StringComparer.OrdinalIgnoreCase);

                var onDisk = Directory.GetFiles(folder, "*.session.json");
                if (onDisk.All(f => known.Contains(Path.GetFullPath(f)))) return;

                // Reload drops and rebuilds every Session instance, so re-point the selection at
                // the fresh object or Start Session would run a detached copy.
                var selectedId = _selectedSession?.Id;
                _sessionManager.LoadAllSessions();   // raises SessionsReloaded -> RepaintSessionRack
                if (!string.IsNullOrEmpty(selectedId))
                {
                    var again = _sessionManager.GetSession(selectedId);
                    if (again != null) _selectedSession = again;
                }
                // Repaint AFTER the selection is re-pointed at the fresh instance, so the row
                // that comes back wearing SdSessionRowSelected is the one Start Session will run.
                RepaintSessionRack();

                App.Logger?.Information(
                    "SyncCustomSessionsFromDisk: picked up {N} session file(s) written after startup",
                    onDisk.Length - known.Count);
            }
            catch (Exception ex) { App.Logger?.Debug("SyncCustomSessionsFromDisk: {E}", ex.Message); }
        }

        private void SessionBtn_Delete(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string sessionId)
            {
                var session = GetSessionById(sessionId);
                if (session == null) return;

                // Confirm deletion
                var result = ShowStyledDialog(
                    Loc.Get("title_delete_session"),
                    Loc.GetF("msg_delete_session_confirm_0", session.Name),
                    Loc.Get("btn_delete"), Loc.Get("btn_cancel"));

                if (result && _sessionManager != null)
                {
                    _sessionManager.DeleteSession(session);
                    ShowDropZoneStatus($"Deleted: {session.Name}", isError: false);

                    // Clear selection if this was selected
                    if (_selectedSession?.Id == sessionId)
                    {
                        _selectedSession = null;
                        PresetsTab.TxtDetailTitle.Text = Loc.Get("label_select_a_session");
                        PresetsTab.TxtDetailSubtitle.Text = Loc.Get("label_click_on_a_session_to_see_details");
                    }
                }
            }
            e.Handled = true;
        }

        private Models.Session? GetSessionById(string sessionId)
        {
            // Check session manager first
            if (_sessionManager != null)
            {
                var session = _sessionManager.GetSession(sessionId);
                if (session != null) return session;
            }

            // Fall back to hardcoded sessions
            return Models.Session.GetAllSessions().FirstOrDefault(s => s.Id == sessionId);
        }

        /// <summary>
        /// Bring a session up in the Sessions tab: switch to it, make the session the selected
        /// one, and scroll its card into view. Returns false when the session is not known.
        ///
        /// Exists because <see cref="SelectSession"/> is private and the Graded Intake needs a
        /// safe way to hand the user to the session it just drafted. Re-selecting by id here
        /// rather than trusting the selection left behind by <see cref="OnSessionAdded"/> is the
        /// whole point: the toast that calls this can sit on screen for eight seconds, and if the
        /// user clicks another session card in the meantime, acting on the stale selection would
        /// hand them - or start - the wrong session entirely.
        /// </summary>
        internal bool RevealSessionInLibrary(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return false;

            if (!Dispatcher.CheckAccess())
                return Dispatcher.Invoke(() => RevealSessionInLibrary(sessionId));

            try
            {
                var session = GetSessionById(sessionId);
                if (session == null)
                {
                    App.Logger?.Debug("RevealSessionInLibrary: no session with id {Id}", sessionId);
                    return false;
                }

                ShowTab("presets");
                SelectSession(session);

                // "Reveal" has to actually reveal. The rack is filtered and searched, so the row
                // this call exists to show can legitimately be hidden by a chip the user set an
                // hour ago - and the Graded Intake's toast promising a drafted session, landing on
                // a list that does not contain it, is #614 wearing a different hat. Clear the
                // view (never the data) and repaint before looking for the row.
                if (!HasSessionRackRow(sessionId)) ClearSessionRackFilters();

                // Scroll the matching row into view. Rows carry their session id in Tag; a
                // miss here is cosmetic only, so it never fails the call.
                try
                {
                    var panel = PresetsTab?.SessionRackPanel;
                    if (panel != null)
                    {
                        foreach (var child in panel.Children)
                        {
                            if (child is Border card && (card.Tag as string) == sessionId)
                            {
                                card.BringIntoView();
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("RevealSessionInLibrary: BringIntoView: {E}", ex.Message); }

                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RevealSessionInLibrary failed for {Id}", sessionId);
                return false;
            }
        }

        internal void SessionDropZone_Drop(object sender, DragEventArgs e)
        {
            // Mark handled to prevent Window_Drop from also importing the session
            e.Handled = true;

            // Reset visual state
            PresetsTab.SessionDropZone.BorderBrush = Application.Current.Resources["PanelAccentBrush"] as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(64, 64, 96));
            PresetsTab.DropZoneIcon.Text = "📂";
            PresetsTab.DropZoneIcon.Foreground = new SolidColorBrush(Color.FromRgb(112, 112, 144));

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length != 1) return;

            var filePath = files[0];
            if (!filePath.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
            {
                ShowDropZoneStatus(Loc.Get("msg_only_session_json_files_allowed"), isError: true);
                return;
            }

            // Validate and import
            if (_sessionFileService == null)
            {
                _sessionFileService = new Services.SessionFileService();
            }

            if (!_sessionFileService.ValidateSessionFile(filePath, out var errorMessage))
            {
                ShowDropZoneStatus($"Invalid: {errorMessage}", isError: true);
                return;
            }

            if (_sessionManager == null)
            {
                InitializeSessionManager();
            }

            var result = _sessionManager!.ImportSession(filePath);
            if (result.success)
            {
                ShowDropZoneStatus($"Imported: {result.session?.Name}", isError: false);
                App.Logger?.Information("Session imported via drag-drop: {Name}", result.session?.Name);
            }
            else
            {
                ShowDropZoneStatus($"Failed: {result.message}", isError: true);
            }
        }

        private void ShowDropZoneStatus(string message, bool isError)
        {
            PresetsTab.DropZoneStatus.Text = message;
            PresetsTab.DropZoneStatus.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(255, 100, 100))
                : FindResource("PinkBrush") as SolidColorBrush;
            PresetsTab.DropZoneStatus.Visibility = Visibility.Visible;

            // Auto-hide after 3 seconds
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += (s, e) =>
            {
                PresetsTab.DropZoneStatus.Visibility = Visibility.Collapsed;
                timer.Stop();
            };
            timer.Start();
        }

        internal void BtnExportSession_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSession == null) return;
            ExportSessionToFile(_selectedSession);
        }

        internal void BtnCreateSession_Click(object sender, RoutedEventArgs e)
        {
            var editor = new SessionEditorWindow();
            editor.Owner = this;
            if (editor.ShowDialog() == true && editor.ResultSession != null)
            {
                if (_sessionFileService == null)
                {
                    _sessionFileService = new Services.SessionFileService();
                }

                var session = editor.ResultSession;

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Session Files (*.session.json)|*.session.json",
                    Title = Loc.Get("title_save_new_session"),
                    InitialDirectory = SessionFileService.CustomSessionsFolder,
                    FileName = SessionFileService.GetExportFileName(session)
                };

                if (dialog.ShowDialog() == true)
                {
                    if (_sessionManager == null) InitializeSessionManager();
                    _sessionManager.AddNewSession(session, dialog.FileName);

                    // The OnSessionAdded event will handle UI updates
                    MessageBox.Show(Loc.Get("msg_new_session_saved"), Loc.Get("title_success"), MessageBoxButton.OK, MessageBoxImage.Information);
                    App.Logger?.Information("Session created: {Name} at {Path}", session.Name, dialog.FileName);
                }
            }
        }

        private void SessionContextMenu_Export(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string sessionId)
            {
                var sessions = Models.Session.GetAllSessions();
                var session = sessions.FirstOrDefault(s => s.Id == sessionId);
                if (session != null)
                {
                    ExportSessionToFile(session);
                }
            }
        }

        private void ExportSessionToFile(Models.Session session)
        {
            if (_sessionFileService == null)
            {
                _sessionFileService = new Services.SessionFileService();
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.Get("title_export_session"),
                Filter = "Session files (*.session.json)|*.session.json",
                FileName = Services.SessionFileService.GetExportFileName(session),
                DefaultExt = ".session.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _sessionFileService.ExportSession(session, dialog.FileName);
                    ShowStyledDialog(Loc.Get("title_export_complete"), Loc.GetF("msg_session_exported_to_0", dialog.FileName), "OK", "");
                    App.Logger?.Information("Session exported: {Name} to {Path}", session.Name, dialog.FileName);
                }
                catch (Exception ex)
                {
                    ShowStyledDialog(Loc.Get("title_export_failed"), Loc.GetF("msg_failed_to_export_session_0", ex.Message), "OK", "");
                    App.Logger?.Error(ex, "Failed to export session");
                }
            }
        }

        #endregion
    }
}
