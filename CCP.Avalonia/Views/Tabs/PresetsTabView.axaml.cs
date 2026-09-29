using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/PresetsTabView.xaml.cs.
    ///
    /// The WPF code-behind holds the full preset/session store and feature wiring. This head keeps
    /// those head-owned dependencies out of the view; the built-in Core session rack is the one
    /// honest read-only slice restored here, including pointer and keyboard selection.
    ///
    /// Preset Load / New / Save-over / Delete are wired (WPF MainWindow.Presets.cs:2202-2381).
    /// ponytail: needs JustDropOrdersService and the tab FX clock, wired when those move to
    /// Core. The remaining wiring points, all named in the XAML, are:
    ///   BtnRevealSpoilers / BtnSharePreset /
    ///   BtnSelectCornerGif / ChkCornerGifEnabled / RbCornerTL..BR /
    ///   SliderCornerGifSize + SliderCornerGifOpacity / CmbRackSort.SelectionChanged /
    ///   TxtRackSearch.TextChanged / SessionDropZone (catalogue) /
    ///   preset chip clicks and IsVisibleChanged -> OnPresetsTabVisibilityChanged (the card-sheen
    ///   clock, started on show, dropped on hide).
    ///
    /// Two handlers that look view-only are NOT wired on purpose. SliderCornerGif*_ValueChanged
    /// stamps "{n}px" / "{n}%" into TxtCornerGifSize / TxtCornerGifOpacity, but it also writes
    /// AppSettings, and those two labels carry {loc:Str} - assigning .Text over a live loc binding
    /// is the documented trap that loses the value on the next language change. They come back
    /// with the settings service.
    /// </summary>
    public partial class PresetsTabView : UserControl
    {
        public PresetsTabView()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and Load leaves every one of them permanently null - a silent no-op
            // that compiles, renders and reviews clean.
            InitializeComponent();
            RestoreRackSortSelection();
            CmbRackSort.SelectionChanged += CmbRackSort_SelectionChanged;
            TxtRackSearch.TextChanged += TxtRackSearch_TextChanged;
            BtnExportPreset.Click += BtnExportPreset_Click;
            BtnLoadPreset.Click += BtnLoadPreset_Click;
            BtnSaveOverPreset.Click += BtnSaveOverPreset_Click;
            BtnDeletePreset.Click += BtnDeletePreset_Click;
            BtnNewPreset.PointerReleased += BtnNewPreset_Click;   // WPF MouseLeftButtonUp (PresetsTabView.xaml:720)
            BtnSessionHistory.Click += BtnSessionHistory_Click;
            BtnCreateSession.Click += BtnCreateSession_Click;
            BtnExportSession.Click += (_, _) => { if (_selectedSession is { } s) ExportSession(s); };
            // WPF Window_Drop's Session/Preset cases (MainWindow.SessionIO.cs:1477-1484), scoped to
            // this tab. ponytail: asset/zip/mod drops and the window-wide overlay are still WPF-only.
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DropEvent, Tab_Drop);
            _startSessionLabel = BtnStartSession.Content;
            BtnStartSession.Click += (_, _) =>
                (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.BtnStartSession_Click(_selectedSession);
            TxtDetailTitle.Text = Loc.Get("label_select_a_preset");
            TxtDetailSubtitle.Text = Loc.Get("label_click_on_a_preset_or_session_to_see_details");
            TxtSessionDuration.Text = Loc.Get("label_30_minutes");
            TxtSessionXP.Text = Loc.Get("label_50_xp");
            TxtSessionDifficulty.Text = Loc.Get("label_easy_2");
            SeedRailRackAndTakeaway();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            RefreshLocalizedDetails();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnLanguageChanged(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (VisualRoot is null) return;
                RefreshLocalizedDetails();
            });

        private void RefreshLocalizedDetails()
        {
            RefreshLocalizedRack();

            if (_selectedSession is Session selected)
            {
                SelectSession(selected);
                return;
            }
            if (_selectedPreset is Preset preset)
            {
                SelectPreset(preset);
                return;
            }

            TxtDetailTitle.Text = Loc.Get("label_select_a_preset");
            TxtDetailSubtitle.Text = Loc.Get("label_click_on_a_preset_or_session_to_see_details");
            TxtSessionDuration.Text = Loc.Get("label_30_minutes");
            TxtSessionXP.Text = Loc.Get("label_50_xp");
            TxtSessionDifficulty.Text = Loc.Get("label_easy_2");
        }

        /// <summary>
        /// Refreshes the code-built catalogue in place. Language changes must not rebuild rows:
        /// their Session tags, focused row and either ScrollViewer's offset belong to the live view,
        /// not to a newly loaded catalogue.
        /// </summary>
        private void RefreshLocalizedRack()
        {
            foreach (var row in SessionRackPanel.Children.OfType<Border>())
            {
                if (row.Tag is not Session session || row.Child is not Grid grid) continue;

                foreach (var child in grid.Children)
                {
                    switch (Grid.GetColumn(child))
                    {
                        case 2 when child is TextBlock title:
                            title.Text = SessionName(session);
                            break;
                        case 3 when child is TextBlock description:
                        {
                            var blurb = SessionDescription(session);
                            description.Text = string.IsNullOrWhiteSpace(blurb)
                                ? Loc.Get("label_custom_session")
                                : blurb.Split('\n')[0].Trim();
                            ToolTip.SetTip(description, blurb);
                            break;
                        }
                        case 4 when child is Border difficulty:
                            SetPillText(difficulty, session.GetDifficultyText());
                            break;
                        case 5 when child is TextBlock duration:
                            duration.Text = Loc.GetF("rack_duration", session.DurationMinutes);
                            break;
                        case 6 when child is TextBlock reward:
                            reward.Text = Loc.GetF("rack_xp", session.BonusXP);
                            break;
                        case 7 when child is StackPanel badges:
                            if (badges.Children.OfType<Border>().FirstOrDefault() is Border source)
                                SetPillText(source, Loc.Get(RackSourceKeys(session.Source).labelKey));
                            break;
                        case 8 when child is StackPanel actions:
                            RefreshRowActionTooltips(actions);
                            break;
                    }
                }
            }

            for (var i = 0; i < RackSourceChips.Children.Count; i++)
            {
                if (RackSourceChips.Children[i] is not ToggleButton chip) continue;
                var key = chip.Tag as string ?? "all";
                var count = key switch
                {
                    "builtin" => _availableSessions.Count(session => session.Source == SessionSource.BuiltIn),
                    "yours" => _availableSessions.Count(session => session.Source == SessionSource.Custom),
                    "catalogue" => _availableSessions.Count(session => session.Source == SessionSource.Imported),
                    _ => _availableSessions.Count
                };
                SetTextContent(chip, $"{Loc.Get(key switch
                {
                    "builtin" => "rack_source_builtin",
                    "yours" => "rack_source_yours",
                    "catalogue" => "rack_source_catalogue",
                    _ => "rack_source_all"
                })}  {count}");
            }

            foreach (var dot in RackDifficultyChips.Children.OfType<ToggleButton>())
            {
                if (dot.Tag is not SessionDifficulty difficulty) continue;
                ToolTip.SetTip(dot, Loc.Get($"rack_diff_{difficulty.ToString().ToLowerInvariant()}"));
            }

            if (SessionRackPanel.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock empty)
                empty.Text = Loc.Get("rack_empty");
            UpdateRackCount(SessionRackPanel.Children.OfType<Border>().Count());
            RefreshRackSortSelection();
        }

        /// <summary>Refresh the closed ComboBox face after a language change. Avalonia's default
        /// selection presenter can retain the old visual when an item carries a live TextBlock
        /// binding; reselecting the same item keeps its invariant Tag while letting the presenter
        /// read the localized content. The toolbar guard prevents a persistence echo.</summary>
        private void RefreshRackSortSelection()
        {
            if (CmbRackSort.SelectedItem is not object selected) return;

            _rackToolbarSyncing = true;
            try
            {
                CmbRackSort.SelectedItem = null;
                CmbRackSort.SelectedItem = selected;
            }
            finally
            {
                _rackToolbarSyncing = false;
            }
        }

        private static void SetTextContent(ToggleButton control, string text)
        {
            if (control.Content is TextBlock block)
                block.Text = text;
            else
                control.Content = new TextBlock { Text = text };
        }

        private static void SetPillText(Border pill, string text)
        {
            if (pill.Child is TextBlock block)
                block.Text = text;
        }

        private static void RefreshRowActionTooltips(StackPanel actions)
        {
            var keys = new[] { "tooltip_edit_session", "tooltip_export_session" };
            var buttons = actions.Children.OfType<Button>().ToArray();
            for (var i = 0; i < Math.Min(keys.Length, buttons.Length); i++)
                ToolTip.SetTip(buttons[i], Loc.Get(keys[i]));
        }

        private const string RackSourceAll = "all";
        private const string RackSourceBuiltIn = "builtin";
        private const string RackSourceYours = "yours";
        private const string RackSourceCatalogue = "catalogue";

        private string _rackSourceFilter = InitialRackSourceFilter();
        private string _rackSort = InitialRackSort();
        private string _rackSearch = "";
        private readonly HashSet<SessionDifficulty> _rackDifficulties = new()
        {
            SessionDifficulty.Easy,
            SessionDifficulty.Medium,
            SessionDifficulty.Hard,
            SessionDifficulty.Extreme
        };
        private bool _rackToolbarSyncing;

        private SessionManager? _sessionManager;

        private IReadOnlyList<Session> _availableSessions =
            Session.GetAllSessions().Where(session => session.IsAvailable).ToArray();
        private Session? _selectedSession;

        private static string InitialRackSourceFilter() =>
            CoreSettings.HasProvider ? CoreSettings.Current.SessionRackSourceFilter : RackSourceAll;

        private static string InitialRackSort() =>
            CoreSettings.HasProvider ? CoreSettings.Current.SessionRackSort : "recent";

        /// <summary>Replaces the offline built-in source with an already-loaded manager. Loading
        /// stays in App's desktop composition so constructing a view for render/nav never touches
        /// the user's session folders.</summary>
        internal void UseSessionManager(SessionManager manager)
        {
            ArgumentNullException.ThrowIfNull(manager);
            _sessionManager = manager;
            _availableSessions = manager.AllSessions.Where(session => session.IsAvailable).ToArray();
            _selectedSession = null;
            RackSourceChips.Children.Clear();
            RackDifficultyChips.Children.Clear();
            SessionRackPanel.Children.Clear();
            SeedRackToolbar();
            SeedSessionRack();
            RefreshLocalizedDetails();
        }

        // ---- placeholder furniture + Core-backed session rack --------------------
        //
        // The Takeaway strip remains render furniture until its store moves. The preset rail and
        // the session rack read Core (Preset, AppSettings.UserPresets, Session).

        private void SeedRailRackAndTakeaway()
        {
            RefreshPresetsList();
            SeedRackToolbar();
            SeedSessionRack();
            SeedTakeaway();
        }

        private Preset? _selectedPreset;
        private PresetFileService? _presetFileService;

        /// <summary>WPF's RefreshPresetsList: the built-ins plus AppSettings.UserPresets, inserted
        /// ahead of the fixed "+ New" chip.</summary>
        internal void RefreshPresetsList()
        {
            var presets = Preset.GetDefaultPresets();
            presets.AddRange(CoreSettings.Current.UserPresets);

            for (int i = PresetCardsPanel.Children.Count - 1; i >= 0; i--)
                if (PresetCardsPanel.Children[i] is Border { Tag: string })
                    PresetCardsPanel.Children.RemoveAt(i);

            int at = 0;
            foreach (var preset in presets)
                PresetCardsPanel.Children.Insert(at++, PresetChip(preset));
        }

        /// <summary>WPF's SelectPreset (MainWindow.Presets.cs:461). Share stays disabled: the
        /// preset Share-to-catalogue submission is split out (shell-preset-io).</summary>
        private void SelectPreset(Preset preset)
        {
            _selectedPreset = preset;
            _selectedSession = null;
            RefreshPresetsList();

            PresetDetailScroller.IsVisible = true;
            PresetButtonsPanel.IsVisible = true;
            SessionDetailScroller.IsVisible = false;
            SessionButtonsPanel.IsVisible = false;

            TxtDetailTitle.Text = CoreMods.MakeModAware(preset.Name);
            TxtDetailSubtitle.Text = CoreMods.MakeModAware(preset.Description);
            TxtDetailFlash.Text = preset.FlashEnabled
                ? $"Enabled | {preset.FlashFrequency}/hr | ×{preset.SimultaneousImages} | Opacity: {preset.FlashOpacity}%"
                : "Disabled";
            TxtDetailVideo.Text = preset.MandatoryVideosEnabled
                ? $"Enabled | {preset.VideosPerHour}/hr | Strict: {(preset.StrictLockEnabled ? "Yes" : "No")}"
                : "Disabled";
            TxtDetailSubliminal.Text = preset.SubliminalEnabled
                ? $"Enabled | {preset.SubliminalFrequency}/min | Opacity: {preset.SubliminalOpacity}%"
                : "Disabled";
            TxtDetailAudio.Text = $"Whispers: {(preset.SubAudioEnabled ? $"Yes ({preset.SubAudioVolume}%)" : "No")} | Master: {preset.MasterVolume}%";
            TxtDetailOverlays.Text = $"Spiral: {(preset.SpiralEnabled ? "Yes" : "No")} | Pink: {(preset.PinkFilterEnabled ? "Yes" : "No")}";
            TxtDetailAdvanced.Text = $"Bubbles: {(preset.BubblesEnabled ? "Yes" : "No")} | Lock Card: {(preset.LockCardEnabled ? "Yes" : "No")}";

            BtnLoadPreset.IsEnabled = true;
            BtnSaveOverPreset.IsEnabled = !preset.IsDefault;
            BtnDeletePreset.IsEnabled = !preset.IsDefault;
            BtnExportPreset.IsEnabled = true;
            UpdatePresetShareStatusBadge(preset);
            RefreshSessionRackSelection();
        }

        /// <summary>WPF UpdatePresetShareStatusBadge (MainWindow.PresetIO.cs:332).</summary>
        private void UpdatePresetShareStatusBadge(Preset preset)
        {
            PresetShareStatusHost.Children.Clear();
            var badge = Windows.MainShellWindow.CreateCatalogueStatusBadge(
                Windows.MainShellWindow.GetCatalogueRecord(Windows.MainShellWindow.CatalogueKindPresets, preset.Id));
            if (badge == null) return;
            badge.Margin = new Thickness(0);
            PresetShareStatusHost.Children.Add(badge);
        }

        /// <summary>After a share-status poll: the preset pill and the rack rows (WPF RefreshCatalogueShareBadges).</summary>
        internal void RefreshCatalogueBadges()
        {
            if (_selectedPreset != null) UpdatePresetShareStatusBadge(_selectedPreset);
            RepaintSessionRack();
        }

        // ---- preset CRUD: WPF MainWindow.Presets.cs:2202-2381. The ops are split from their
        // dialogs so a test can drive them; the handlers below are WPF's click flow.

        private Windows.MainShellWindow? Shell => TopLevel.GetTopLevel(this) as Windows.MainShellWindow;

        /// <summary>WPF LoadPreset: refused mid-session (the chokepoint), apply, save, and stop the
        /// running features the preset cleared (#872). Open editors re-seed from settings INPC.
        /// ponytail: WPF keeps the strict flags on mid-Lockdown (LockdownStrictHold); no Lockdown
        /// on this head yet, add it with the Lockdown port.</summary>
        internal bool LoadPreset(Preset preset)
        {
            if (Shell?.RefuseActionIfSessionLocked($"load-preset:{preset.Name}") ?? CoreSession.IsSessionRunning) return false;
            preset.ApplyTo(CoreSettings.Current);
            CoreSettings.Save();
            CoreEngine.Reconcile();
            RefreshPresetsList();
            Serilog.Log.Information("Loaded preset: {Name}", preset.Name);
            return true;
        }

        /// <summary>WPF PromptSaveNewPreset minus the prompt. Null when the name is taken.</summary>
        internal Preset? SaveNewPreset(string name)
        {
            var s = CoreSettings.Current;
            if (Preset.GetDefaultPresets().Concat(s.UserPresets)
                .Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return null;
            var preset = Preset.FromSettings(s, name, "Custom preset created by user");
            s.UserPresets.Add(preset);
            s.CurrentPresetName = name;
            CoreSettings.Save();
            SelectPreset(preset);
            Serilog.Log.Information("Created new preset: {Name}", name);
            return preset;
        }

        /// <summary>WPF BtnSaveOverPreset_Click's write: current settings under the same id.</summary>
        internal Preset? SaveOverPreset(Preset old)
        {
            var s = CoreSettings.Current;
            var index = s.UserPresets.FindIndex(p => p.Id == old.Id);
            if (old.IsDefault || index < 0) return null;
            var updated = Preset.FromSettings(s, old.Name, old.Description);
            updated.Id = old.Id;
            updated.CreatedAt = old.CreatedAt;
            s.UserPresets[index] = updated;
            CoreSettings.Save();
            SelectPreset(updated);
            Serilog.Log.Information("Updated preset: {Name}", updated.Name);
            return updated;
        }

        /// <summary>WPF BtnDeletePreset_Click's write.</summary>
        internal void DeletePreset(Preset preset)
        {
            if (preset.IsDefault) return;
            CoreSettings.Current.UserPresets.RemoveAll(p => p.Id == preset.Id);
            CoreSettings.Save();
            _selectedPreset = null;
            RefreshPresetsList();
            Serilog.Log.Information("Deleted preset");
        }

        private async void BtnLoadPreset_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_selectedPreset is not { } preset || Shell is not { } owner) return;
            // Checked before the confirm too, so the user is never asked to approve a refusal.
            if (owner.RefuseActionIfSessionLocked("load-preset-click")) return;
            if (!await Dialogs.MessageDialog.ConfirmAsync(owner, Loc.Get("title_load_preset"),
                    Loc.GetF("msg_load_preset_confirm_0", PresetNaming.DisplayName(preset)))) return;
            if (LoadPreset(preset))
                await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_preset_loaded"),
                    Loc.GetF("msg_preset_0_loaded", PresetNaming.DisplayName(preset)));
        }

        private async void BtnNewPreset_Click(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            if (Shell is not { } owner) return;
            var dialog = new Dialogs.InputDialog(Loc.Get("title_new_preset"),
                Loc.Get("msg_enter_a_name_for_your_preset"), Loc.Get("label_my_custom_preset"));
            if (await dialog.ShowDialog<bool?>(owner) != true || string.IsNullOrWhiteSpace(dialog.ResultText)) return;
            var name = dialog.ResultText.Trim();
            if (SaveNewPreset(name) == null)
                await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_name_taken"), Loc.Get("msg_a_preset_with_this_name_already_exists"));
            else
                await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_preset_saved"), Loc.GetF("msg_preset_0_saved", name));
        }

        private async void BtnSaveOverPreset_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_selectedPreset is not { IsDefault: false } preset || Shell is not { } owner) return;
            if (!await Dialogs.MessageDialog.ConfirmAsync(owner, Loc.Get("title_overwrite_preset"),
                    Loc.GetF("msg_overwrite_preset_confirm_0", preset.Name))) return;
            if (SaveOverPreset(preset) is { } updated)
                await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_preset_updated"), Loc.GetF("msg_preset_0_updated", updated.Name));
        }

        private async void BtnDeletePreset_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_selectedPreset is not { IsDefault: false } preset || Shell is not { } owner) return;
            if (await Dialogs.MessageDialog.ConfirmAsync(owner, Loc.Get("title_delete_preset"),
                    Loc.GetF("msg_delete_preset_confirm_0", preset.Name)))
                DeletePreset(preset);
        }

        /// <summary>WPF's BtnExportPreset_Click (MainWindow.PresetIO.cs), save picker via StorageProvider.</summary>
        private async void BtnExportPreset_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_selectedPreset == null || TopLevel.GetTopLevel(this) is not { } top) return;
            _presetFileService ??= new PresetFileService();

            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.Get("title_export_preset"),
                SuggestedFileName = PresetFileService.GetExportFileName(_selectedPreset),
                DefaultExtension = ".preset.json",
                FileTypeChoices = new[] { new FilePickerFileType("Preset files") { Patterns = new[] { "*.preset.json" } } },
            });
            if (file?.TryGetLocalPath() is not { } path) return;

            var owner = top as Window;
            try
            {
                _presetFileService.ExportPreset(_selectedPreset, path);
                if (owner != null) _ = Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_export_complete"), Loc.GetF("msg_preset_exported_to_0", path));
                Serilog.Log.Information("Preset exported: {Name} to {Path}", _selectedPreset.Name, path);
            }
            catch (Exception ex)
            {
                if (owner != null) _ = Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_export_failed"), Loc.GetF("msg_failed_to_export_preset_0", ex.Message));
                Serilog.Log.Error(ex, "Failed to export preset");
            }
        }

        /// <summary>WPF's HandlePresetDrop: validate, import, de-dup the id, add to UserPresets,
        /// keep a provenance copy, save, repaint. Called from MainShellWindow's Window_Drop.</summary>
        internal void HandlePresetDrop(string filePath)
        {
            _presetFileService ??= new PresetFileService();

            if (!_presetFileService.ValidatePresetFile(filePath, out var errorMessage))
            {
                ShowDropZoneStatus($"Invalid: {errorMessage}", isError: true);
                return;
            }

            var preset = _presetFileService.ImportPreset(filePath);
            if (preset == null)
            {
                ShowDropZoneStatus("Failed to read preset", isError: true);
                return;
            }

            var settings = CoreSettings.Current;
            var takenIds = Preset.GetDefaultPresets().Select(p => p.Id)
                .Concat(settings.UserPresets.Select(p => p.Id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(preset.Id) || takenIds.Contains(preset.Id))
                preset.Id = Guid.NewGuid().ToString();

            settings.UserPresets.Add(preset);
            try { _presetFileService.CopyToCustomPresets(filePath, preset); }
            catch (Exception ex) { Serilog.Log.Debug("[Preset] CopyToCustomPresets failed: {Error}", ex.Message); }
            CoreSettings.Save();

            RefreshPresetsList();
            ShowDropZoneStatus($"Preset imported: {preset.Name}", isError: false);
            Serilog.Log.Information("Preset imported via drag-drop: {Name}", preset.Name);
        }

        private void ShowDropZoneStatus(string message, bool isError)
        {
            DropZoneStatus.Text = message;
            DropZoneStatus.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(255, 100, 100))
                : this.TryFindResource("PinkBrush", out var pink) ? pink as IBrush : null;
            DropZoneStatus.IsVisible = true;
            DispatcherTimer.RunOnce(() => DropZoneStatus.IsVisible = false, TimeSpan.FromSeconds(3));
        }

        private Border PresetChip(Preset preset)
        {
            var glyphs = (preset.FlashEnabled ? "⚡" : "") + (preset.MandatoryVideosEnabled ? "🎬" : "")
                + (preset.SubliminalEnabled ? "💭" : "") + (preset.SpiralEnabled ? "🌀" : "")
                + (preset.LockCardEnabled ? "🔒" : "");
            var name = CoreMods.MakeModAware(preset.Name);
            var isDefault = preset.IsDefault;
            var selected = _selectedPreset?.Id == preset.Id;

            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // Feature glyphs ahead of the name - the same five the detail pane uses.
            line.Children.Add(new TextBlock
            {
                Text = glyphs,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });

            var nameText = new TextBlock { Text = name, MaxWidth = 150, Theme = TabTheme("SdPresetChipName") };
            ToolTip.SetTip(nameText, CoreMods.MakeModAware(preset.Description));
            line.Children.Add(nameText);

            // DEF / CUSTOM in the RACK's provenance colours: a built-in preset and a built-in
            // session are the same kind of thing, so they wear the same cyan.
            var (tagText, tagSolid, tagWash) = isDefault
                ? (Loc.Get("preset_tag_default"), "SessionSrcBuiltInBrush", "SessionSrcBuiltInWashBrush")
                : (Loc.Get("preset_tag_custom"), "SessionSrcCustomBrush", "SessionSrcCustomWashBrush");
            line.Children.Add(Pill(tagText, tagWash, tagSolid, "SdRackBadgeText", "SdChipTag"));

            var chip = new Border
            {
                Tag = preset.Id,
                Theme = TabTheme(selected ? "SdPresetChipSelected" : "SdPresetChip"),
                Child = line,
            };
            chip.PointerPressed += (_, e) => { SelectPreset(preset); e.Handled = true; };
            return chip;
        }

        /// <summary>Four source chips (single-select) and four difficulty dots (independent).</summary>
        private void SeedRackToolbar()
        {
            // Counts come from the same available Core catalogue as the rows; unavailable
            // placeholders must never make the rack claim that they can be selected.
            var builtIn = _availableSessions.Count(session => session.Source == SessionSource.BuiltIn);
            var custom = _availableSessions.Count(session => session.Source == SessionSource.Custom);
            var imported = _availableSessions.Count(session => session.Source == SessionSource.Imported);

            RackSourceChips.Children.Add(SourceChip("rack_source_all", _availableSessions.Count,
                RackSourceAll, _rackSourceFilter == RackSourceAll));
            RackSourceChips.Children.Add(SourceChip("rack_source_builtin", builtIn,
                RackSourceBuiltIn, _rackSourceFilter == RackSourceBuiltIn));
            RackSourceChips.Children.Add(SourceChip("rack_source_yours", custom,
                RackSourceYours, _rackSourceFilter == RackSourceYours));
            RackSourceChips.Children.Add(SourceChip("rack_source_catalogue", imported,
                RackSourceCatalogue, _rackSourceFilter == RackSourceCatalogue));

            RackDifficultyChips.Children.Add(Dot(SessionDifficulty.Easy, "SessionDiffEasyBrush",
                Loc.Get("rack_diff_easy")));
            RackDifficultyChips.Children.Add(Dot(SessionDifficulty.Medium, "SessionDiffMediumBrush",
                Loc.Get("rack_diff_medium")));
            RackDifficultyChips.Children.Add(Dot(SessionDifficulty.Hard, "SessionDiffHardBrush",
                Loc.Get("rack_diff_hard")));
            RackDifficultyChips.Children.Add(Dot(SessionDifficulty.Extreme, "SessionDiffExtremeBrush",
                Loc.Get("rack_diff_extreme")));

            UpdateRackCount(SessionRackPanel.Children.OfType<Border>().Count());
        }

        private ToggleButton SourceChip(string labelKey, int count, string tag, bool isOn)
        {
            var chip = new ToggleButton
            {
                Theme = TabTheme("SdRackChip"),
                Tag = tag,
                IsChecked = isOn,
                IsEnabled = true,
                // A TextBlock rather than a string Content: the labels are localized words today, but
                // every other button on this page had to opt out of Avalonia's access-key parse and a
                // chip is not the place to discover that a translation gained an underscore.
                Content = new TextBlock { Text = $"{Loc.Get(labelKey)}  {count}" },
            };
            chip.IsCheckedChanged += RackSourceChip_Changed;
            return chip;
        }

        private ToggleButton Dot(SessionDifficulty difficulty, string solidKey, string tip)
        {
            var dot = new ToggleButton
            {
                Theme = TabTheme("SdRackDot"),
                Tag = difficulty,
                IsChecked = _rackDifficulties.Contains(difficulty),
                IsEnabled = true,
                Content = new TextBlock { Text = "●" },
                Foreground = Brush(solidKey),
            };
            ToolTip.SetTip(dot, tip);
            dot.IsCheckedChanged += RackDifficultyChip_Changed;
            return dot;
        }

        private void UpdateRackCount(int shownCount) =>
            TxtRackCount.Text = shownCount == _availableSessions.Count
                ? Loc.GetF("rack_count_all", _availableSessions.Count)
                : Loc.GetF("rack_count_filtered", shownCount, _availableSessions.Count);

        private void RestoreRackSortSelection()
        {
            _rackToolbarSyncing = true;
            try
            {
                var item = CmbRackSort.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(candidate => (candidate.Tag as string) == _rackSort)
                    ?? CmbRackSort.Items.OfType<ComboBoxItem>().FirstOrDefault();
                if (item is not null)
                {
                    CmbRackSort.SelectedItem = item;
                    _rackSort = item.Tag as string ?? "recent";
                }
            }
            finally
            {
                _rackToolbarSyncing = false;
            }
        }

        private bool RackAccepts(Session session) =>
            SessionRackQuery.RackAccepts(session, _rackSourceFilter, _rackDifficulties, _rackSearch);

        private void CmbRackSort_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_rackToolbarSyncing || sender is not ComboBox combo ||
                combo.SelectedItem is not ComboBoxItem item || item.Tag is not string sort ||
                sort == _rackSort) return;

            _rackSort = sort;
            if (CoreSettings.HasProvider)
            {
                CoreSettings.Current.SessionRackSort = sort;
                CoreSettings.Save();
            }

            RepaintSessionRack();
        }

        private void TxtRackSearch_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox search) return;

            var normalized = (search.Text ?? "").Trim();
            if (normalized == _rackSearch) return;
            _rackSearch = normalized;
            RepaintSessionRack();
        }

        private void RackSourceChip_Changed(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_rackToolbarSyncing || sender is not ToggleButton chip) return;

            _rackToolbarSyncing = true;
            try
            {
                // The active source is a strict single-select chip: clicking it again must leave it
                // selected instead of creating a no-source state.
                if (chip.IsChecked != true)
                {
                    chip.IsChecked = true;
                    return;
                }

                foreach (var other in RackSourceChips.Children.OfType<ToggleButton>())
                    other.IsChecked = ReferenceEquals(other, chip);
            }
            finally
            {
                _rackToolbarSyncing = false;
            }

            var key = chip.Tag as string ?? RackSourceAll;
            if (key == _rackSourceFilter) return;
            _rackSourceFilter = key;

            if (CoreSettings.HasProvider)
            {
                CoreSettings.Current.SessionRackSourceFilter = key;
                CoreSettings.Save();
            }

            RepaintSessionRack();
        }

        private void RackDifficultyChip_Changed(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_rackToolbarSyncing || sender is not ToggleButton dot ||
                dot.Tag is not SessionDifficulty difficulty) return;

            if (dot.IsChecked == true) _rackDifficulties.Add(difficulty);
            else _rackDifficulties.Remove(difficulty);
            RepaintSessionRack();
        }

        /// <summary>Rebuild only the filtered rows; details and selected model remain view state.</summary>
        private void RepaintSessionRack()
        {
            SessionRackPanel.Children.Clear();
            SeedSessionRack();
        }

        /// <summary>Build the selectable rows from the available Core session snapshot.</summary>
        private void SeedSessionRack()
        {
            var filtered = _availableSessions.Where(RackAccepts).ToArray();
            var shown = SessionRackQuery.SortRackSessions(filtered, _availableSessions, _rackSort);
            foreach (var session in shown)
                SessionRackPanel.Children.Add(RackRow(session));

            if (shown.Count == 0)
            {
                SessionRackPanel.Children.Add(new TextBlock
                {
                    Theme = TabTheme("SdRackEmpty"),
                    Text = Loc.Get("rack_empty"),
                });
            }

            UpdateRackCount(shown.Count);
            RefreshSessionRackSelection();
        }

        private Border RackRow(Session session)
        {
            var (diffSolid, diffWash) = session.Difficulty switch
            {
                SessionDifficulty.Medium => ("SessionDiffMediumBrush", "SessionDiffMediumWashBrush"),
                SessionDifficulty.Hard => ("SessionDiffHardBrush", "SessionDiffHardWashBrush"),
                SessionDifficulty.Extreme => ("SessionDiffExtremeBrush", "SessionDiffExtremeWashBrush"),
                _ => ("SessionDiffEasyBrush", "SessionDiffEasyWashBrush"),
            };
            var (srcKey, srcSolid, srcWash) = RackSourceKeys(session.Source);
            var icon = string.IsNullOrWhiteSpace(session.Icon) ? "🎬" : session.Icon;
            var name = SessionName(session);
            var blurb = SessionDescription(session);
            var difficulty = session.GetDifficultyText();

            var grid = new Grid
            {
                // stripe | icon | name | blurb* | difficulty | duration | xp | badges | actions
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto,Auto,Auto"),
                ClipToBounds = true,
            };

            // 0. The stripe: full height, full bleed, 4px - the one part of the row you can read
            // at a glance while scrolling.
            var stripe = new Border { Width = 4, VerticalAlignment = VerticalAlignment.Stretch, Background = Brush(diffSolid) };
            Grid.SetColumn(stripe, 0);
            grid.Children.Add(stripe);

            var glyph = new TextBlock { Text = icon, Theme = TabTheme("SdRackIcon"), Margin = new Thickness(7, 0, 0, 0) };
            Grid.SetColumn(glyph, 1);
            grid.Children.Add(glyph);

            // MaxWidth rather than a star column: a long custom name must not push the blurb off
            // the row, and an Auto column will not trim without one.
            var title = new TextBlock { Text = name, MaxWidth = 210, Theme = TabTheme("SdRowTitle"), Margin = new Thickness(7, 0, 0, 0) };
            Grid.SetColumn(title, 2);
            grid.Children.Add(title);

            var rowBlurb = string.IsNullOrWhiteSpace(blurb) ? Loc.Get("label_custom_session") : blurb.Split('\n')[0].Trim();
            var desc = new TextBlock { Text = rowBlurb, Theme = TabTheme("SdRowBlurb") };
            ToolTip.SetTip(desc, blurb);
            Grid.SetColumn(desc, 3);
            grid.Children.Add(desc);

            var diffPill = Pill(difficulty, diffWash, diffSolid);
            Grid.SetColumn(diffPill, 4);
            grid.Children.Add(diffPill);

            var duration = Meta(Loc.GetF("rack_duration", session.DurationMinutes), 56);
            Grid.SetColumn(duration, 5);
            grid.Children.Add(duration);

            var reward = Meta(Loc.GetF("rack_xp", session.BonusXP), 66);
            Grid.SetColumn(reward, 6);
            grid.Children.Add(reward);

            var badges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            badges.Children.Add(Pill(Loc.Get(srcKey), srcWash, srcSolid, "SdRackBadgeText"));
            var sessRec = string.IsNullOrEmpty(session.SourceFilePath) ? null : Windows.MainShellWindow.GetCatalogueRecord(
                Windows.MainShellWindow.CatalogueKindSessions, Windows.MainShellWindow.CanonicalCataloguePathKey(session.SourceFilePath));
            if (Windows.MainShellWindow.CreateCatalogueStatusBadge(sessRec) is { } statusBadge) badges.Children.Add(statusBadge);
            Grid.SetColumn(badges, 7);
            grid.Children.Add(badges);

            // Edit, export and delete are live (WPF SessionBtn_Edit/_Export/_Delete).
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var edit = RowAction("✎", Loc.Get("tooltip_edit_session"), danger: false);
            edit.IsEnabled = true;
            edit.Click += (_, e) => { e.Handled = true; EditSession(session); };
            actions.Children.Add(edit);
            var export = RowAction("↗", Loc.Get("tooltip_export_session"), danger: false);
            export.IsEnabled = true;
            export.Click += (_, e) => { e.Handled = true; ExportSession(session); };
            actions.Children.Add(export);
            // WPF SessionIO.cs:453-457: delete only where SessionManager.DeleteSession can succeed.
            // ponytail: the share (☁) button needs the catalogue submission write path on this head.
            if (session.Source != SessionSource.BuiltIn)
            {
                var delete = RowAction("\U0001F5D1", Loc.Get("tooltip_delete_session"), danger: true);
                delete.IsEnabled = true;
                delete.Click += (_, e) => { e.Handled = true; ConfirmDeleteSession(session); };
                actions.Children.Add(delete);
            }
            // Pad out to four buttons' worth (28px wide, 3px margin) so the existing row columns
            // keep their layout if custom rows are restored later.
            actions.Margin = new Thickness(8 + (4 - actions.Children.Count) * 31, 0, 4, 0);
            Grid.SetColumn(actions, 8);
            grid.Children.Add(actions);

            var row = new Border
            {
                Name = $"SessionRow_{session.Id}",
                Tag = session,
                Theme = TabTheme(_selectedSession?.Id == session.Id ? "SdSessionRowSelected" : "SdSessionRow"),
                Focusable = true,
                IsTabStop = true,
                Child = grid,
            };
            row.PointerPressed += SessionRow_PointerPressed;
            row.KeyDown += SessionRow_KeyDown;
            return row;
        }

        private Button RowAction(string glyph, string tip, bool danger)
        {
            var btn = new Button
            {
                Theme = TabTheme(danger ? "SdRowActionDanger" : "SdRowAction"),
                Content = new TextBlock { Text = glyph },
                IsEnabled = false,
            };
            ToolTip.SetTip(btn, tip);
            return btn;
        }

        private static (string labelKey, string solidKey, string washKey) RackSourceKeys(SessionSource source) =>
            source switch
            {
                SessionSource.Custom => ("rack_src_yours", "SessionSrcCustomBrush", "SessionSrcCustomWashBrush"),
                SessionSource.Imported => ("rack_src_catalogue", "SessionSrcImportedBrush", "SessionSrcImportedWashBrush"),
                _ => ("rack_src_builtin", "SessionSrcBuiltInBrush", "SessionSrcBuiltInWashBrush")
            };

        private static string SessionName(Session session) =>
            LocalizedOrFallback(session.LocalizedName, $"session_{session.Id}_name", session.GetModeAwareName());

        private static string SessionDescription(Session session) =>
            LocalizedOrFallback(session.LocalizedDescription, $"session_{session.Id}_desc", session.GetModeAwareDescription());

        private static string LocalizedOrFallback(string localized, string key, string fallback) =>
            string.IsNullOrWhiteSpace(localized) || string.Equals(localized, key, StringComparison.Ordinal)
                ? fallback
                : CoreMods.MakeModAware(localized);

        private void SessionRow_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border row || !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                return;
            if (row.Tag is not Session session || !session.IsAvailable) return;

            row.Focus();
            SelectSession(session);
            e.Handled = true;
        }

        private void SessionRow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Enter or Key.Space) ||
                sender is not Border { Tag: Session session } || !session.IsAvailable)
                return;

            SelectSession(session);
            e.Handled = true;
        }

        private void SelectSession(Session session)
        {
            if (!session.IsAvailable) return;

            _selectedSession = session;
            _selectedPreset = null;
            RefreshPresetsList();
            PresetDetailScroller.IsVisible = false;
            PresetButtonsPanel.IsVisible = false;
            SessionDetailScroller.IsVisible = true;
            SessionButtonsPanel.IsVisible = true;   // WPF SessionIO.cs:923/:971
            BtnStartSession.IsEnabled = true;
            BtnExportSession.IsEnabled = true;   // WPF SessionIO.cs:973
            SessionSpoilerPanel.IsVisible = false;
            CornerGifOptionPanel.IsVisible = false;

            TxtDetailTitle.Text = $"{(string.IsNullOrWhiteSpace(session.Icon) ? "🎬" : session.Icon)} {SessionName(session)}";
            TxtDetailSubtitle.Text = session.GenerateFeatureDescription();
            TxtSessionDuration.Text = Loc.GetF("rack_duration", session.DurationMinutes);
            TxtSessionXP.Text = Loc.GetF("rack_xp", session.BonusXP);
            TxtSessionDifficulty.Text = session.GetDifficultyText();
            TxtSessionDescription.Text = SessionDescription(session);

            RefreshSessionRackSelection();
        }

        private readonly object? _startSessionLabel;

        /// <summary>The session button's text while a session runs (WPF sets its Content per tick);
        /// null puts the localised Start label back. A TextBlock, so no '_' becomes an access key.</summary>
        internal void SetSessionButtonLabel(string? text) =>
            BtnStartSession.Content = text is null ? _startSessionLabel : new TextBlock { Text = text };

        private void RefreshSessionRackSelection()
        {
            var selectedStyle = TabTheme("SdSessionRowSelected");
            var normalStyle = TabTheme("SdSessionRow");
            if (selectedStyle is null || normalStyle is null) return;

            foreach (var row in SessionRackPanel.Children.OfType<Border>())
            {
                var selected = row.Tag is Session session && session.Id == _selectedSession?.Id;
                row.Theme = selected ? selectedStyle : normalStyle;
            }
        }

        /// <summary>WPF MainWindow.Presets.cs BtnSessionHistory_Click.</summary>
        private void BtnSessionHistory_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            try { _ = new Windows.SessionLogHistoryWindow().ShowDialog(owner); }
            catch (Exception ex) { Serilog.Log.Error(ex, "Failed to open session history dialog"); }
        }

        /// <summary>WPF MainWindow.SessionIO.cs BtnCreateSession_Click: editor, then save-as into
        /// CustomSessions and register with SessionManager.</summary>
        private async void BtnCreateSession_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var editor = new Windows.SessionEditorWindow((TimelineSession?)null);
            if (await editor.ShowDialog<bool?>(owner) != true || editor.ResultSession is not { } session) return;
            if (await PickSessionSavePath(owner, "title_save_new_session", session) is not { } path) return;

            try
            {
                var lib = SessionLibrary();
                lib.AddNewSession(session, path);
                UseSessionManager(lib);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to save session {Path}", path);
                await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_error"), ex.Message);
                return;
            }
            await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_success"), Loc.Get("msg_new_session_saved"));
            Serilog.Log.Information("Session created: {Name} at {Path}", session.Name, path);
        }

        /// <summary>WPF MainWindow.SessionIO.cs SessionBtn_Edit: a built-in becomes a new custom
        /// session saved where the user picks; a custom session is saved over its own file.</summary>
        private async void EditSession(Session session)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var editor = new Windows.SessionEditorWindow(TimelineSession.FromSession(session));
            if (await editor.ShowDialog<bool?>(owner) != true || editor.ResultSession is not { } edited) return;

            if (session.Source == SessionSource.BuiltIn)
            {
                edited.Id = Guid.NewGuid().ToString();
                if (await PickSessionSavePath(owner, "title_save_as_new_custom_session", edited) is not { } path) return;
                try
                {
                    var lib = SessionLibrary();
                    lib.AddNewSession(edited, path);
                    UseSessionManager(lib);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "Failed to save session {Path}", path);
                    await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_error"), ex.Message);
                    return;
                }
                await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_success"),
                    Loc.Get("msg_built_in_session_saved_as_a_new_custom_sessio"));
            }
            else
            {
                edited.Id = session.Id;
                edited.Source = session.Source;
                edited.SourceFilePath = session.SourceFilePath;
                try
                {
                    var lib = SessionLibrary();
                    lib.UpdateCustomSession(edited);
                    UseSessionManager(lib);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "Failed to save session {Path}", edited.SourceFilePath);
                    await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_error"), ex.Message);
                    return;
                }
                SelectSession(edited);
                ShowDropZoneStatus($"Session updated: {edited.Name}", isError: false);
            }
        }

        /// <summary>WPF SessionBtn_Export / BtnExportSession_Click -> ExportSessionToFile.</summary>
        private async void ExportSession(Session session)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.Get("title_export_session"),
                SuggestedFileName = SessionFileService.GetExportFileName(session),
                DefaultExtension = ".session.json",
                FileTypeChoices = new[] { new FilePickerFileType("Session files") { Patterns = new[] { "*.session.json" } } },
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            try
            {
                new SessionFileService().ExportSession(session, path);
                await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_export_complete"), Loc.GetF("msg_session_exported_to_0", path));
                Serilog.Log.Information("Session exported: {Name} to {Path}", session.Name, path);
            }
            catch (Exception ex)
            {
                await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("title_export_failed"), Loc.GetF("msg_failed_to_export_session_0", ex.Message));
                Serilog.Log.Error(ex, "Failed to export session");
            }
        }

        /// <summary>WPF SessionBtn_Delete: styled confirm, then DeleteSession.</summary>
        private async void ConfirmDeleteSession(Session session)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            if (await Dialogs.MessageDialog.ConfirmAsync(owner, Loc.Get("title_delete_session"),
                    Loc.GetF("msg_delete_session_confirm_0", session.Name)))
                DeleteSession(session);
        }

        internal void DeleteSession(Session session)
        {
            var lib = SessionLibrary();
            if (!lib.DeleteSession(lib.GetSession(session.Id) ?? session)) return;
            var wasSelected = _selectedSession?.Id == session.Id;
            var keep = wasSelected ? null : _selectedSession;
            UseSessionManager(lib);
            if (keep != null && lib.GetSession(keep.Id) is { } again) SelectSession(again);
            if (wasSelected)
            {
                TxtDetailTitle.Text = Loc.Get("label_select_a_session");
                TxtDetailSubtitle.Text = Loc.Get("label_click_on_a_session_to_see_details");
            }
            ShowDropZoneStatus($"Deleted: {session.Name}", isError: false);
        }

        /// <summary>WPF HandleSessionDrop: validate, import into CustomSessions, repaint.</summary>
        internal void HandleSessionDrop(string filePath)
        {
            if (!new SessionFileService().ValidateSessionFile(filePath, out var errorMessage))
            {
                ShowDropZoneStatus($"Invalid: {errorMessage}", isError: true);
                return;
            }
            var lib = SessionLibrary();
            var (success, message, session) = lib.ImportSession(filePath);
            if (!success) { ShowDropZoneStatus($"Failed: {message}", isError: true); return; }
            UseSessionManager(lib);
            ShowDropZoneStatus($"Session loaded: {session?.Name}", isError: false);
            Serilog.Log.Information("Session imported via drag-drop: {Name}", session?.Name);
        }

        private void Tab_Drop(object? sender, DragEventArgs e)
        {
            if (e.DataTransfer.TryGetFiles() is not { } files || files.Length != 1 ||
                files[0].TryGetLocalPath() is not { } path) return;
            if (path.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase)) HandleSessionDrop(path);
            else if (path.EndsWith(".preset.json", StringComparison.OrdinalIgnoreCase)) HandlePresetDrop(path);
            else return;
            e.Handled = true;
        }

        /// <summary>WPF's InitializeSessionManager on first use, for a view mounted without one.</summary>
        private SessionManager SessionLibrary()
        {
            if (_sessionManager is null)
            {
                _sessionManager = new SessionManager();
                _sessionManager.LoadAllSessions();
            }
            return _sessionManager;
        }

        private static async System.Threading.Tasks.Task<string?> PickSessionSavePath(Window owner, string titleKey, Session session)
        {
            var folder = await owner.StorageProvider.TryGetFolderFromPathAsync(SessionFileService.CustomSessionsFolder);
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.Get(titleKey),
                SuggestedStartLocation = folder,
                SuggestedFileName = SessionFileService.GetExportFileName(session),
                DefaultExtension = ".session.json",
                FileTypeChoices = new[] { new FilePickerFileType("Session Files") { Patterns = new[] { "*.session.json" } } },
            });
            return file?.TryGetLocalPath();
        }

        /// <summary>Three pinned receipts, the "+n more" toggle, the shop door, and three tray
        /// rows behind it. The tray host stays collapsed, as PaintTakeawayShelf leaves it.</summary>
        private void SeedTakeaway()
        {
            // PaintTakeawayShelf pins up to three receipts, then the "+n more" toggle, then
            // the door. ONE receipt here: the strip never wraps and never scrolls sideways,
            // and at the render proof's 1100px the fill is ~330px, so a second receipt would
            // push the door off the clip and leave its ControlTheme unproven. With three or
            // fewer orders there is no overflow, so the toggle (SdTakeawayChipAccent, a
            // two-setter Border variant of the chip below it) is correctly absent too.
            TakeawayShelf.Children.Add(TakeawayChip("Slow Sink", 30, "AUG 09"));
            TakeawayShelf.Children.Add(DoorChip());

            // The tray renders EVERY order the drawer returned, not just the pinned ones.
            TakeawayTray.Children.Add(TrayRow("Velvet Hour", 45, "AUG 12", Loc.Get("takeaway_today")));
            TakeawayTray.Children.Add(TrayRow("Slow Sink", 30, "AUG 09", Loc.GetF("takeaway_days_ago", 3)));
            TakeawayTray.Children.Add(TrayRow("Static Bloom", 20, "JUL 28", Loc.GetF("takeaway_days_ago", 15)));

            TxtTakeawayCount.Text = Loc.GetF("sd_takeaway_kept", 3);
        }

        private Border TakeawayChip(string name, int minutes, string date)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(new TextBlock
            {
                Text = "📦",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            line.Children.Add(new TextBlock { Text = name, MaxWidth = 120, Theme = TabTheme("SdTakeawayChipTitle") });
            line.Children.Add(new TextBlock { Text = Loc.GetF("sd_takeaway_meta", minutes, date), Theme = TabTheme("SdTakeawayChipMeta") });

            // The copy element sits INSIDE the chip; its handler marks the click handled, or
            // copying a link would also start playing the drop.
            var copy = new Border
            {
                Theme = TabTheme("SdTakeawayCopy"),
                Child = new TextBlock
                {
                    Text = "🔗",
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            ToolTip.SetTip(copy, Loc.Get("tooltip_takeaway_copy_link"));
            line.Children.Add(copy);

            var chip = new Border { Theme = TabTheme("SdTakeawayChip"), Child = line };
            ToolTip.SetTip(chip, Loc.Get("tooltip_takeaway_replay"));
            return chip;
        }

        private Border DoorChip()
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(new TextBlock
            {
                Text = "+",
                Foreground = Brush("PinkBrush"),
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            line.Children.Add(new TextBlock
            {
                Text = Loc.Get("sd_takeaway_order"),
                Foreground = Brush("PinkBrush"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var chip = new Border { Theme = TabTheme("SdTakeawayChipDoor"), Child = line };
            ToolTip.SetTip(chip, Loc.Get("tooltip_takeaway_order_drop"));
            return chip;
        }

        private Border TrayRow(string name, int minutes, string date, string age)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto") };

            var box = new TextBlock
            {
                Text = "📦",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0),
            };
            Grid.SetColumn(box, 0);
            grid.Children.Add(box);

            var title = new TextBlock { Text = name, Theme = TabTheme("SdTakeawayRowTitle") };
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            var mins = new TextBlock { Text = Loc.GetF("takeaway_row_min", minutes), MinWidth = 58, Theme = TabTheme("SdTakeawayRowMeta") };
            Grid.SetColumn(mins, 2);
            grid.Children.Add(mins);

            var when = new TextBlock { Text = date, MinWidth = 62, Theme = TabTheme("SdTakeawayRowMeta") };
            Grid.SetColumn(when, 3);
            grid.Children.Add(when);

            var howLong = new TextBlock { Text = age, MinWidth = 78, Theme = TabTheme("SdTakeawayRowAge") };
            Grid.SetColumn(howLong, 4);
            grid.Children.Add(howLong);

            return new Border { Theme = TabTheme("SdTakeawayRow"), Child = grid };
        }

        // ---- shared shapes (MakeRackPill / MakeRackMeta) ---------------------------

        /// <summary>Solid foreground on its 13% wash sibling. Background is a local value on
        /// purpose: SdPill deliberately sets none, so every pill can carry its own meaning colour.</summary>
        private Border Pill(string text, string washKey, string solidKey,
                            string textThemeKey = "SdPillText", string pillThemeKey = "SdPill") => new()
        {
            Theme = TabTheme(pillThemeKey),
            Background = Brush(washKey),
            Child = new TextBlock { Text = text, Theme = TabTheme(textThemeKey), Foreground = Brush(solidKey) },
        };

        /// <summary>Duration / reward cell. MinWidth is what turns them into columns.</summary>
        private TextBlock Meta(string text, double minWidth) =>
            new() { Text = text, MinWidth = minWidth, Theme = TabTheme("SdRackMeta") };

        // ---- resource lookup ------------------------------------------------------

        /// <summary>
        /// The twin of MainWindow.TryFindTabStyle: this page's card vocabulary lives in the view's
        /// OWN dictionary, not in Theme/, so it is reached from the view rather than from above.
        /// </summary>
        private ControlTheme? TabTheme(string key) =>
            Resources.TryGetResource(key, null, out var value) ? value as ControlTheme : null;

        /// <summary>
        /// The provenance and difficulty families live in Theme/Brushes.xaml, so these come from
        /// the APP dictionary the way SetResourceReference reaches them on WPF.
        ///
        /// Application, not <c>this</c>: resource lookup on a StyledElement walks its logical
        /// parents, and these are built from the constructor, before the view is attached to a
        /// tree - so `this.TryFindResource` finds nothing and every pill, badge and difficulty
        /// stripe renders with a null brush, which draws as invisible rather than as an error.
        /// </summary>
        private static IBrush? Brush(string key) =>
            Application.Current is { } app && app.TryFindResource(key, out var value) ? value as IBrush : null;
    }
}
