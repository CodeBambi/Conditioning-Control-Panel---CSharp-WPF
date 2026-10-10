using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Z0 header band + Z1 the Companion Card. See the XAML header for the visual spec.
    /// PORTED from ConditioningControlPanel/Views/Controls/Companion/CompanionHeroCard.xaml.cs.
    ///
    /// <para>This control owns the Companion tab's <b>single ambient loop</b>: the portrait ring
    /// breathing 1.000 ↔ 1.015. The FX plan allows exactly one forever animation per tab, and
    /// this is where it is spent — nothing else on the page may add another.</para>
    ///
    /// <para>The loop is parked in three situations, so a hidden or sleeping hero is never still
    /// burning a composition clock: on unload, while <see cref="CompanionHeroCardViewModel.IsCompanionEnabled"/>
    /// is false (the mockup's <c>animation:none</c> asleep state), and whenever the viewmodel is
    /// swapped out.</para>
    ///
    /// <para>The mod repaint is wired: <see cref="CoreMods.ModChanged"/> is the authoritative
    /// "the art answers differently now" signal, and the only one this tab gets, so the hook is
    /// taken on Loaded and released on Unloaded exactly as the WPF original does with
    /// <c>App.Mods.ModChanged</c>. It re-reads the bust for real now: <see cref="ApplyAvatarArt"/>
    /// resolves the same <c>avatar[N]_pose1.png</c> the WPF runtime viewmodel does, through
    /// <see cref="CoreModArt"/> + <see cref="ModArt"/>, and <see cref="CentrePortrait"/> then
    /// crops it the way WPF's measured Viewbox does. What is still head-only is the rest of
    /// <c>Sync()</c> — her name, mod chip and flavour — named at <see cref="ApplyAvatarArt"/>.</para>
    /// </summary>
    public partial class CompanionHeroCard : UserControl
    {
        private CompanionHeroCardViewModel? _observed;

        /// <summary>Guards the ModChanged hook: Loaded fires again on every re-parent.</summary>
        private bool _modHooked;

        /// <summary>How much of the ring's inner diameter the figure's own INK may occupy.</summary>
        private const double PortraitInkFill = 0.86;

        /// <summary>Width the opaque-bounds scan runs at; below this the art is scanned as-is.</summary>
        private const int PortraitProbeWidth = 96;

        /// <summary>Alpha at or below which a pixel counts as transparent padding.</summary>
        private const byte PortraitAlphaFloor = 8;

        public CompanionHeroCard()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new CompanionHeroCardViewModel();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public CompanionHeroCardViewModel? ViewModel
        {
            get => DataContext as CompanionHeroCardViewModel;
            set => DataContext = value;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            // WPF's "Known Issues #2: never animate before the element is loaded and templated"
            // guard is deliberately NOT ported: RefreshAmbientState and StartAmbientLoop already
            // re-check IsLoaded, and an early return here would also skip the mod hook.
            Observe(ViewModel);

            // Hook on Loaded, unhook on Unloaded, and never let a re-parent double-subscribe.
            if (!_modHooked)
            {
                CoreMods.ModChanged += OnModChanged;
                _modHooked = true;
            }

            // DispatcherPriority.Normal, never Loaded — Loaded is starved in this app and the
            // breathe would silently never start.
            Dispatcher.UIThread.Post(RefreshAmbientState, DispatcherPriority.Normal);
            Dispatcher.UIThread.Post(ApplyAvatarArt, DispatcherPriority.Normal);
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            Observe(null);
            StopAmbientLoop();

            if (_modHooked) CoreMods.ModChanged -= OnModChanged;
            _modHooked = false;
        }

        /// <summary>ModChanged can be raised off the UI thread; marshal before touching the art.</summary>
        private void OnModChanged(object? sender, ModPackage mod)
            => Dispatcher.UIThread.Post(ApplyAvatarArt);

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            Observe(ViewModel);
            RefreshAmbientState();
            CentrePortrait();
        }

        /// <summary>
        /// Subscribes to the live viewmodel so the asleep state can park the loop, and — more
        /// importantly — unsubscribes from the previous one. A hero that is re-pointed at a new
        /// companion must not leave a handler rooted in the old viewmodel.
        /// </summary>
        private void Observe(CompanionHeroCardViewModel? vm)
        {
            if (ReferenceEquals(_observed, vm)) return;

            if (_observed != null) _observed.PropertyChanged -= OnViewModelPropertyChanged;
            _observed = vm;
            if (_observed != null) _observed.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var name = e.PropertyName;
            bool all = string.IsNullOrEmpty(name);

            if (all || string.Equals(name, nameof(CompanionHeroCardViewModel.Portrait), StringComparison.Ordinal))
                CentrePortrait();

            if (all || string.Equals(name, nameof(CompanionHeroCardViewModel.IsCompanionEnabled), StringComparison.Ordinal))
                RefreshAmbientState();
        }

        /// <summary>Starts or parks the breathe to match the current state. Safe to call any time.</summary>
        public void RefreshAmbientState()
        {
            if (IsLoaded && ViewModel?.IsCompanionEnabled != false) StartAmbientLoop();
            else StopAmbientLoop();
        }

        /// <summary>
        /// Starts (or restarts) the portrait breathe. Idempotent. The animation itself is the
        /// <c>Ellipse.ring.breathe</c> style in the XAML (CmpPortraitBreatheStoryboard's numbers);
        /// the class is the clock.
        /// </summary>
        public void StartAmbientLoop()
        {
            if (!IsLoaded) return;
            this.FindControl<Ellipse>("PortraitRing")?.Classes.Add("breathe");
        }

        /// <summary>Stops the ambient loop and releases the clock.</summary>
        public void StopAmbientLoop()
            => this.FindControl<Ellipse>("PortraitRing")?.Classes.Remove("breathe");

        // =====================================================================================
        //  the portrait: mod repaint + optical centring
        // =====================================================================================

        /// <summary>
        /// Re-reads her bust and re-centres it in the ring. Called on Loaded and on every
        /// <see cref="CoreMods.ModChanged"/>. The WPF version calls
        /// <c>CompanionHeroRuntimeVm.Sync()</c> first, which is what re-reads her name, mod chip
        /// and flavour as well as the pose.
        /// </summary>
        internal void ApplyAvatarArt()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(ApplyAvatarArt); return; }

            ViewModel?.Sync();   // WPF: CompanionHeroRuntimeVm.Sync() first - name, mod chip, flavour
            try
            {
                // WPF's CompanionHeroRuntimeVm.LoadPortrait, minus the head types: the pose file
                // is a plain Resources-relative name, so the mod override is CoreModArt's and the
                // shipped copy is this head's avares:// one - both inside ModArt.TryLoad.
                // GetAvatarSetForLevel returns a constant 7 since level gating was removed
                // (AvatarTubeWindow.Avatar.cs), so the "< 1" branch does not need the player level.
                var set = CoreSettings.Current.SelectedAvatarSet;
                if (set < 1) set = 7;
                var name = set == 1 ? "avatar_pose1.png" : $"avatar{set}_pose1.png";

                if (ViewModel is { } vm) vm.Portrait = ModArt.TryLoad(name);
                CentrePortrait();
            }
            catch (Exception ex)
            {
                Log.Debug("Companion hero: avatar art refresh failed: {E}", ex.Message);
            }
        }

        /// <summary>
        /// Points the portrait brush at a square centred on the art's own opaque bounds - WPF's
        /// measured <c>Viewbox</c>, which on Avalonia is <c>ImageBrush.SourceRect</c>.
        ///
        /// <para>Avalonia refuses an <c>x:Name</c> on a brush (AVLN2000), so the brush is reached
        /// through the Ellipse that owns it. The Ellipse's name is resolved with
        /// <c>FindControl</c> because this control loads with <c>AvaloniaXamlLoader.Load</c> and
        /// the generated name fields are therefore never assigned.</para>
        /// </summary>
        private void CentrePortrait()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(CentrePortrait); return; }

            try
            {
                if (this.FindControl<Ellipse>("PortraitFill")?.Fill is not ImageBrush brush) return;
                brush.SourceRect = InkViewbox(ViewModel?.Portrait as Bitmap);
            }
            catch (Exception ex)
            {
                Log.Debug("Companion hero: portrait centring failed: {E}", ex.Message);
            }
        }

        /// <summary>
        /// The relative source rect that puts <paramref name="bmp"/>'s opaque bounds dead centre in
        /// a square viewport, at <see cref="PortraitInkFill"/> of its width. A SQUARE region, not
        /// the ink rectangle, so <c>Stretch=Uniform</c> into the round hole is exact; it may run
        /// off the source, which is what lets a tall thin figure sit in a circle un-widened.
        /// Falls back to the whole image whenever the art cannot be measured.
        /// </summary>
        internal static global::Avalonia.RelativeRect InkViewbox(Bitmap? bmp)
        {
            var whole = new global::Avalonia.RelativeRect(0, 0, 1, 1, global::Avalonia.RelativeUnit.Relative);
            if (bmp == null) return whole;

            double w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
            if (w <= 0 || h <= 0) return whole;

            if (OpaqueBounds(bmp) is not { } f) return whole;

            // fractions back onto the source's own pixel grid, where "square" means something
            double side = Math.Max(f.W * w, f.H * h) / PortraitInkFill;
            if (side <= 0) return whole;

            double cx = (f.X + f.W / 2) * w, cy = (f.Y + f.H / 2) * h;
            return new global::Avalonia.RelativeRect((cx - side / 2) / w, (cy - side / 2) / h, side / w, side / h,
                                    global::Avalonia.RelativeUnit.Relative);
        }

        /// <summary>
        /// The bounding box of everything that is not transparent padding, as fractions of the
        /// image. Null when it cannot be read, or when the art is transparent end to end.
        /// The scan runs on a <see cref="PortraitProbeWidth"/>-wide copy, which is what WPF's
        /// TransformedBitmap buys there. Avalonia's <c>Bitmap.CopyPixels</c> only fills a raw
        /// buffer in the bitmap's OWN format (there is no FormatConvertedBitmap equivalent), so a
        /// source with no alpha channel is answered null - "cannot be measured", i.e. the whole
        /// image - rather than scanned as if byte 3 meant something.
        /// </summary>
        private static (double X, double Y, double W, double H)? OpaqueBounds(Bitmap bmp)
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                var fmt = bmp.Format;
                if (fmt != PixelFormat.Bgra8888 && fmt != PixelFormat.Rgba8888) return null;

                int sw = bmp.PixelSize.Width, sh = bmp.PixelSize.Height;
                int pw = Math.Min(sw, PortraitProbeWidth);
                int ph = Math.Max(1, (int)Math.Round(sh * (pw / (double)sw)));

                using var probe = pw < sw
                    ? bmp.CreateScaledBitmap(new global::Avalonia.PixelSize(pw, ph))
                    : null;
                var src = (Bitmap?)probe ?? bmp;
                pw = src.PixelSize.Width;
                ph = src.PixelSize.Height;

                int stride = pw * 4, size = stride * ph;
                buffer = Marshal.AllocHGlobal(size);
                src.CopyPixels(new global::Avalonia.PixelRect(0, 0, pw, ph), buffer, size, stride);

                var row = new byte[stride];
                int minX = pw, minY = ph, maxX = -1, maxY = -1;
                for (int y = 0; y < ph; y++)
                {
                    Marshal.Copy(buffer + y * stride, row, 0, stride);
                    for (int x = 0; x < pw; x++)
                    {
                        if (row[x * 4 + 3] <= PortraitAlphaFloor) continue;   // alpha is byte 3 in both formats
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }

                if (maxX < minX || maxY < minY) return null;   // nothing opaque to centre on
                return ((double)minX / pw, (double)minY / ph,
                        (maxX - minX + 1) / (double)pw, (maxY - minY + 1) / (double)ph);
            }
            catch (Exception ex)
            {
                Log.Debug("Companion hero: opaque-bounds probe failed: {E}", ex.Message);
                return null;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>
    /// The view's data contract, in one concrete class: compiled bindings need one, and the WPF
    /// <c>ICompanionHeroCardVm</c> lives in the head and cannot cross. The body is WPF's
    /// <c>CompanionHeroRuntimeVm</c> (Views/Controls/Companion/Runtime/CompanionHeroRuntimeVm.cs):
    /// <see cref="Sync"/> re-reads settings, the active companion's progress and the mod; the
    /// quick actions go through the shell's room writes and the room's navigator, then re-sync.
    /// </summary>
    public sealed class CompanionHeroCardViewModel : INotifyPropertyChanged
    {
        private string _name = string.Empty, _modName = string.Empty, _flavor = string.Empty;
        private bool _isCompanionEnabled = true, _isAiLive, _isAiLocked, _isAwarenessOpen, _isMuted, _isCompanionShown = true;
        private string _aiPillText = string.Empty, _awarenessPillText = string.Empty;
        private int _level = 1;
        private double _xpFraction;
        private string _xpLabel = string.Empty, _nextLevelLabel = string.Empty, _chatShortcutHint = string.Empty;

        public CompanionHeroCardViewModel()
        {
            ChatCommand = AvatarTube.AvatarTubeWindow.OpenChatCommand;   // WPF App.AvatarWindow?.OpenChatInput()
            SwitchCommand = new RelayCommand(() => Navigator?.RevealWorkshop(CompanionRoomAnchors.WorkshopRosterCell));
            DetachCommand = new RelayCommand(() =>
            {
                CoreBark.NotifyUiAction("detach_companion");   // WPF MainWindow.Patreon.cs:1278
                // Popping out a switched-off companion wakes it, popped out, as the tray's Wake does
                // (WPF MainWindow.Patreon.cs:1287); it used to do nothing here.
                if (!CoreSettings.Current.AvatarEnabled) Shell?.WakeBambiUp();
                else AvatarTube.AvatarTubeWindow.ToggleDetachedSink?.Invoke();
            });
            ToggleMuteCommand = new RelayCommand(() => { Shell?.SetAvatarMuted(!IsMuted); Sync(); });
            ToggleShownCommand = new RelayCommand(() => { Shell?.SetAvatarEnabled(!IsCompanionShown); Sync(); });
            WakeCommand = new RelayCommand(() => { Shell?.SetAvatarEnabled(true); Sync(); });
            OpenEngineRoomCommand = new RelayCommand(() => Navigator?.RevealEngineRoom());
            FocusAwarenessCommand = new RelayCommand(() => Navigator?.FocusAwareness());
            Header = new CompanionHeaderViewModel(this);
            Sync();
        }

        /// <summary>The control the room seats this card in: the shell and the navigator are
        /// resolved through it, as WPF's CompanionRuntimeContext resolves its window.</summary>
        internal global::Avalonia.Visual? Host { get; set; }
        internal Windows.MainShellWindow? Shell => Host == null ? null : TopLevel.GetTopLevel(Host) as Windows.MainShellWindow;
        private ICompanionRoomNavigator? Navigator => Host as ICompanionRoomNavigator;

        /// <summary>WPF CompanionHeroRuntimeVm.SyncCore, minus the portrait (the view's
        /// <see cref="CompanionHeroCard.ApplyAvatarArt"/> owns that).</summary>
        public void Sync()
        {
            try
            {
                var s = CoreSettings.Current;
                var def = CompanionDefinition.GetById(s.ActiveCompanionId);
                var display = def.GetDisplayName(s.SlutModeEnabled);
                var neutral = CoreMods.IsCCPDefault ? CoreMods.ActiveModPackage?.Manifest.Identity?.CompanionName : null;
                Name = !string.IsNullOrWhiteSpace(neutral) ? neutral! : CoreMods.MakeModAware(display);
                ModName = CoreMods.ActiveModPackage?.Manifest.Name ?? string.Empty;
                Flavor = CoreMods.MakeModAware(def.Description);

                // CompanionService.GetProgress, read-only: an absent entry is a fresh level-1 companion.
                var p = s.CompanionProgressData.TryGetValue(s.ActiveCompanionId, out var saved)
                    ? saved : CompanionProgress.CreateNew((CompanionId)s.ActiveCompanionId);
                Level = p.Level;
                XpFraction = p.IsMaxLevel ? 1.0 : Math.Clamp(p.LevelProgress, 0, 1);
                XpLabel = p.IsMaxLevel ? Loc.Get("companion_hero_xp_complete") : $"{p.CurrentXP:F0} / {p.XPForNextLevel:F0} XP";
                NextLevelLabel = p.IsMaxLevel ? Loc.Get("companion_hero_max_level") : Loc.GetF("companion_hero_next_level_fmt", p.Level + 1);

                IsCompanionShown = s.AvatarEnabled;
                IsCompanionEnabled = IsCompanionShown;
                IsMuted = s.AvatarMuted;
                ChatShortcutHint = AvatarTube.AvatarTubeWindow.FormatChatShortcut();

                bool aiOn = s.AiChatEnabled;
                bool entitled = Header!.Sync();
                var provider = s.CompanionPrompt?.AiProvider ?? AiProviderType.Cloud;
                IsAiLocked = aiOn && provider == AiProviderType.Cloud && !entitled;
                IsAiLive = aiOn && !IsAiLocked;
                AiPillText = !aiOn ? Loc.Get("companion_hero_pill_ai_off")
                    : IsAiLocked ? Loc.Get("companion_hero_pill_ai_locked")
                    : provider switch
                    {
                        AiProviderType.Local => Loc.Get("label_ai_status_pill_local"),
                        AiProviderType.OpenAiCompatible => Loc.Get("label_ai_status_pill_custom"),
                        _ => Loc.Get("companion_hero_pill_ai_cloud")
                    };

                IsAwarenessOpen = s.AwarenessModeEnabled;
                AwarenessPillText = Loc.Get(IsAwarenessOpen ? "companion_hero_pill_eyes_broad" : "companion_hero_pill_eyes_closed");
            }
            catch (Exception ex)
            {
                // WPF CompanionRuntimeContext.Guarded: a failed read never takes the tab down.
                Log.Warning(ex, "Companion hero: sync failed");
            }
        }

        // ---- identity ----
        public string Name { get => _name; private set => Set(ref _name, value); }
        public string ModName { get => _modName; private set => Set(ref _modName, value); }
        public string Flavor { get => _flavor; private set => Set(ref _flavor, value); }
        private IImage? _portrait;

        /// <summary>
        /// Companion bust. Null renders the gradient placeholder disc. The view re-resolves it
        /// from the mod layer on Loaded, on tab show and on every <see cref="CoreMods.ModChanged"/>,
        /// and the change is what re-runs the optical centring.
        /// </summary>
        public IImage? Portrait
        {
            get => _portrait;
            set => Set(ref _portrait, value);
        }

        // ---- state ----
        public bool IsCompanionEnabled { get => _isCompanionEnabled; private set => Set(ref _isCompanionEnabled, value); }
        public bool IsAiLive { get => _isAiLive; private set => Set(ref _isAiLive, value); }
        public bool IsAiLocked { get => _isAiLocked; private set => Set(ref _isAiLocked, value); }
        public bool IsAwarenessOpen { get => _isAwarenessOpen; private set => Set(ref _isAwarenessOpen, value); }
        public string AiPillText { get => _aiPillText; private set => Set(ref _aiPillText, value); }
        public string AwarenessPillText { get => _awarenessPillText; private set => Set(ref _awarenessPillText, value); }
        public string AsleepCopy => Loc.Get("companion_hero_asleep_copy");

        // ---- daily mood token (Train 4): dormant on WPF too ----
        public bool IsMoodLive => false;
        public string MoodGlyph => "✧";
        public string MoodWord => Loc.Get("companion_hero_mood_asleep");
        public string MoodCaption => Loc.Get("companion_hero_mood_caption_dormant");

        // ---- progression ----
        public int Level { get => _level; private set => Set(ref _level, value); }
        public double XpFraction { get => _xpFraction; private set => Set(ref _xpFraction, value); }
        public string XpLabel { get => _xpLabel; private set => Set(ref _xpLabel, value); }
        public string NextLevelLabel { get => _nextLevelLabel; private set => Set(ref _nextLevelLabel, value); }

        // ---- quick actions ----
        public string ChatShortcutHint { get => _chatShortcutHint; private set => Set(ref _chatShortcutHint, value); }
        public bool IsMuted { get => _isMuted; private set => Set(ref _isMuted, value); }
        public bool IsCompanionShown { get => _isCompanionShown; private set => Set(ref _isCompanionShown, value); }

        public ICommand ChatCommand { get; }
        public ICommand SwitchCommand { get; }
        public ICommand DetachCommand { get; }
        public ICommand ToggleMuteCommand { get; }
        public ICommand ToggleShownCommand { get; }
        public ICommand OpenEngineRoomCommand { get; }
        public ICommand FocusAwarenessCommand { get; }
        public ICommand WakeCommand { get; }

        /// <summary>Z0 band. Null collapses it, for a host that draws its own page header.</summary>
        public CompanionHeaderViewModel? Header { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// Z0 — the header band: title, subtitle, tutorial chip and the AI-entitlement plate. WPF's
    /// <c>CompanionHeaderRuntimeVm</c>.
    /// </summary>
    public sealed class CompanionHeaderViewModel : INotifyPropertyChanged
    {
        private bool _hasAiAccess;

        public CompanionHeaderViewModel(CompanionHeroCardViewModel hero)
        {
            // WPF BtnCompanionTutorial_Click (MainWindow.Settings.cs:665): StartTutorial(Companion).
            TutorialCommand = new RelayCommand(() => CoreTutorial.Start("Companion"));
            OpenPatreonCommand = new RelayCommand(() => hero.Shell?.ShowTab("patreon"));
        }

        public string Title => Loc.Get("companion_header_title");
        public string Subtitle => Loc.Get("companion_header_subtitle");
        public string TutorialLabel => Loc.Get("companion_header_tutorial");
        public string AiPlateLabel => Loc.Get("companion_header_plate_ai");
        public string NextTierPlateLabel => Loc.Get("companion_header_plate_next");
        public string TeaserRibbonLabel => Loc.Get("companion_header_teaser");

        public bool HasAiAccess
        {
            get => _hasAiAccess;
            private set
            {
                if (_hasAiAccess == value) return;
                _hasAiAccess = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAiAccess)));
            }
        }

        /// <summary>WPF: <c>App.Patreon?.HasAiAccess == true || App.HasCloudIdentity</c>.</summary>
        internal bool Sync() => HasAiAccess = CoreAccount.HasPremiumAccess || !string.IsNullOrEmpty(CoreAccount.UnifiedUserId);

        public ICommand TutorialCommand { get; }
        public ICommand OpenPatreonCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // RelayCommand: the one AwarenessPrivacyView declares in this namespace (a lower layer of the
    // stack) is a superset of the one this file carried, so this file uses it.
}
