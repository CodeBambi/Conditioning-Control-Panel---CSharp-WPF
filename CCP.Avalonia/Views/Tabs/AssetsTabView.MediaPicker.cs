using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The media-source picker, PORTED from MainWindow.Assets.cs:2208-3230 (main 3e225c963,
    /// 31c176ce1, 442eee04e, 8a4b6df97, 686f8c960): segmented source, flavour cards, one Fine-tune
    /// row over the niche rows and your own communities. No TierGate, by design (WPF :2211).
    /// It lives in the view rather than the shell because nothing outside this block reads it;
    /// WPF re-syncs it on every ShowTab("assets"), this head on every IsVisible -> true.
    ///
    /// Deviations: the consent ask is AWAITED (MessageDialog is async) and MediaSource is written
    /// only after Yes; the chips are put back first so nothing reads "Reddit" while it waits.
    /// WPF's InvalidateAssetPoolsAfterSelectionChange has no target here: no flash/video service
    /// on this head consumes online media yet (parity row views-tab-assets), so the switch is
    /// ResetAllChannels + Save, which is what intake (the one consumer) reads.
    /// </summary>
    public partial class AssetsTabView
    {
        private const string MediaSrcLocal = "local", MediaSrcOnline = "online", MediaSrcMixed = "mixed";
        private const int RemoteCustomSubCap = 20;   // WPF :2248, the FYP popover's Take(20)

        /// <summary>Test seams: the consent modal (title, message) -> answer, and the Scrolller probe.
        /// Production asks the real dialog and the real provider.</summary>
        internal static Func<string, string, Task<bool>>? ConsentOverride { get; set; }
        internal static Func<string, CancellationToken, Task<SubProbe>> ProbeSub { get; set; } = FypOnlineCoordinator.ProbeSubAsync;

        /// <summary>The last async user action (source switch, sub add), so tests await its end (P56).
        /// Both bodies catch everything they can throw.</summary>
        internal Task LastPickerTask { get; private set; } = Task.CompletedTask;

        private bool _pickerWired, _pickerSyncing, _consentPending, _fineTuneOpen;
        private readonly List<ToggleButton> _sourceChips = new(), _nicheChips = new();
        private readonly List<Button> _flavourTiles = new();
        private List<string>? _mineNiches, _mineSubs;
        private string? _subPending;

        private static AppSettings? Settings => CoreSettings.Current;

        private static string LocOr(string key, string english)
        {
            var v = Loc.Get(key);
            return string.IsNullOrEmpty(v) || v == key ? english : v;
        }

        /// <summary>Builds the picker once, then re-syncs it to settings (WPF InitializeRemoteMediaPicker).</summary>
        internal void InitializeRemoteMediaPicker()
        {
            try
            {
                if (!_pickerWired)
                {
                    _pickerWired = true;
                    BuildSourceChips();
                    BuildFlavourTiles();
                    BuildNicheChips();
                    SliderRemoteRatio.ValueChanged += (_, e) => SliderRemoteRatio_Changed(e.NewValue);
                    BtnRemoteAddSub.Click += (_, _) => LastPickerTask = AddRemoteCustomSubAsync();
                    TxtRemoteCustomSub.KeyDown += (_, e) =>
                    {
                        if (e.Key != Key.Enter) return;
                        e.Handled = true;
                        LastPickerTask = AddRemoteCustomSubAsync();
                    };
                    BtnRemoteFineTune.Click += (_, _) => SetFineTuneOpen(!_fineTuneOpen);
                    PropertyChanged += (_, e) =>
                    {
                        if (e.Property == IsVisibleProperty && IsVisible) RefreshRemoteMediaPicker();
                    };
                }
                RefreshRemoteMediaPicker();
            }
            catch (Exception ex) { Log.Warning(ex, "Remote media picker init failed"); }
        }

        /// <summary>Opens or closes Fine-tune; the browser steps aside while it is open (WPF :2280).</summary>
        private void SetFineTuneOpen(bool open)
        {
            _fineTuneOpen = open;
            RemoteFineTuneScroll.IsVisible = open;
            AssetBrowserSection.IsVisible = !open;
            TxtRemoteFineTuneChevron.Text = open ? "▴" : "▾";
        }

        private void BuildSourceChips()
        {
            var theme = RemoteMediaBlock.FindResource("RemoteSourceSegment") as ControlTheme;
            void Add(string key, string locKey, string english, string tipKey, string tipEnglish)
            {
                var chip = new ToggleButton { Name = "ChipSource_" + key, Theme = theme, Tag = key, Content = new TextBlock { Text = LocOr(locKey, english) } };
                ToolTip.SetTip(chip, LocOr(tipKey, tipEnglish));
                chip.IsCheckedChanged += (_, _) => { if (!_pickerSyncing) LastPickerTask = RemoteSourceChip_Changed(chip); };
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

        private void BuildNicheChips()
        {
            var theme = RemoteMediaBlock.FindResource("RemoteNichePill") as ControlTheme;
            foreach (var niche in FypOnlineCoordinator.Catalog)
            {
                var chip = CreateNicheChip(niche, theme);
                chip.IsCheckedChanged += (_, _) => RemoteNicheChip_Changed();
                _nicheChips.Add(chip);
                RemoteNicheChips.Children.Add(chip);
            }
        }

        private void BuildFlavourTiles()
        {
            var theme = RemoteMediaBlock.FindResource("RemoteFlavourTile") as ControlTheme;
            foreach (var f in FlavourPresets.All.Append(FlavourPresets.Mine))
            {
                var flavour = f;
                var tile = CreateFlavourTile(f, theme);
                tile.Click += (_, _) => ApplyRemoteFlavour(flavour);
                _flavourTiles.Add(tile);
                RemoteFlavourTiles.Children.Add(tile);
            }
        }

        /// <summary>Pushes live settings into every control (WPF RefreshRemoteMediaPicker).</summary>
        internal void RefreshRemoteMediaPicker()
        {
            var s = Settings;
            if (s == null || !_pickerWired) return;
            _pickerSyncing = true;
            try
            {
                var source = s.MediaSource;
                foreach (var chip in _sourceChips) chip.IsChecked = (string?)chip.Tag == source;
                SliderRemoteRatio.Value = s.RemoteMediaRatio;
                TxtRemoteRatio.Text = $"{s.RemoteMediaRatio}%";
                RemoteRatioRow.IsVisible = source == MediaSrcMixed;
                RemoteMediaDetails.IsVisible = source != MediaSrcLocal;
                var selected = new HashSet<string>(s.FypOnlineNiches ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var chip in _nicheChips) chip.IsChecked = chip.Tag is string id && selected.Contains(id);
                PaintChrome(s);
                RebuildCustomSubRows(s);
                SetFineTuneOpen(_fineTuneOpen);
            }
            catch (Exception ex) { Log.Warning(ex, "Remote media picker refresh failed"); }
            finally { _pickerSyncing = false; }
        }

        internal async Task RemoteSourceChip_Changed(ToggleButton chip)
        {
            if (_pickerSyncing) return;
            var s = Settings;
            if (s == null || chip.Tag is not string key) return;
            try
            {
                // Un-clicking the live chip would leave no source; a click while the ask is open is ignored.
                if (chip.IsChecked != true || key == s.MediaSource || _consentPending)
                {
                    RefreshRemoteMediaPicker();
                    return;
                }
                if (key != MediaSrcLocal && !s.HasRemoteMediaConsent)
                {
                    RefreshRemoteMediaPicker();   // the chips stay on the old source while we ask
                    _consentPending = true;
                    bool yes;
                    try { yes = await AskRemoteMediaConsentAsync(); }
                    finally { _consentPending = false; }
                    if (!yes) return;
                    s.RemoteMediaConsented = true;
                    CoreSettings.Save();
                    (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.MaybeShowFeatureIntro("remotemedia");
                }
                s.MediaSource = key;
                FypOnlineCoordinator.ResetAllChannels();
                CoreSettings.Save();
                RefreshRemoteMediaPicker();
                Log.Information("[remote media] source -> {Source}", s.MediaSource);
            }
            catch (Exception ex) { Log.Warning(ex, "Remote media source change failed"); }
        }

        /// <summary>The one-time ask before anything remote is fetched (WPF AskRemoteMediaConsent :2380).</summary>
        private Task<bool> AskRemoteMediaConsentAsync()
        {
            var title = LocOr("title_remote_media_consent", "Use Reddit media?");
            var message = LocOr("msg_remote_media_consent",
                "Pull media from Reddit?\n\n" +
                "The app will stream images and clips from the subreddits you pick, straight from your own machine. " +
                "Nothing is saved to your disk, nothing is uploaded, and none of it goes through our servers.\n\n" +
                "It is adult content and it is not curated by us - you choose the niches and subreddits, and only those are ever fetched.\n\n" +
                "Turn it on?");
            if (ConsentOverride is { } o) return o(title, message);
            if (TopLevel.GetTopLevel(this) is not Window owner) return Task.FromResult(false);
            // WPF YesNo; there is no "No" key in the language files, so the second button keeps the
            // dialog's localized Cancel. Close/Cancel both answer false, as No does.
            return Dialogs.MessageDialog.ConfirmAsync(owner, title, message, okText: Loc.Get("label_yes"));
        }

        private void RemoteNicheChip_Changed()
        {
            if (_pickerSyncing || Settings is not { } s) return;
            s.FypOnlineNiches = _nicheChips.Where(c => c.IsChecked == true).Select(c => (string)c.Tag!).ToList();
            PersistChannelChange();
            PaintChrome(s);
        }

        private void SliderRemoteRatio_Changed(double value)
        {
            if (_pickerSyncing || Settings is not { } s) return;
            s.RemoteMediaRatio = (int)Math.Round(value);   // property clamps 5..95
            TxtRemoteRatio.Text = $"{s.RemoteMediaRatio}%";
            CoreSettings.Save();
        }

        /// <summary>Commit a typed subreddit after the provider says it exists (WPF AddRemoteCustomSub :2525).</summary>
        internal async Task AddRemoteCustomSubAsync()
        {
            var s = Settings;
            if (s == null || _subPending != null) return;
            ShowSubError(null);
            var clean = FypOnlineCoordinator.SanitizeSub(TxtRemoteCustomSub.Text);
            if (clean == null) { ShowSubOutcome(RemoteSubAddOutcome.NotAName, null); return; }
            if (s.LibraryHasSub(clean))
            {
                TxtRemoteCustomSub.Text = "";
                if ((s.FypOnlineCustomSubs ?? new List<string>()).Any(x => string.Equals(x, clean, StringComparison.OrdinalIgnoreCase)))
                    ShowSubOutcome(RemoteSubAddOutcome.AlreadyAdded, clean);
                else ToggleSubSelection(clean);
                return;
            }
            if (s.RemoteSubLibrary.Count >= AppSettings.RemoteSubLibraryCap) { ShowSubOutcome(RemoteSubAddOutcome.LibraryFull, clean); return; }

            TxtRemoteCustomSub.Text = "";
            _subPending = clean;
            BtnRemoteAddSub.IsEnabled = false;
            RebuildCustomSubRows(s);

            SubProbe probe;
            try { probe = await Task.Run(() => ProbeSub(clean, CancellationToken.None)); }
            catch (Exception ex)
            {
                Log.Warning(ex, "Probing r/{Sub} threw", clean);
                probe = new SubProbe { Ok = false, Error = "offline" };
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    if (probe.Ok)
                    {
                        s.TryAddLibrarySub(clean);
                        var list = new List<string>(s.FypOnlineCustomSubs ?? new List<string>());
                        if (!list.Any(x => string.Equals(x, clean, StringComparison.OrdinalIgnoreCase)) && list.Count < RemoteCustomSubCap)
                            list.Add(clean);
                        s.FypOnlineCustomSubs = list;
                        s.FypOnlineSubVerdicts[clean] = new RemoteSubVerdict { Ok = true, VideoCount = probe.VideoCount, CheckedAtUtc = DateTime.UtcNow };
                        _subPending = null;
                        PersistChannelChange();
                        RefreshRemoteMediaPicker();
                    }
                    else
                    {
                        var outcome = RemoteSubAddMessages.Classify(probe.Ok, probe.Error);
                        if (outcome == RemoteSubAddOutcome.NotCarried)
                        {
                            s.FypOnlineSubVerdicts[clean] = new RemoteSubVerdict { Ok = false, VideoCount = null, CheckedAtUtc = DateTime.UtcNow };
                            CoreSettings.Save();
                        }
                        ShowSubOutcome(outcome, clean);
                    }
                }
                catch (Exception ex) { Log.Warning(ex, "Committing a probed subreddit failed"); }
                finally
                {
                    var had = _subPending != null;
                    _subPending = null;
                    BtnRemoteAddSub.IsEnabled = true;
                    if (had) RebuildCustomSubRows(s);
                }
            });
        }

        private void ShowSubOutcome(RemoteSubAddOutcome outcome, string? name) =>
            ShowSubError(RemoteSubAddMessages.Describe(outcome, name, AppSettings.RemoteSubLibraryCap));

        /// <summary>The soft-error slot under the add row; a refusal never lands in a closed drawer.</summary>
        private void ShowSubError(string? text)
        {
            var show = !string.IsNullOrWhiteSpace(text);
            TxtRemoteSubError.Text = show ? text : "";
            TxtRemoteSubError.IsVisible = show;
            if (show && !_fineTuneOpen) SetFineTuneOpen(true);
        }

        /// <summary>The X: forget the sub everywhere (library, verdict, pool).</summary>
        private void RemoveSub(string sub)
        {
            if (Settings is not { } s) return;
            s.RemoveLibrarySub(sub);
            ShowSubError(null);
            PersistChannelChange();
            RefreshRemoteMediaPicker();
        }

        /// <summary>The row body: in this machine's pool or not; the name stays kept either way.</summary>
        private void ToggleSubSelection(string sub)
        {
            if (Settings is not { } s) return;
            var clean = FypOnlineCoordinator.SanitizeSub(sub) ?? sub;
            var list = new List<string>(s.FypOnlineCustomSubs ?? new List<string>());
            int idx = list.FindIndex(x => string.Equals(x, clean, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) list.RemoveAt(idx);
            else
            {
                if (list.Count >= RemoteCustomSubCap) { ShowSubError(LocOr("msg_remote_sub_cap", "Up to 20 custom subreddits")); return; }
                list.Add(clean);
            }
            s.FypOnlineCustomSubs = list;
            ShowSubError(null);
            PersistChannelChange();
            RefreshRemoteMediaPicker();
        }

        private static void PersistChannelChange()
        {
            CoreSettings.Save();
            FypOnlineCoordinator.ResetAllChannels();
        }

        private void RebuildCustomSubRows(AppSettings s)
        {
            var host = RemoteCustomSubChips;
            host.Children.Clear();
            foreach (var row in s.BuildRemoteSubLibraryView())
            {
                var name = row.Name;
                string? status = row.Ok switch
                {
                    true => row.StillOnly
                        ? LocOr("label_remote_sub_verified_stills", "Verified · stills only")
                        : string.Format(LocOr("label_remote_sub_verified", "Verified · {0} clips on Reddit"), row.VideoCount.GetValueOrDefault()),
                    false => RemoteSubAddMessages.Describe(RemoteSubAddOutcome.NotCarried, name, AppSettings.RemoteSubLibraryCap),
                    _ => null
                };
                host.Children.Add(BuildSubRow($"r/{name}", status, row.Selected,
                    () => ToggleSubSelection(name), () => RemoveSub(name)));
            }
            if (_subPending != null)
                host.Children.Add(BuildSubRow($"r/{_subPending}…",
                    string.Format(LocOr("label_remote_sub_checking", "Checking r/{0}…"), _subPending), false, null, null));
            if (host.Children.Count == 0)
                host.Children.Add(new TextBlock
                {
                    Text = LocOr("label_remote_custom_subs_none", "None yet - the niches above are plenty to start with."),
                    FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), Foreground = Muted
                });
            RepaintSummary(s);
        }

        // ---- The look (WPF :2805-3228): static builders and painters --------------------------

        private static readonly Color DefaultTint = Color.FromRgb(0xFF, 0x69, 0xB4);
        private static IBrush Muted => Application.Current?.FindResource("TextMutedBrush") as IBrush ?? Tint(Colors.White, 0.5);
        private static Color TintOf(string hex) => Color.TryParse(hex, out var c) ? c : DefaultTint;
        private static SolidColorBrush Tint(Color c, double a) =>
            new(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B));

        /// <summary>The 36x20 pill switch every list row leads with.</summary>
        internal static Border BuildSwitch() => new()
        {
            Tag = "switch", Width = 36, Height = 20, CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), IsHitTestVisible = false,
            Child = new Ellipse { Width = 14, Height = 14, Margin = new Thickness(3, 0, 3, 0) }
        };

        /// <summary>On: Pink, white knob right. Off: grey, knob left (686f8c960: the tint stays on the name).</summary>
        internal static void PaintSwitch(Border track, bool on)
        {
            track.Background = on ? new SolidColorBrush(DefaultTint) : Tint(Colors.White, 0.16);
            if (track.Child is Ellipse knob)
            {
                knob.Fill = on ? Brushes.White : Tint(Colors.White, 0.62);
                knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
        }

        private static TextBlock RowName(string text) => new()
        { Text = text, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Tint(Colors.White, 0.92), TextTrimming = TextTrimming.CharacterEllipsis };

        private static TextBlock RowLine(string text) => new()
        { Text = text, FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Muted };

        private static Grid RowGrid(bool close) => new() { ColumnDefinitions = new ColumnDefinitions(close ? "Auto,*,Auto" : "Auto,*") };

        /// <summary>One of YOUR communities: switch, name, status, X. A Button, so it is keyboard-reachable.
        /// <paramref name="onToggle"/> null = the in-flight probe row.</summary>
        internal Control BuildSubRow(string name, string? status, bool on, Action? onToggle, Action? onRemove)
        {
            var grid = RowGrid(onRemove != null);
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
                var close = new Button { Content = "✕", Theme = this.FindResource("RemoteChipCloseButton") as ControlTheme };
                ToolTip.SetTip(close, LocOr("tooltip_remote_library_remove", "Forget this subreddit everywhere"));
                close.Click += (_, e) => { e.Handled = true; onRemove(); };
                Grid.SetColumn(close, 2);
                grid.Children.Add(close);
            }
            // The same row look as RemoteNichePill (WPF :2906), as a Button so it is keyboard-reachable.
            var row = new Button { Template = RowTemplate, Content = grid, IsEnabled = onToggle != null, Cursor = new Cursor(StandardCursorType.Hand) };
            if (onToggle != null) row.Click += (_, _) => onToggle();
            return row;
        }

        private static readonly global::Avalonia.Controls.Templates.FuncControlTemplate<Button> RowTemplate = new((b, _) => new Border
        {
            MinHeight = 44, Padding = new Thickness(4, 6), Margin = new Thickness(0, 0, 16, 0),
            Background = Brushes.Transparent, BorderBrush = Tint(Colors.White, 0x14 / 255.0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new ContentPresenter { [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty] }
        });

        /// <summary>A niche wears the tint of the flavour whose preset selects it, Pink otherwise.</summary>
        internal static Color NicheTintOf(string? nicheId)
        {
            var owner = nicheId == null ? null : FlavourPresets.OwnerOf(nicheId, FypOnlineCoordinator.Catalog);
            return owner != null ? TintOf(owner.Tint) : DefaultTint;
        }

        internal static ToggleButton CreateNicheChip(FypOnlineCoordinator.Niche niche, ControlTheme? theme)
        {
            var grid = RowGrid(false);
            grid.Children.Add(BuildSwitch());
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(RowName(niche.Label));
            text.Children.Add(RowLine(string.Join(", ", (niche.Subs ?? Array.Empty<string>()).Select(x => "r/" + x))));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var chip = new ToggleButton { Theme = theme, Tag = niche.Id, Content = grid };
            global::Avalonia.Automation.AutomationProperties.SetName(chip, niche.Label);
            chip.IsCheckedChanged += (_, _) => PaintSwitch((Border)grid.Children[0], chip.IsChecked == true);
            PaintSwitch((Border)grid.Children[0], false);
            return chip;
        }

        private static string CommunitiesText(int n) => Plural(LocOr("label_remote_communities_n", "{0} community|{0} communities"), n);

        /// <summary>Distinct communities choosing the flavour puts in the pool (686f8c960).</summary>
        internal static int FlavourPoolCount(FlavourPresets.Flavour f)
        {
            var sel = FlavourPresets.Resolve(f, FypOnlineCoordinator.Catalog);
            return SelectionCounts(sel.NicheIds, sel.CustomSubs).Subs;
        }

        internal static Button CreateFlavourTile(FlavourPresets.Flavour f, ControlTheme? theme)
        {
            var tint = TintOf(f.Tint);
            bool mine = f.Id == FlavourPresets.MineId;
            var grid = new Grid();
            grid.Children.Add(new Border
            {
                Width = 4, Margin = new Thickness(7, 10, 0, 10), CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(tint)
            });
            var stack = new StackPanel { Margin = new Thickness(20, 8, 24, 8), VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = f.Name, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Tint(Colors.White, 0.95), TextTrimming = TextTrimming.CharacterEllipsis });
            stack.Children.Add(new TextBlock { Text = mine ? LocOr("label_remote_flavour_mine", "Your own mix") : f.Line, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Muted });
            stack.Children.Add(new TextBlock { Tag = "count", Text = mine ? "" : CommunitiesText(FlavourPoolCount(f)), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Muted });
            grid.Children.Add(stack);
            grid.Children.Add(new TextBlock
            {
                Tag = "check", Text = "✓", FontSize = 12, Margin = new Thickness(0, 9, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Foreground = new SolidColorBrush(tint), IsVisible = false
            });
            var tile = new Button { Name = "TileFlavour_" + f.Id, Theme = theme, Tag = f.Id, Content = grid };
            global::Avalonia.Automation.AutomationProperties.SetName(tile, f.Name);
            return tile;
        }

        /// <summary>One click on a flavour: its niches + leftover subs; the pre-flavour selection
        /// is kept for this session so Mine brings it back (WPF ApplyRemoteFlavour).</summary>
        internal void ApplyRemoteFlavour(FlavourPresets.Flavour flavour)
        {
            try
            {
                if (Settings is not { } s) return;
                var catalog = FypOnlineCoordinator.Catalog;
                var current = FlavourPresets.Match(s.FypOnlineNiches, s.FypOnlineCustomSubs, catalog);
                ShowSubError(null);
                if (flavour.Id == FlavourPresets.MineId)
                {
                    if (current == null || _mineNiches == null) return;
                    s.FypOnlineNiches = new List<string>(_mineNiches);
                    s.FypOnlineCustomSubs = new List<string>(_mineSubs ?? new List<string>());
                }
                else
                {
                    if (current == null)
                    {
                        _mineNiches = new List<string>(s.FypOnlineNiches ?? new List<string>());
                        _mineSubs = new List<string>(s.FypOnlineCustomSubs ?? new List<string>());
                    }
                    var sel = FlavourPresets.Resolve(flavour, catalog);
                    var pool = new List<string>();
                    foreach (var sub in sel.CustomSubs)
                    {
                        if (pool.Count >= RemoteCustomSubCap) break;
                        if (s.LibraryHasSub(sub) || s.TryAddLibrarySub(sub)) pool.Add(sub);
                        else ShowSubOutcome(RemoteSubAddOutcome.LibraryFull, sub);
                    }
                    s.FypOnlineNiches = sel.NicheIds.ToList();
                    s.FypOnlineCustomSubs = pool;
                }
                PersistChannelChange();
                RefreshRemoteMediaPicker();
            }
            catch (Exception ex) { Log.Warning(ex, "Applying a media flavour failed"); }
        }

        private void PaintChrome(AppSettings s)
        {
            foreach (var chip in _nicheChips)
                if (chip.Content is Grid g && g.Children[0] is Border t) PaintSwitch(t, chip.IsChecked == true);
            PaintFlavourTiles(_flavourTiles, s.FypOnlineNiches, s.FypOnlineCustomSubs);
            RepaintSummary(s);
        }

        /// <summary>Lights the card whose preset equals the selection; Mine otherwise. Mine's count follows the selection.</summary>
        internal static void PaintFlavourTiles(IEnumerable<Button> tiles, IEnumerable<string>? niches, IEnumerable<string>? customSubs)
        {
            var nicheList = niches?.ToList() ?? new List<string>();
            var subList = customSubs?.ToList() ?? new List<string>();
            var lit = FlavourPresets.Match(nicheList, subList, FypOnlineCoordinator.Catalog) ?? FlavourPresets.Mine;
            foreach (var tile in tiles)
            {
                if (FlavourPresets.ById(tile.Tag as string) is not { } f) continue;
                var tint = TintOf(f.Tint);
                bool on = f.Id == lit.Id;
                tile.Background = on ? Tint(tint, 0.18) : Tint(Colors.White, 0x14 / 255.0);
                tile.BorderBrush = on ? new SolidColorBrush(tint) : Tint(Colors.White, 0x22 / 255.0);
                tile.BorderThickness = new Thickness(on ? 1.5 : 1);
                if (tile.Content is not Grid g) continue;
                foreach (var tb in g.Children.OfType<TextBlock>().Where(t => (string?)t.Tag == "check")) tb.IsVisible = on;
                if (f.Id == FlavourPresets.MineId && on)
                    foreach (var tb in g.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<TextBlock>()).Where(t => (string?)t.Tag == "count"))
                        tb.Text = CommunitiesText(SelectionCounts(nicheList, subList).Subs);
            }
        }

        internal static (int Niches, int Subs) SelectionCounts(IEnumerable<string>? nicheIds, IEnumerable<string>? customSubs)
        {
            var selected = new HashSet<string>(nicheIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var subs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int niches = 0;
            foreach (var n in FypOnlineCoordinator.Catalog)
            {
                if (n?.Id == null || !selected.Contains(n.Id)) continue;
                niches++;
                foreach (var x in n.Subs ?? Array.Empty<string>()) subs.Add(x);
            }
            foreach (var x in customSubs ?? Array.Empty<string>()) subs.Add(x);
            return (niches, subs.Count);
        }

        /// <summary>"Fine-tune: N niches, M communities" (31c176ce1 knows its singular).</summary>
        internal static string SummaryText(IEnumerable<string>? nicheIds, IEnumerable<string>? customSubs)
        {
            var (niches, subs) = SelectionCounts(nicheIds, customSubs);
            return string.Format(LocOr("label_remote_fine_tune", "Fine-tune: {0}, {1}"),
                Plural(LocOr("unit_remote_niches", "{0} niche|{0} niches"), niches), CommunitiesText(subs));
        }

        internal static string Plural(string forms, int n)
        {
            var parts = (forms ?? "").Split('|');
            return string.Format(parts.Length > 1 && n != 1 ? parts[1] : parts[0], n);
        }

        private void RepaintSummary(AppSettings s)
        {
            TxtRemoteSummary.Text = SummaryText(s.FypOnlineNiches, s.FypOnlineCustomSubs);
            global::Avalonia.Automation.AutomationProperties.SetName(BtnRemoteFineTune, TxtRemoteSummary.Text);
        }
    }
}
