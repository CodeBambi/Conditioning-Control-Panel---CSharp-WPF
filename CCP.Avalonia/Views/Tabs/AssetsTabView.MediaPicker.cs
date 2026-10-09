using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// LIBRARY: the media source picker and the Folders chip, PORTED from WPF 7.1.5
    /// MainWindow.Assets.cs ("Media Source Picker" region, nav polish wave 3) and
    /// AssetsTabView.xaml.cs (Folders zone). One decision visible at a time: source as a segmented
    /// bar, flavour as six cards (Core <see cref="FlavourPresets"/>), everything finer behind one
    /// Fine-tune row.
    ///
    /// <para>THERE IS NO TierGate HERE, AND THAT IS THE DESIGN (owner, 2026-08-12): remote media as
    /// an ASSET SOURCE is free for everyone; only the For You feed is premium.</para>
    ///
    /// <para>The consent gate is ASYNC on this head (MessageDialog), so the source write happens
    /// only after the answer lands: the chips are repainted back to the live source while the
    /// question is open and on a "no". A switch resets every rotation (Core
    /// <see cref="FypOnlineCoordinator.ResetAllChannels"/>). ponytail: WPF also calls
    /// InvalidateAssetPoolsAfterSelectionChange; the asset pools are not on this head yet, and no
    /// media service here reads MediaSource, so the write is the whole of it for now.</para>
    /// </summary>
    public partial class AssetsTabView
    {
        private const string MediaSrcLocal = "local";
        private const string MediaSrcOnline = "online";
        private const string MediaSrcMixed = "mixed";

        /// <summary>Same cap the FYP popover host enforces (Take(20)).</summary>
        private const int RemoteCustomSubCap = 20;

        /// <summary>The Pink of the app, used for a niche no flavour owns.</summary>
        private static readonly Color RemoteNicheDefaultTint = Color.FromRgb(0xFF, 0x69, 0xB4);

        private bool _pickerWired;
        /// <summary>Set while the picker writes settings INTO its controls, so the change handlers
        /// can tell a user click from an echo of their own last paint.</summary>
        private bool _pickerSyncing;
        private bool _consentAsking;
        private readonly List<ToggleButton> _sourceChips = new();
        private readonly List<ToggleButton> _nicheChips = new();
        private readonly List<Button> _flavourTiles = new();
        /// <summary>The selection from before the first flavour click this session (Mine brings it back).</summary>
        private List<string>? _mineNiches, _mineSubs;
        /// <summary>The sub being probed right now, or null.</summary>
        private string? _subPending;
        /// <summary>Session-only: the block opens calm every time the app starts.</summary>
        private bool _fineTuneOpen;

        /// <summary>Called from the constructor: builds the picker once and follows visibility.</summary>
        private void InitializeLibraryPicker()
        {
            PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty || !IsVisible) return;
                PaintAssetsFolder();
                RefreshRemoteMediaPicker();
                // Library > Folders: the shell sets CurrentTab after the panel shows, so look once
                // the navigation has finished.
                Dispatcher.UIThread.Post(() =>
                {
                    if ((TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.CurrentTab == "folders")
                        ScrollToZone("folders");
                });
            };
            try
            {
                BuildSourceChips();
                BuildFlavourTiles();
                BuildNicheChips();
                SliderRemoteRatio.ValueChanged += SliderRemoteRatio_Changed;
                BtnRemoteAddSub.Click += (_, _) => AddRemoteCustomSub();
                TxtRemoteCustomSub.KeyDown += (_, e) =>
                {
                    if (e.Key != Key.Enter && e.Key != Key.Return) return;
                    e.Handled = true;
                    AddRemoteCustomSub();
                };
                BtnRemoteFineTune.Click += (_, _) => SetFineTuneOpen(!_fineTuneOpen);
                _pickerWired = true;
                PaintAssetsFolder();
                RefreshRemoteMediaPicker();
            }
            catch (Exception ex) { Log.Warning(ex, "Remote media picker init failed"); }
        }

        private static string LocOr(string key, string english)
        {
            var t = Loc.Get(key);
            return string.IsNullOrWhiteSpace(t) || string.Equals(t, key, StringComparison.Ordinal) ? english : t;
        }

        // =====================================================================================
        //  LIBRARY > FOLDERS
        // =====================================================================================

        /// <summary>Zone keys the Library section strip reaches (WPF nav rework).</summary>
        public static readonly string[] ZoneKeys = { "folders" };

        /// <summary>
        /// "folders": the page does not scroll (fixed rows), so this paints the current folder and
        /// glows the chip and the preset picker once, in the section's sage (skipped at Off).
        /// Any other key does nothing.
        /// </summary>
        public void ScrollToZone(string zone)
        {
            if (!string.Equals((zone ?? "").Trim(), "folders", StringComparison.OrdinalIgnoreCase)) return;
            PaintAssetsFolder();
            if (global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level == MotionLevel.Off) return;
            var sage = Color.FromUInt32(NavStripRules.Accent(NavSections.Library));
            GlowOnce(ZoneFolders, sage);
            GlowOnce(CmbAssetPresets, sage);
        }

        /// <summary>A 2 s ring in the section hue that fades out (WPF NavGlow.Once stand-in).</summary>
        private static void GlowOnce(Control target, Color hue)
        {
            if (target is not TemplatedControl and not Border) return;
            var ring = new BoxShadows(new BoxShadow { Blur = 16, Spread = 2, Color = Color.FromArgb(0xCC, hue.R, hue.G, hue.B) });
            if (target is Border b)
            {
                var was = b.BorderBrush;
                b.BoxShadow = ring;
                b.BorderBrush = new SolidColorBrush(hue);
                DispatcherTimer.RunOnce(() => { b.BoxShadow = default; b.BorderBrush = was; }, TimeSpan.FromSeconds(2));
            }
            else if (target is TemplatedControl t)
            {
                var was = t.BorderBrush;
                t.BorderBrush = new SolidColorBrush(hue);
                DispatcherTimer.RunOnce(() => t.BorderBrush = was, TimeSpan.FromSeconds(2));
            }
        }

        /// <summary>The folder the app reads today, as one line; the full path is the tooltip.</summary>
        internal void PaintAssetsFolder()
        {
            try
            {
                var path = CorePaths.EffectiveAssets ?? "";
                TxtAssetsFolderPath.Text = path;
                ToolTip.SetTip(TxtAssetsFolderPath, string.IsNullOrEmpty(path) ? null : path);
            }
            catch (Exception ex) { Log.Debug("PaintAssetsFolder: {E}", ex.Message); }
        }

        private async void BtnPickAssetsFolder_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (TopLevel.GetTopLevel(this) is Windows.MainShellWindow shell) await shell.RequestPickAssetsFolder();
            }
            catch (Exception ex) { Log.Warning(ex, "Picking the assets folder failed"); }
            PaintAssetsFolder();
            RefreshAssetBrowser();   // a new folder is a new tree
        }

        // =====================================================================================
        //  build
        // =====================================================================================

        /// <summary>Own assets / Scrolller / Both, the segments of one rounded bar. Radio behaviour
        /// is enforced in <see cref="SourceChip_Changed"/>.</summary>
        private void BuildSourceChips()
        {
            if (RemoteSourceChips.Children.Count > 0) return;
            var theme = this.FindResource("RemoteSourceSegment") as ControlTheme;
            void Add(string key, string locKey, string english, string tipKey, string tipEnglish)
            {
                var chip = new ToggleButton
                {
                    Theme = theme,
                    Tag = key,
                    Content = new TextBlock { Text = LocOr(locKey, english) },
                };
                ToolTip.SetTip(chip, LocOr(tipKey, tipEnglish));
                chip.IsCheckedChanged += SourceChip_Changed;
                _sourceChips.Add(chip);
                RemoteSourceChips.Children.Add(chip);
            }
            Add(MediaSrcLocal, "label_media_source_own", "My own assets",
                "tooltip_media_source_own", "Only the images and videos in your assets folder.");
            Add(MediaSrcOnline, "label_media_source_online", "Reddit",
                "tooltip_media_source_online", "Stream everything from the subreddits you pick. Nothing is saved to your disk.");
            Add(MediaSrcMixed, "label_media_source_both", "Both",
                "tooltip_media_source_both", "Blend your own media with Reddit, in whatever proportion you like.");
        }

        /// <summary>Six tiles: the five flavours plus Mine. Built once; painted on every refresh.</summary>
        private void BuildFlavourTiles()
        {
            if (RemoteFlavourTiles.Children.Count > 0) return;
            var theme = this.FindResource("RemoteFlavourTile") as ControlTheme;
            foreach (var f in FlavourPresets.All.Append(FlavourPresets.Mine))
            {
                var flavour = f;
                var tile = CreateFlavourTile(f, theme);
                tile.Click += (_, _) => ApplyFlavour(flavour);
                _flavourTiles.Add(tile);
                RemoteFlavourTiles.Children.Add(tile);
            }
        }

        /// <summary>Niche rows straight off the shared catalog. They edit <c>FypOnlineNiches</c>, the
        /// SAME selection the For You feed uses: one taxonomy, one selection, two surfaces.</summary>
        private void BuildNicheChips()
        {
            if (RemoteNicheChips.Children.Count > 0) return;
            var theme = this.FindResource("RemoteNichePill") as ControlTheme;
            foreach (var niche in FypOnlineCoordinator.Catalog)
            {
                var chip = CreateNicheChip(niche, theme);
                chip.IsCheckedChanged += NicheChip_Changed;
                _nicheChips.Add(chip);
                RemoteNicheChips.Children.Add(chip);
            }
        }

        // =====================================================================================
        //  paint
        // =====================================================================================

        /// <summary>Pushes live settings into every control in the block. Safe on every visit.</summary>
        internal void RefreshRemoteMediaPicker()
        {
            if (!_pickerWired) return;
            var settings = CoreSettings.Current;
            _pickerSyncing = true;
            try
            {
                var source = settings.MediaSource;
                foreach (var chip in _sourceChips)
                    chip.IsChecked = string.Equals(chip.Tag as string, source, StringComparison.Ordinal);

                SliderRemoteRatio.Value = settings.RemoteMediaRatio;
                TxtRemoteRatio.Text = $"{settings.RemoteMediaRatio}%";
                RemoteRatioRow.IsVisible = source == MediaSrcMixed;
                // Folded away while the app runs on local files only.
                RemoteMediaDetails.IsVisible = source != MediaSrcLocal;

                var selected = new HashSet<string>(settings.FypOnlineNiches ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var chip in _nicheChips)
                    chip.IsChecked = chip.Tag is string id && selected.Contains(id);

                PaintPickerChrome(settings);
                RebuildCustomSubRows(settings);
                SetFineTuneOpen(_fineTuneOpen);
            }
            catch (Exception ex) { Log.Warning(ex, "Remote media picker refresh failed"); }
            finally { _pickerSyncing = false; }
        }

        /// <summary>Opens or closes the Fine-tune list. The browser steps aside while it is open:
        /// one decision at a time, and the only way the list fits a page that never scrolls.</summary>
        private void SetFineTuneOpen(bool open)
        {
            _fineTuneOpen = open;
            RemoteFineTuneScroll.IsVisible = open;
            AssetBrowserSection.IsVisible = !open;
            TxtRemoteFineTuneChevron.Text = open ? "▴" : "▾";
        }

        /// <summary>Niche switch colours, the lit flavour tile and the summary line.</summary>
        private void PaintPickerChrome(AppSettings settings)
        {
            foreach (var chip in _nicheChips) PaintNicheChip(chip);
            PaintFlavourTiles(_flavourTiles, settings.FypOnlineNiches, settings.FypOnlineCustomSubs);
            TxtRemoteSummary.Text = SummaryText(settings.FypOnlineNiches, settings.FypOnlineCustomSubs);
        }

        // =====================================================================================
        //  source, ratio, niches
        // =====================================================================================

        private async void SourceChip_Changed(object? sender, RoutedEventArgs e)
        {
            if (_pickerSyncing || _consentAsking) return;
            try
            {
                if (sender is not ToggleButton chip || chip.Tag is not string key) return;
                var settings = CoreSettings.Current;

                // Un-clicking the live chip would leave the app with no source at all.
                if (chip.IsChecked != true)
                {
                    if (key == settings.MediaSource) { _pickerSyncing = true; chip.IsChecked = true; _pickerSyncing = false; }
                    return;
                }
                if (key == settings.MediaSource) return;

                // Anything but "local" starts fetching third-party content, so it is asked for once,
                // and never again of someone who already said yes to the For You feed's card.
                if (key != MediaSrcLocal && !settings.HasRemoteMediaConsent)
                {
                    RefreshRemoteMediaPicker();   // the chips stay on the live source while we ask
                    if (!await AskRemoteMediaConsentAsync(settings)) return;
                }

                settings.MediaSource = key;
                // Rotation state was picked for the old source; it goes stale the moment this changes.
                FypOnlineCoordinator.ResetAllChannels();
                CoreSettings.Save();
                RefreshRemoteMediaPicker();
                Log.Information("[remote media] source -> {Source}", settings.MediaSource);
            }
            catch (Exception ex) { Log.Warning(ex, "Remote media source change failed"); }
        }

        /// <summary>The one-time ask before anything remote is fetched (WPF AskRemoteMediaConsent),
        /// awaited: nothing is written until the answer lands.</summary>
        private async Task<bool> AskRemoteMediaConsentAsync(AppSettings settings)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return false;
            _consentAsking = true;
            try
            {
                var yes = await Dialogs.MessageDialog.ConfirmAsync(owner,
                    LocOr("title_remote_media_consent", "Use Reddit media?"),
                    LocOr("msg_remote_media_consent",
                        "Pull media from Reddit?\n\n" +
                        "The app will stream images and clips from the subreddits you pick, straight from your own machine. " +
                        "Nothing is saved to your disk, nothing is uploaded, and none of it goes through our servers.\n\n" +
                        "It is adult content and it is not curated by us - you choose the niches and subreddits, and only those are ever fetched.\n\n" +
                        "Turn it on?"),
                    defaultToCancel: true);
                if (!yes) return false;
                settings.RemoteMediaConsented = true;
                CoreSettings.Save();
                return true;
            }
            finally { _consentAsking = false; }
        }

        private void SliderRemoteRatio_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_pickerSyncing) return;
            var settings = CoreSettings.Current;
            settings.RemoteMediaRatio = (int)Math.Round(e.NewValue);   // the property clamps 5..95
            TxtRemoteRatio.Text = $"{settings.RemoteMediaRatio}%";
            CoreSettings.Save();   // debounced: a whole drag is one write
        }

        private void NicheChip_Changed(object? sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton t) PaintNicheChip(t);
            if (_pickerSyncing) return;
            try
            {
                var settings = CoreSettings.Current;
                // An empty selection is allowed: the coordinator falls back to the first niche.
                settings.FypOnlineNiches = _nicheChips.Where(c => c.IsChecked == true && c.Tag is string)
                                                      .Select(c => (string)c.Tag!).ToList();
                PersistChannelChange();
                PaintPickerChrome(settings);
            }
            catch (Exception ex) { Log.Warning(ex, "Remote niche toggle failed"); }
        }

        /// <summary>Niche / custom-sub edits land the same way: the lists are replaced, not observed,
        /// so save, and every consumer's rotation state was built from the old set.</summary>
        private static void PersistChannelChange()
        {
            CoreSettings.Save();
            FypOnlineCoordinator.ResetAllChannels();
        }

        // =====================================================================================
        //  flavours
        // =====================================================================================

        /// <summary>One click on a flavour: select the catalog niches that cover it and put the rest
        /// in the pool as custom subs. The selection from before the first flavour click is kept for
        /// this session, so Mine brings it back.</summary>
        private void ApplyFlavour(FlavourPresets.Flavour flavour)
        {
            try
            {
                var settings = CoreSettings.Current;
                var catalog = FypOnlineCoordinator.Catalog;
                var current = FlavourPresets.Match(settings.FypOnlineNiches, settings.FypOnlineCustomSubs, catalog);
                ShowSubError(null);
                if (flavour.Id == FlavourPresets.MineId)
                {
                    if (current == null || _mineNiches == null) return;   // already Mine
                    settings.FypOnlineNiches = new List<string>(_mineNiches);
                    settings.FypOnlineCustomSubs = new List<string>(_mineSubs ?? new List<string>());
                }
                else
                {
                    if (current == null)
                    {
                        _mineNiches = new List<string>(settings.FypOnlineNiches ?? new List<string>());
                        _mineSubs = new List<string>(settings.FypOnlineCustomSubs ?? new List<string>());
                    }
                    var sel = FlavourPresets.Resolve(flavour, catalog);
                    var pool = new List<string>();
                    foreach (var sub in sel.CustomSubs)
                    {
                        if (pool.Count >= RemoteCustomSubCap) break;
                        // Kept first (the library), then used here (the pool), same as a typed add.
                        if (settings.LibraryHasSub(sub) || settings.TryAddLibrarySub(sub)) pool.Add(sub);
                        else ShowSubOutcome(RemoteSubAddOutcome.LibraryFull, sub);
                    }
                    settings.FypOnlineNiches = sel.NicheIds.ToList();
                    settings.FypOnlineCustomSubs = pool;
                }
                PersistChannelChange();
                RefreshRemoteMediaPicker();
            }
            catch (Exception ex) { Log.Warning(ex, "Applying a media flavour failed"); }
        }

        private static Color Tint(string hex) => Color.TryParse(hex, out var c) ? c : RemoteNicheDefaultTint;

        private static SolidColorBrush TintBrush(Color c, double alpha) =>
            new(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B));

        private static IBrush Muted() =>
            Application.Current?.FindResource("TextMutedBrush") as IBrush ?? TintBrush(Colors.White, 0.5);

        /// <summary>One flavour card: a 4 px accent bar in its tint, name, one-liner, how many
        /// communities, and a check (Tag "check") on the chosen one.</summary>
        internal static Button CreateFlavourTile(FlavourPresets.Flavour f, ControlTheme? theme)
        {
            var tint = Tint(f.Tint);
            bool mine = f.Id == FlavourPresets.MineId;
            var grid = new Grid();
            grid.Children.Add(new Border
            {
                Width = 4, Margin = new Thickness(7, 10, 0, 10), CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(tint),
            });
            var stack = new StackPanel { Margin = new Thickness(20, 8, 24, 8), VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = f.Name, FontSize = 14, FontWeight = FontWeight.SemiBold,
                Foreground = TintBrush(Colors.White, 0.95), TextTrimming = TextTrimming.CharacterEllipsis,
            });
            stack.Children.Add(new TextBlock
            {
                Text = mine ? LocOr("label_remote_flavour_mine", "Your own mix") : f.Line,
                FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Muted(),
            });
            stack.Children.Add(new TextBlock
            {
                Tag = "count", Text = mine ? string.Empty : CommunitiesText(FlavourPoolCount(f)),
                FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Muted(),
            });
            grid.Children.Add(stack);
            grid.Children.Add(new TextBlock
            {
                Tag = "check", Text = "✓", FontSize = 12, FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 7, 10, 0), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top, Foreground = new SolidColorBrush(tint), IsVisible = false,
            });
            var tile = new Button { Theme = theme, Tag = f.Id, Content = grid };
            global::Avalonia.Automation.AutomationProperties.SetName(tile, f.Name);
            return tile;
        }

        /// <summary>Lights the card whose preset equals the selection exactly; Mine otherwise.
        /// Chosen: the tint at 18% with a 1.5 px tint border and the check.</summary>
        internal static void PaintFlavourTiles(IEnumerable<Button> tiles, IEnumerable<string>? niches, IEnumerable<string>? customSubs)
        {
            var nicheList = niches?.ToList() ?? new List<string>();
            var subList = customSubs?.ToList() ?? new List<string>();
            var lit = FlavourPresets.Match(nicheList, subList, FypOnlineCoordinator.Catalog) ?? FlavourPresets.Mine;
            foreach (var tile in tiles)
            {
                var f = FlavourPresets.ById(tile.Tag as string);
                if (f == null) continue;
                var tint = Tint(f.Tint);
                bool on = f.Id == lit.Id;
                tile.Background = on ? TintBrush(tint, 0.18) : TintBrush(Colors.White, 0x14 / 255.0);
                tile.BorderBrush = on ? new SolidColorBrush(tint) : TintBrush(Colors.White, 0x22 / 255.0);
                tile.BorderThickness = new Thickness(on ? 1.5 : 1);
                if (tile.Content is not Grid g) continue;
                foreach (var tb in g.Children.OfType<TextBlock>())
                    if (tb.Tag as string == "check") tb.IsVisible = on;
                if (f.Id == FlavourPresets.MineId && on)
                    foreach (var sp in g.Children.OfType<StackPanel>())
                        foreach (var tb in sp.Children.OfType<TextBlock>())
                            if (tb.Tag as string == "count") tb.Text = CommunitiesText(SelectionCounts(nicheList, subList).Subs);
            }
        }

        /// <summary>How many distinct communities choosing the flavour puts in the pool (the catalog
        /// niches it selects plus the leftovers it adds as your own).</summary>
        internal static int FlavourPoolCount(FlavourPresets.Flavour f)
        {
            try
            {
                var sel = FlavourPresets.Resolve(f, FypOnlineCoordinator.Catalog);
                var subs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var n in FypOnlineCoordinator.Catalog)
                    if (n?.Id != null && sel.NicheIds.Contains(n.Id, StringComparer.OrdinalIgnoreCase))
                        foreach (var s in n.Subs ?? Array.Empty<string>()) subs.Add(s);
                foreach (var s in sel.CustomSubs) subs.Add(s);
                return subs.Count;
            }
            catch { return f.Subs.Count; }
        }

        private static string CommunitiesText(int n) =>
            Plural(LocOr("label_remote_communities_n", "{0} community|{0} communities"), n);

        /// <summary>"{0} niche|{0} niches": the form after the bar for every count but one.</summary>
        internal static string Plural(string forms, int n)
        {
            var parts = (forms ?? string.Empty).Split('|');
            return string.Format(parts.Length > 1 && n != 1 ? parts[1] : parts[0], n);
        }

        /// <summary>Selected catalog niches, and the distinct subreddits they and the custom subs bring.</summary>
        internal static (int Niches, int Subs) SelectionCounts(IEnumerable<string>? nicheIds, IEnumerable<string>? customSubs)
        {
            var selected = new HashSet<string>(nicheIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var subs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int niches = 0;
            foreach (var n in FypOnlineCoordinator.Catalog)
            {
                if (n?.Id == null || !selected.Contains(n.Id)) continue;
                niches++;
                foreach (var s in n.Subs ?? Array.Empty<string>()) subs.Add(s);
            }
            foreach (var s in customSubs ?? Array.Empty<string>()) subs.Add(s);
            return (niches, subs.Count);
        }

        /// <summary>The Fine-tune row's label: "Fine-tune: N niches, M communities".</summary>
        internal static string SummaryText(IEnumerable<string>? nicheIds, IEnumerable<string>? customSubs)
        {
            var (niches, subs) = SelectionCounts(nicheIds, customSubs);
            return string.Format(LocOr("label_remote_fine_tune", "Fine-tune: {0}, {1}"),
                Plural(LocOr("unit_remote_niches", "{0} niche|{0} niches"), niches), CommunitiesText(subs));
        }

        // =====================================================================================
        //  rows: niches and your own communities
        // =====================================================================================

        /// <summary>The 36x20 pill switch every list row leads with (Tag "switch").</summary>
        internal static Border BuildSwitch() => new()
        {
            Tag = "switch", Width = 36, Height = 20, CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
            IsHitTestVisible = false,
            Child = new Ellipse { Width = 14, Height = 14, Margin = new Thickness(3, 0, 3, 0) },
        };

        /// <summary>On: Pink with a white knob at the right. Off: grey, knob left. The flavour tint
        /// stays on the name only (a pale tint on the track read as neither on nor off).</summary>
        internal static void PaintSwitch(Border track, bool on)
        {
            track.Background = on ? new SolidColorBrush(RemoteNicheDefaultTint) : TintBrush(Colors.White, 0.16);
            if (track.Child is Ellipse knob)
            {
                knob.Fill = on ? Brushes.White : TintBrush(Colors.White, 0.62);
                knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
        }

        private static TextBlock RowName(string text) => new()
        {
            Text = text, FontSize = 12.5, FontWeight = FontWeight.SemiBold,
            Foreground = TintBrush(Colors.White, 0.92), TextTrimming = TextTrimming.CharacterEllipsis,
        };

        private static TextBlock RowLine(string text) => new()
        {
            Text = text, FontSize = 11, Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Muted(),
        };

        /// <summary>One catalog niche as a list row: switch, name, and its subreddits on one muted line.</summary>
        internal static ToggleButton CreateNicheChip(FypOnlineCoordinator.Niche niche, ControlTheme? theme)
        {
            var subs = niche.Subs ?? Array.Empty<string>();
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            grid.Children.Add(BuildSwitch());
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(RowName(niche.Label));
            text.Children.Add(RowLine(string.Join(", ", subs.Select(x => "r/" + x))));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var chip = new ToggleButton { Theme = theme, Tag = niche.Id, Content = grid };
            global::Avalonia.Automation.AutomationProperties.SetName(chip, niche.Label);
            PaintNicheChip(chip);
            return chip;
        }

        internal static void PaintNicheChip(ToggleButton chip)
        {
            if (chip.Content is Grid g && g.Children.Count > 0 && g.Children[0] is Border track)
                PaintSwitch(track, chip.IsChecked == true);
        }

        /// <summary>One of YOUR communities as a list row: switch, name, a status line and an X that
        /// forgets it everywhere. <paramref name="onToggle"/> null = the in-flight probe row.</summary>
        private Border BuildSubRow(string name, string? status, bool on, Action? onToggle, Action? onRemove)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var track = BuildSwitch();
            PaintSwitch(track, on);
            grid.Children.Add(track);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(RowName(name));
            if (!string.IsNullOrWhiteSpace(status)) text.Children.Add(RowLine(status));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            if (onRemove != null)
            {
                var close = new Button { Content = new TextBlock { Text = "✕" }, Theme = this.FindResource("RemoteChipCloseButton") as ControlTheme };
                ToolTip.SetTip(close, LocOr("tooltip_remote_library_remove", "Forget this subreddit everywhere"));
                close.Click += (_, e) => { e.Handled = true; onRemove(); };
                Grid.SetColumn(close, 2);
                grid.Children.Add(close);
            }
            var row = new Border
            {
                MinHeight = 44, Padding = new Thickness(4, 6, 4, 6), Margin = new Thickness(0, 0, 16, 0),
                Background = Brushes.Transparent, BorderBrush = TintBrush(Colors.White, 0x14 / 255.0),
                BorderThickness = new Thickness(0, 0, 0, 1), Child = grid,
            };
            if (onToggle != null)
            {
                row.Cursor = new Cursor(StandardCursorType.Hand);
                row.PointerReleased += (_, e) =>
                {
                    if (e.Handled || e.InitialPressMouseButton != MouseButton.Left) return;
                    onToggle();
                };
            }
            return row;
        }

        /// <summary>The rows ARE the library (every sub kept anywhere in the app), not the pool: a
        /// switched-on row is in this machine's media pool, off = kept but unused here.</summary>
        private void RebuildCustomSubRows(AppSettings settings)
        {
            var host = RemoteCustomSubChips;
            host.Children.Clear();
            foreach (var row in settings.BuildRemoteSubLibraryView())
            {
                var name = row.Name;
                string? status = null;
                if (row.Ok == true)
                    status = row.StillOnly
                        ? LocOr("label_remote_sub_verified_stills", "Verified · stills only")
                        : string.Format(LocOr("label_remote_sub_verified", "Verified · {0} clips on Reddit"), row.VideoCount.GetValueOrDefault());
                else if (row.Ok == false)
                    status = RemoteSubAddMessages.Describe(RemoteSubAddOutcome.NotCarried, name, AppSettings.RemoteSubLibraryCap);
                host.Children.Add(BuildSubRow($"r/{name}", status, row.Selected,
                    onToggle: () => ToggleSubSelection(name), onRemove: () => RemoveCustomSub(name)));
            }
            if (_subPending != null)
                host.Children.Add(BuildSubRow($"r/{_subPending}…",
                    string.Format(LocOr("label_remote_sub_checking", "Checking r/{0}…"), _subPending), false, null, null));
            if (host.Children.Count == 0)
                host.Children.Add(new TextBlock
                {
                    Text = LocOr("label_remote_custom_subs_none", "None yet - the niches above are plenty to start with."),
                    FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), Foreground = Muted(),
                });
            TxtRemoteSummary.Text = SummaryText(settings.FypOnlineNiches, settings.FypOnlineCustomSubs);
        }

        /// <summary>The pill body: is this KEPT subreddit in the app-wide media pool or not.</summary>
        private void ToggleSubSelection(string sub)
        {
            var settings = CoreSettings.Current;
            var clean = FypOnlineCoordinator.SanitizeSub(sub) ?? sub;
            var list = new List<string>(settings.FypOnlineCustomSubs ?? new List<string>());
            int idx = list.FindIndex(x => string.Equals(x, clean, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) list.RemoveAt(idx);
            else
            {
                if (list.Count >= RemoteCustomSubCap)
                {
                    ShowSubError(LocOr("msg_remote_sub_cap", "Up to 20 custom subreddits"));
                    return;
                }
                list.Add(clean);
            }
            settings.FypOnlineCustomSubs = list;
            ShowSubError(null);
            PersistChannelChange();
            RefreshRemoteMediaPicker();
        }

        /// <summary>The X: FORGET this subreddit everywhere (library, verdict, pool).</summary>
        private void RemoveCustomSub(string sub)
        {
            if (CoreSettings.Current.RemoveLibrarySub(sub))
                Log.Information("[remote media] r/{Sub} removed from the library", sub);
            ShowSubError(null);
            PersistChannelChange();
            RefreshRemoteMediaPicker();
        }

        /// <summary>
        /// Commit a typed subreddit, only after asking the provider whether it exists. Every refusal
        /// is answered inline under the box (Core <see cref="RemoteSubAddMessages"/>). The row goes
        /// up switched off the moment the name is legal; the sub is written only on a real "it
        /// exists". A not-found verdict is stored (no round trip for the same typo); a transport
        /// failure stores nothing.
        /// </summary>
        private async void AddRemoteCustomSub()
        {
            var settings = CoreSettings.Current;
            if (_subPending != null) return;   // one probe at a time; the button is disabled too
            string clean;
            try
            {
                ShowSubError(null);
                var sanitized = FypOnlineCoordinator.SanitizeSub(TxtRemoteCustomSub.Text);
                if (sanitized == null) { ShowSubOutcome(RemoteSubAddOutcome.NotAName, null); return; }
                clean = sanitized;
                var current = settings.FypOnlineCustomSubs ?? new List<string>();
                if (settings.LibraryHasSub(clean))
                {
                    // Already KEPT: typing it again is "use it here", not an error.
                    TxtRemoteCustomSub.Text = "";
                    if (current.Any(x => string.Equals(x, clean, StringComparison.OrdinalIgnoreCase)))
                    {
                        ShowSubOutcome(RemoteSubAddOutcome.AlreadyAdded, clean);
                        return;
                    }
                    ToggleSubSelection(clean);
                    return;
                }
                if (settings.RemoteSubLibrary.Count >= AppSettings.RemoteSubLibraryCap)
                {
                    ShowSubOutcome(RemoteSubAddOutcome.LibraryFull, clean);
                    return;
                }
                TxtRemoteCustomSub.Text = "";
                _subPending = clean;
                BtnRemoteAddSub.IsEnabled = false;
                RebuildCustomSubRows(settings);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Adding a custom subreddit failed");
                EndSubProbe();
                return;
            }

            SubProbe probe;
            try { probe = await Task.Run(() => FypOnlineCoordinator.ProbeSubAsync(clean, CancellationToken.None)); }
            catch (Exception ex)
            {
                Log.Warning(ex, "Probing r/{Sub} threw", clean);
                probe = new SubProbe { Ok = false, Error = "offline" };
            }

            try
            {
                settings = CoreSettings.Current;
                if (probe.Ok)
                {
                    settings.TryAddLibrarySub(clean);
                    var list = new List<string>(settings.FypOnlineCustomSubs ?? new List<string>());
                    if (!list.Any(x => string.Equals(x, clean, StringComparison.OrdinalIgnoreCase)) && list.Count < RemoteCustomSubCap)
                        list.Add(clean);
                    settings.FypOnlineCustomSubs = list;
                    settings.FypOnlineSubVerdicts[clean] = new RemoteSubVerdict
                    {
                        Ok = true, VideoCount = probe.VideoCount, CheckedAtUtc = DateTime.UtcNow,
                    };
                    _subPending = null;
                    PersistChannelChange();
                    RefreshRemoteMediaPicker();
                    Log.Information("[remote media] custom sub r/{Sub} verified ({N} videos)", clean, probe.VideoCount);
                }
                else
                {
                    var outcome = RemoteSubAddMessages.Classify(probe.Ok, probe.Error);
                    if (outcome == RemoteSubAddOutcome.NotCarried)
                    {
                        settings.FypOnlineSubVerdicts[clean] = new RemoteSubVerdict { Ok = false, VideoCount = null, CheckedAtUtc = DateTime.UtcNow };
                        CoreSettings.Save();
                    }
                    ShowSubOutcome(outcome, clean);
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Committing a probed subreddit failed"); }
            finally { EndSubProbe(); }
        }

        private void EndSubProbe()
        {
            var hadPending = _subPending != null;
            _subPending = null;
            BtnRemoteAddSub.IsEnabled = true;
            if (hadPending) RebuildCustomSubRows(CoreSettings.Current);
        }

        private void ShowSubOutcome(RemoteSubAddOutcome outcome, string? name) =>
            ShowSubError(RemoteSubAddMessages.Describe(outcome, name, AppSettings.RemoteSubLibraryCap));

        /// <summary>The one soft-error slot under the add row. Null hides it; a reason opens
        /// Fine-tune so it never lands in a closed drawer.</summary>
        private void ShowSubError(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                TxtRemoteSubError.Text = "";
                TxtRemoteSubError.IsVisible = false;
                return;
            }
            TxtRemoteSubError.Text = text;
            TxtRemoteSubError.IsVisible = true;
            if (!_fineTuneOpen) SetFineTuneOpen(true);
        }
    }
}
