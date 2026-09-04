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

                // ponytail: the REST of Sync() - her name, mod chip and flavour - needs
                // ConditioningControlPanel/Views/Controls/Companion/Runtime/CompanionHeroRuntimeVm.cs,
                // which reads App.Companion, App.AvatarWindow and CompanionRuntimeContext.Navigator:
                // head navigation, not a mod lookup, so it cannot cross to Core as it stands.
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
    /// <c>ICompanionHeroCardVm</c> / <c>MockCompanionHeroCardVm</c> live in the head and cannot
    /// cross. Seeded with the mock's <c>Default()</c> exhibit: AI live, awareness on, mood token
    /// still dormant (pre-Train 4), header entitled. Every string comes through CCP.Core's
    /// <see cref="Loc"/> by the key the WPF mock/runtime uses, so a missing key shows as itself.
    /// </summary>
    public sealed class CompanionHeroCardViewModel : INotifyPropertyChanged
    {
        private bool _isMuted;
        private bool _isCompanionShown = true;

        public CompanionHeroCardViewModel()
        {
            ChatCommand = new RelayCommand(() => { });
            SwitchCommand = new RelayCommand(() => { });
            DetachCommand = new RelayCommand(() => { });
            ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
            ToggleShownCommand = new RelayCommand(() => IsCompanionShown = !IsCompanionShown);
            OpenEngineRoomCommand = new RelayCommand(() => { });
            FocusAwarenessCommand = new RelayCommand(() => { });
            WakeCommand = new RelayCommand(() => { });
            Header = new CompanionHeaderViewModel();
            // ponytail: every command above is a no-op; they deep-link into the Companion room
            // (Engine Room, Workshop roster, awareness cell, chat) which is head-owned navigation
            // with no Avalonia counterpart yet.
        }

        // ---- identity ----
        public string Name { get; init; } = "Bambi";
        public string ModName { get; init; } = "BAMBI SLEEP";
        public string Flavor { get; init; } = "Gains bonus XP from Pink Filter intensity. Currently plotting something.";
        private IImage? _portrait;

        /// <summary>
        /// Companion bust. Null renders the gradient placeholder disc. Settable and notifying, not
        /// <c>init</c>: the view re-resolves it from the mod layer on Loaded and on every
        /// <see cref="CoreMods.ModChanged"/>, and the change is what re-runs the optical centring.
        /// </summary>
        public IImage? Portrait
        {
            get => _portrait;
            set => Set(ref _portrait, value);
        }

        // ---- state ----
        public bool IsCompanionEnabled { get; init; } = true;
        public bool IsAiLive { get; init; } = true;
        public bool IsAiLocked { get; init; }
        public bool IsAwarenessOpen { get; init; } = true;
        public string AiPillText { get; init; } = Loc.Get("companion_hero_pill_ai_cloud");
        public string AwarenessPillText { get; init; } = Loc.Get("companion_hero_pill_eyes_broad");
        public string AsleepCopy { get; init; } = Loc.Get("companion_hero_asleep_copy");

        // ---- daily mood token (Train 4) ----
        public bool IsMoodLive { get; init; }
        public string MoodGlyph { get; init; } = "✧";
        public string MoodWord { get; init; } = Loc.Get("companion_hero_mood_asleep");
        public string MoodCaption { get; init; } = Loc.Get("companion_hero_mood_caption_dormant");

        // ---- progression (placeholder numbers = the WPF mock's artboard) ----
        public int Level { get; init; } = 41;
        public double XpFraction { get; init; } = 0.62;
        /// <summary>Interpolated in the runtime VM too, never a loc key.</summary>
        public string XpLabel { get; init; } = "341 / 550 XP";
        public string NextLevelLabel { get; init; } = Loc.GetF("companion_hero_next_level_fmt", 42);

        // ---- quick actions ----
        public string ChatShortcutHint { get; init; } = "Ctrl+T";

        public bool IsMuted
        {
            get => _isMuted;
            set => Set(ref _isMuted, value);
        }

        public bool IsCompanionShown
        {
            get => _isCompanionShown;
            set => Set(ref _isCompanionShown, value);
        }

        public ICommand ChatCommand { get; }
        public ICommand SwitchCommand { get; }
        public ICommand DetachCommand { get; }
        public ICommand ToggleMuteCommand { get; }
        public ICommand ToggleShownCommand { get; }
        public ICommand OpenEngineRoomCommand { get; }
        public ICommand FocusAwarenessCommand { get; }
        public ICommand WakeCommand { get; }

        /// <summary>Z0 band. Null collapses it, for a host that draws its own page header.</summary>
        public CompanionHeaderViewModel? Header { get; init; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// Z0 — the header band: title, subtitle, tutorial chip and the AI-entitlement plate. The
    /// shape of the WPF <c>ICompanionHeaderVm</c>, seeded from <c>MockCompanionHeaderVm.Entitled()</c>.
    /// </summary>
    public sealed class CompanionHeaderViewModel : INotifyPropertyChanged
    {
        public CompanionHeaderViewModel()
        {
            TutorialCommand = new RelayCommand(() => { });
            OpenPatreonCommand = new RelayCommand(() => { });
            // ponytail: no-ops — the tutorial and the Patreon tab are head-owned navigation.
        }

        public string Title { get; init; } = Loc.Get("companion_header_title");
        public string Subtitle { get; init; } = Loc.Get("companion_header_subtitle");
        public string TutorialLabel { get; init; } = Loc.Get("companion_header_tutorial");
        public bool HasAiAccess { get; init; } = true;
        public string AiPlateLabel { get; init; } = Loc.Get("companion_header_plate_ai");
        public string NextTierPlateLabel { get; init; } = Loc.Get("companion_header_plate_next");
        public string TeaserRibbonLabel { get; init; } = Loc.Get("companion_header_teaser");

        public ICommand TutorialCommand { get; }
        public ICommand OpenPatreonCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    /// <summary>The one command shape these placeholders need: run a delegate, always enabled.</summary>
    // RelayCommand: the one AwarenessPrivacyView declares in this namespace (a lower layer of the
    // stack) is a superset of the one this file carried, so this file uses it.
}
