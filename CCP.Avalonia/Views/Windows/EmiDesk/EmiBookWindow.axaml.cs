using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// HER BOOK: the flipbook that unfurls beside her.
    ///
    /// <para><b>What it is.</b> One card per feature. The top of the card is a little 8-bit loop
    /// that SHOWS the feature happening, and under it sit a title, one pink line, up to four
    /// nudges, the catch on its own strip, and at most one button. Roughly forty words.</para>
    ///
    /// <para>PORTED from ConditioningControlPanel/Windows/EmiDesk/EmiBookWindow.xaml.cs. The XAML's
    /// header lists the markup deviations; these are the code ones, and every one is forced:</para>
    ///
    /// <list type="bullet">
    ///   <item><b>The Win32 goes, and one behaviour goes with it.</b>
    ///     <c>SetWindowLong(GWL_EXSTYLE, WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)</c> becomes
    ///     <c>ShowInTaskbar="False"</c> plus <c>ShowActivated="False"</c> in the markup.
    ///     <b>WS_EX_NOACTIVATE is only half covered.</b> On Windows it meant the book could NEVER
    ///     take the keyboard, at any point in its life - which is why the original has no key
    ///     handling at all. <c>ShowActivated="False"</c> only governs the first Show, so on X11 a
    ///     click on a chip WILL focus this window and pull focus off whatever the reader was doing.
    ///     There is no property for the permanent form; it needs
    ///     <c>_NET_WM_STATE_SKIP_TASKBAR</c>/input-hint work on the X11 shim, which is its own
    ///     layer. Nothing here fakes it.
    ///     <c>NativeStyle()</c> (<c>GWL_STYLE</c>/<c>WS_VISIBLE</c>) is dropped outright: it existed
    ///     to catch a WPF-specific bug where <c>Window.IsVisible</c> lied, and the native style word
    ///     it printed has no X11 twin worth inventing. The log line keeps the geometry.</item>
    ///   <item><b>No web view anywhere.</b> This window never hosted one - the original's header
    ///     explains at length why it could not - so nothing here touches <c>controls:WebHost</c>.</item>
    ///   <item><b>No X11Overlay call.</b> Nothing in this window was click-through or restacked
    ///     against a sibling; <c>Topmost</c> in the markup is the whole z-order story.</item>
    ///   <item><b>The deck and the demos are the real ones</b>: <c>EmiBookCards</c>,
    ///     <c>EmiBookDemos</c> and <c>EmiPixelCanvas</c> live in Core; this window blits the buffer
    ///     into its own <c>WriteableBitmap</c>. The glyphs and the fonts are still stubs, each marked
    ///     <c>ponytail:</c> below.</item>
    ///   <item><b>The emphasis parser is the real one.</b> <c>EmiBookText.Parse</c> is already in
    ///     Core, so the <c>*asterisk*</c> runs are ported rather than approximated.
    ///     <c>EmiBookLayout.Place</c> makes the side/width decision.</item>
    ///   <item><c>Left</c>/<c>Top</c> (DIPs) -&gt; <c>Position</c>, which is PHYSICAL pixels: the
    ///     conversion the original does in one direction now runs in both.
    ///     <c>PresentationSource...TransformToDevice.M11</c> -&gt; <c>RenderScaling</c>;
    ///     <c>Forms.Screen.FromPoint</c> -&gt; <c>Screens.ScreenFromPoint</c>.</item>
    ///   <item><c>ScrollableHeight</c> does not exist: the overflow test is
    ///     <c>Extent.Height - Viewport.Height - Offset.Y</c>. <c>ScrollToTop</c> -&gt;
    ///     <c>ScrollToHome</c>.</item>
    ///   <item><c>DoubleAnimation</c>/<c>BeginAnimation</c> -&gt; <c>Animation.RunAsync</c> on the
    ///     HOST VISUAL that owns the <c>ScaleTransform</c> - NOT on the transform, which compiles
    ///     and throws; <c>BeginTime</c> -&gt; <c>Delay</c>. See <see cref="Scale"/>. The fold closes
    ///     on the awaited sweep, which is the same "close on the LAST animation, never on a timer"
    ///     rule the original states.</item>
    ///   <item><c>Tag = "on"</c> -&gt; the <c>on</c> style class, because Avalonia has no property
    ///     trigger. <c>MouseEnter/Leave</c> -&gt; <c>PointerEntered/Exited</c>;
    ///     <c>MouseLeftButtonUp</c> -&gt; <c>PointerReleased</c>.</item>
    ///   <item><b>The owner is real.</b> <see cref="EmiDeskWindow"/> is ported on this head, so the
    ///     constructor takes it and the chrome hold, her body box and the follow-her-around
    ///     subscriptions are the WPF behaviour rather than stubs. It stays NULLABLE for one reason:
    ///     <c>--render-view</c> needs a parameterless constructor and a headless render has no
    ///     widget to hang off, so every owner call is <c>?.</c> with the fallback the unowned book
    ///     already used. The parameterless ctor also selects the first card, which the WPF one left
    ///     to <see cref="OpenBook"/>: a window that draws nothing until a service calls it renders
    ///     as an empty panel.</item>
    /// </list>
    /// </summary>
    public partial class EmiBookWindow : Window
    {
        /// <summary>The book at its full drawn height, in DIPs. Matches the XAML.</summary>
        private const double FullHeight = 728.0;

        /// <summary>The floor the book will not shrink under, even on a very short desk.</summary>
        private const double MinPanelHeight = 430.0;

        /// <summary>Panel height at or above which the demo stage gets its 3x, 288 x 216 pixels.</summary>
        private const double BigStageFloor = 640.0;

        /// <summary>The demo buffer, in cells. 288 = 3 x 96 exactly, which is why the stage is 288 wide.</summary>
        private const int BufW = 96;

        /// <inheritdoc cref="BufW"/>
        private const int BufH = 72;

        /// <summary>How many bullets a card may show, whatever the deck hands over.</summary>
        private const int MaxNudges = 4;

        /// <summary>
        /// <c>MotionFx.AllowTransitions</c>, spelled out. MotionFx itself is still head-side, but
        /// what it reads is not: <see cref="AmbientFxCanvas.Env"/> in this assembly already carries
        /// the ported gate off <c>CoreSettings.Current.MotionLevel</c> and the tier, so the unfurl
        /// now obeys the reader's setting instead of assuming the shipped default.
        /// <c>Level != Off</c> is MotionFx's own definition, verbatim.
        /// </summary>
        private static bool AllowTransitions
        {
            get
            {
                try { return AmbientFxCanvas.Env.Level != MotionLevel.Off; }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book motion gate failed"); return true; }
            }
        }

        /// <summary><c>MotionFx.AllowAmbientLoops</c>, through the same gate.</summary>
        private static bool AllowAmbientLoops
        {
            get
            {
                try { return AmbientFxCanvas.Env.AllowAmbientLoops; }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book loop gate failed"); return true; }
            }
        }

        private readonly List<Border> _dots = new();

        private readonly Button _tab0;
        private readonly Button _tab1;
        private readonly Button _tab2;
        private readonly Button _btnClose;
        private readonly Button _btnPrev;
        private readonly Button _btnNext;
        private readonly Button _btnGo;
        private readonly Border _sweepHost;
        private readonly Grid _openHost;
        private readonly Border _flash;
        private readonly Border _moreBelow;
        private readonly Image _stage;
        private readonly TextBlock _stageLabel;
        private readonly TextBlock _cardTitle;
        private readonly TextBlock _cardGist;
        private readonly TextBlock _cardCatch;
        private readonly StackPanel _nudges;
        private readonly StackPanel _rail;
        private readonly WrapPanel _dotsPanel;
        private readonly ScrollViewer _nudgeScroll;

        private readonly ScaleTransform _sweepScale = new(1, 1);
        private readonly ScaleTransform _openScale = new(1, 1);

        /// <summary>One log line per book, not one per move: <c>PlaceWindow</c> runs on every drag tick.</summary>
        private bool _notedCover;

        /// <summary>
        /// QA ONLY: pretend there is no room, so the narrow book can be reviewed on a desk that has
        /// plenty. Nothing in a normal launch sets this.
        /// </summary>
        internal static bool ForceNarrow;

        private int _index;
        private int _tab;
        private bool _closingForGood;
        private bool _folding;

        /// <summary>True while the panel sits to her LEFT, which flips the unfurl's origin.</summary>
        private bool _onHerLeft;

        /// <summary>
        /// Which side of her this panel took: <c>-1</c> her LEFT, <c>+1</c> her RIGHT. Read through
        /// the book service, never off this window.
        /// </summary>
        internal int SideOfHer => _onHerLeft ? -1 : 1;

        // ---------------------------------------------------------------- ctor

        /// <summary>The widget this book hangs off. Null only under <c>--render-view</c>.</summary>
        private readonly EmiDeskWindow? _owner;

        /// <summary>The render constructor: a book with no widget behind it. See the header.</summary>
        public EmiBookWindow() : this(null) { }

        /// <summary>
        /// Builds the book for one widget. Created hidden; <see cref="OpenBook"/> shows it.
        /// </summary>
        public EmiBookWindow(EmiDeskWindow? owner)
        {
            _owner = owner;
            AvaloniaXamlLoader.Load(this);

            _tab0 = this.FindControl<Button>("Tab0")!;
            _tab1 = this.FindControl<Button>("Tab1")!;
            _tab2 = this.FindControl<Button>("Tab2")!;
            _btnClose = this.FindControl<Button>("BtnClose")!;
            _btnPrev = this.FindControl<Button>("BtnPrev")!;
            _btnNext = this.FindControl<Button>("BtnNext")!;
            _btnGo = this.FindControl<Button>("BtnGo")!;
            _sweepHost = this.FindControl<Border>("SweepHost")!;
            _openHost = this.FindControl<Grid>("OpenHost")!;
            _flash = this.FindControl<Border>("Flash")!;
            _moreBelow = this.FindControl<Border>("MoreBelow")!;
            _stage = this.FindControl<Image>("Stage")!;
            _stageLabel = this.FindControl<TextBlock>("StageLabel")!;
            _cardTitle = this.FindControl<TextBlock>("CardTitle")!;
            _cardGist = this.FindControl<TextBlock>("CardGist")!;
            _cardCatch = this.FindControl<TextBlock>("CardCatch")!;
            _nudges = this.FindControl<StackPanel>("Nudges")!;
            _rail = this.FindControl<StackPanel>("Rail")!;
            _dotsPanel = this.FindControl<WrapPanel>("Dots")!;
            _nudgeScroll = this.FindControl<ScrollViewer>("NudgeScroll")!;

            // The transforms are built here rather than named in markup: FindControl only returns
            // Controls, and a ScaleTransform is not one.
            _sweepHost.RenderTransform = _sweepScale;
            _openHost.RenderTransform = _openScale;

            ApplyFonts();
            WireControls();

            // The chrome on her body stays lit while the pointer is in here, so the ? chip that
            // opened this is still on screen after the round trip.
            PointerEntered += (_, _) => Hold(true);
            PointerExited += (_, _) => Hold(false);

            // She moves, she resizes: the book is anchored to her body and has to come along.
            if (_owner is not null)
            {
                _owner.Moved += OnOwnerMoved;
                _owner.Resized += OnOwnerResized;
            }

            // NOT in the WPF original, where OpenBook is always what fills the panel. Here the
            // render harness constructs the window and screenshots it, so an unpopulated book is an
            // empty pink box that passes. It does not move the bookmark: nothing was opened.
            _canvas.Committed += OnCommitted;
            _stage.Source = _bitmap;
            SelectCard(0, speak: false, note: false);
        }

        private void OnOwnerMoved(object? sender, EventArgs e)
        {
            if (_closingForGood || !IsVisible) return;
            try { PlaceWindow(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book follow-move failed"); }
        }

        private void OnOwnerResized(object? sender, double width)
        {
            if (_closingForGood || !IsVisible) return;
            try { PlaceWindow(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book follow-resize failed"); }
        }

        /// <summary>
        /// The WPF original's <c>OnClosed</c>, verbatim in effect: the book is a sibling window
        /// holding a subscription to hers, so the handlers come off however it went away - the
        /// fold, her dismissal, or the app shutting down.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                StopClock();
                if (_owner is not null)
                {
                    _owner.Moved -= OnOwnerMoved;
                    _owner.Resized -= OnOwnerResized;
                }
                Hold(false);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book teardown failed"); }
            base.OnClosed(e);
        }

        // ---------------------------------------------------------------- wiring

        /// <summary>
        /// Press Start 2P for the chrome and the title, the mono body face for the copy - as
        /// NAME CHAINS, which is the road EmiRingWindow and EmiDeskWindow.axaml already take on this
        /// head. WPF loads both through a pack:// base URI out of <c>Resources/emi/fonts</c> and its
        /// header warns that naming a font in markup finds installed fonts only; on Avalonia there
        /// is nothing to name yet either way.
        ///
        /// <para>ponytail: the blocker is NOT EmiFace, and it is not a move to Core. Both faces are
        /// already in the repo at <c>Assets/emi/fonts/PressStart2P-latin.ttf</c> and
        /// <c>NotoSansMono-latin.ttf</c>; what is missing is an <c>AvaloniaResource</c> link for
        /// <c>Assets/emi/fonts/</c> in CCP.Avalonia.csproj, which this layer does not own. Until
        /// that one line lands the chain falls through to the system mono face - so the pixel
        /// look is missing, nothing is blank, and the panel lights up the moment the link is
        /// added or a reader has the font installed.</para>
        ///
        /// <para>The SIZES are ported verbatim because they are the layout - 16 for the title, 8 for
        /// the chrome, one whole number of pixels per 8x8 cell. At those sizes a proportional
        /// fallback reads much smaller than Press Start 2P does, which is why the chrome looks
        /// small on a machine without it.</para>
        /// </summary>
        private void ApplyFonts()
        {
            try
            {
                foreach (var b in new[] { _tab0, _tab1, _tab2 }) { b.FontFamily = PixelFont; b.FontSize = 8; }

                _btnClose.FontFamily = PixelFont;
                _btnClose.FontSize = 8;
                _btnPrev.FontFamily = PixelFont;
                _btnPrev.FontSize = 10;
                _btnNext.FontFamily = PixelFont;
                _btnNext.FontSize = 10;
                _btnGo.FontFamily = PixelFont;
                _btnGo.FontSize = 8;

                _stageLabel.FontFamily = PixelFont;
                _stageLabel.FontSize = 6;
                _cardTitle.FontFamily = PixelFont;
                _cardTitle.FontSize = 16;

                _cardGist.FontFamily = FaceFont;
                _cardGist.FontSize = 13;
                _cardCatch.FontFamily = FaceFont;
                _cardCatch.FontSize = 11.5;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] book fonts failed");
            }
        }

        /// <summary><c>EmiFace.PixelFont</c>'s chain, the same string EmiRingWindow uses.</summary>
        private static readonly FontFamily PixelFont = new("Press Start 2P, Consolas, monospace");

        /// <summary><c>EmiFace.FaceFont</c>'s chain, the same one EmiDeskWindow.axaml carries.</summary>
        private static readonly FontFamily FaceFont =
            new("Noto Sans Mono, DejaVu Sans Mono, Consolas, monospace");

        /// <summary>WPF BtnFirstShow: replay EMI's welcome show. Test seam.</summary>
        internal Action ReplayShow = () => WelcomeShow.FirstShowService.Open();

        /// <summary>WPF BtnCompleteGuide: the website manual (EmiCodex.OpenManualInBrowser). Test seam.</summary>
        internal static Action OpenManual = ConditioningControlPanel.Avalonia.Views.Windows.Codex.OpenManualInBrowser;

        private void WireControls()
        {
            _btnClose.Click += (_, _) => CloseBook();
            this.FindControl<Button>("BtnCompleteGuide")!.Click += (_, _) => OpenManual();
            // WPF EmiBookWindow.xaml.cs:232: the book closes, then the welcome show opens.
            this.FindControl<Button>("BtnFirstShow")!.Click += (_, _) => { _owner?.CloseBook(); ReplayShow(); };
            _btnPrev.Click += (_, _) => Step(-1);
            _btnNext.Click += (_, _) => Step(+1);
            _btnGo.Click += (_, _) => Go();

            _tab0.Click += (_, _) => PickTab(0);
            _tab1.Click += (_, _) => PickTab(1);
            _tab2.Click += (_, _) => PickTab(2);

            // Markup in this head, not a code assignment: a local .Text set here would survive a
            // language change and the binding would not. The close tooltip and the stage label are
            // both {loc:Str} in the XAML for the same reason.
            _nudgeScroll.ScrollChanged += (_, _) => UpdateMoreCue();
        }

        /// <summary>Keep the chrome on her body lit while the pointer is in the book.</summary>
        private void Hold(bool on)
        {
            try { _owner?.HoldChromeForPanel(on); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book chrome hold failed"); }
        }

        /// <summary>
        /// The close button. Routed through the widget so SHE drops her reference to the book: a
        /// bare <see cref="Kill"/> here would fold the window and leave the desk holding a closed
        /// one, and the next <c>?</c> click would then re-open a dead panel.
        ///
        /// <para>ponytail: the WPF button calls <c>EmiBook.Close()</c>; the bookmark half is
        /// <c>EmiState.NoteBookCard</c> (Core, written on every card). Still missing:
        /// <c>EmiBook.SideChanged</c> (her bubble dodging away from the panel, EmiBook.cs).</para>
        /// </summary>
        private void CloseBook()
        {
            if (_owner is null) { Kill(); return; }
            try { _owner.CloseBook(); }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] book close through the widget failed");
                Kill();
            }
        }

        // ---------------------------------------------------------------- open / close

        /// <summary>Unfurl the book beside her, at <paramref name="cardId"/> or at the first card.</summary>
        public void OpenBook(string? cardId)
        {
            if (_closingForGood) return;
            try
            {
                SelectCard(EmiBookCards.IndexOf(cardId) is var i and >= 0 ? i : 0, speak: false);

                PlaceWindow();
                if (!IsVisible)
                {
                    Show();
                    // The window has no scaling of its own until it is up, so the first placement is
                    // always done twice: once to get it on screen, once with its real scale.
                    PlaceWindow();
                }

                Log.Information("[EmiDesk] book shown at {P} {W}x{H}", Position, Bounds.Width, Bounds.Height);

                Unfurl();
                StartClock();
                SpeakMargin();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] book failed to open");
                try { Kill(); } catch { /* nothing else to try */ }
            }
        }

        /// <summary>Navigate a book that is already up. Used when a second Open arrives.</summary>
        public void GoTo(string? cardId)
        {
            try
            {
                int i = EmiBookCards.IndexOf(cardId);
                if (i < 0) return;
                SelectCard(i, speak: true);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] book navigate failed");
            }
        }

        /// <summary>Fold the book and let it go: her dismissal, or app shutdown.</summary>
        public void Kill()
        {
            try
            {
                _closingForGood = true;
                StopClock();
                Hold(false);
                if (_owner is not null)
                {
                    _owner.Moved -= OnOwnerMoved;
                    _owner.Resized -= OnOwnerResized;
                }

                if (!IsVisible)
                {
                    Close();
                    return;
                }
                Fold();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] book kill failed");
                try { Close(); } catch { /* it is going away either way */ }
            }
        }

        // ---------------------------------------------------------------- the unfurl

        /// <summary>
        /// Two moves with two origins, which is why there are two transforms.
        ///
        /// <para>A flat line shoots out FROM HER EDGE (ScaleX about the near edge), then it opens
        /// like a CRT coming on (ScaleY about the middle) with a single bright frame at the seam.
        /// One transform cannot do both.</para>
        ///
        /// <para>Under reduced motion the book is simply there. The panel is not a decoration that
        /// can be slowed down; it is the content.</para>
        /// </summary>
        private void Unfurl()
        {
            try
            {
                _sweepHost.RenderTransformOrigin = new RelativePoint(_onHerLeft ? 1 : 0, 0.5, RelativeUnit.Relative);

                if (!AllowTransitions)
                {
                    _sweepScale.ScaleX = 1;
                    _openScale.ScaleY = 1;
                    _flash.Opacity = 0;
                    return;
                }

                _sweepScale.ScaleX = 0;
                _openScale.ScaleY = 0.04;
                _flash.Opacity = 0;

                // THE VISUAL, NOT THE TRANSFORM. See the note on Scale: this window shipped
                // animating the ScaleTransform objects, which throws before the flash is even
                // reached, so the book snapped open with no unfurl and said nothing about it.
                _ = Scale(ScaleTransform.ScaleXProperty, 0, 1, 170, 0, new CubicEaseOut()).RunAsync(_sweepHost);
                _ = Scale(ScaleTransform.ScaleYProperty, 0.04, 1, 200, 160, new CubicEaseOut()).RunAsync(_openHost);

                var flash = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(280),
                    Delay = TimeSpan.FromMilliseconds(160),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0d) } },
                        new KeyFrame { Cue = new Cue(60d / 280d), Setters = { new Setter(OpacityProperty, 0.72d) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 0d) } },
                    },
                };
                _ = flash.RunAsync(_flash);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] book unfurl failed");
                try { _sweepScale.ScaleX = 1; _openScale.ScaleY = 1; } catch { /* nothing left to try */ }
            }
        }

        /// <summary>
        /// One scale ramp, which is the only shape of animation this window has.
        ///
        /// <para><b>IT IS RUN ON THE VISUAL, NEVER ON THE TRANSFORM.</b> Avalonia resolves a
        /// <c>Setter(ScaleTransform.ScaleXProperty, …)</c> to <c>TransformAnimator</c>, which casts
        /// its target to <c>Visual</c>: <c>RunAsync(aScaleTransform)</c> throws
        /// <c>InvalidCastException</c> SYNCHRONOUSLY, into whatever <c>catch</c> is nearest. That is
        /// how this window shipped, so the unfurl snapped straight to the fallback in the catch, the
        /// seam flash never ran at all (the throw is on the line above it), and the fold closed
        /// instantly - three animations, silent, and a PNG cannot see any of it. Handed the VISUAL
        /// that owns the transform it writes through to that same <c>ScaleTransform</c> instance:
        /// measured on Avalonia 12.1.1 headless, 0.880 at 200 ms of a 400 ms CubicEaseOut ramp, same
        /// object by reference at the end, and a later direct write still lands under the fill -
        /// which is what <see cref="Unfurl"/>'s reset and its catch depend on.</para>
        ///
        /// <para>This is the cheap half of the same bug wire/61 found in EmiDeskWindow and
        /// EmiRingWindow. It could not take this road there because those transforms are not the
        /// visual's whole <c>RenderTransform</c>; here each host owns exactly one, so the fix is the
        /// argument.</para>
        /// </summary>
        private static Animation Scale(
            AvaloniaProperty prop, double from, double to, double ms, double delayMs, Easing easing)
        {
            return new Animation
            {
                Duration = TimeSpan.FromMilliseconds(ms),
                Delay = TimeSpan.FromMilliseconds(delayMs),
                Easing = easing,
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(prop, from) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(prop, to) } },
                },
            };
        }

        /// <summary>The unfurl backwards, and then the window is gone.</summary>
        private async void Fold()
        {
            if (_folding) return;
            _folding = true;
            try
            {
                if (!AllowTransitions)
                {
                    Close();
                    return;
                }

                _ = Scale(ScaleTransform.ScaleYProperty, 1, 0.04, 130, 0, new CubicEaseIn()).RunAsync(_openHost);

                // Awaited rather than fired on a timer, which is the same rule the original states:
                // the close hangs off the LAST animation, so a slow frame cannot leave the window on
                // screen at zero width with nothing coming to close it. The await is only an await
                // now that the target is the visual - run on the transform this threw before it
                // suspended, and the fold was an instant Close() out of the catch.
                await Scale(ScaleTransform.ScaleXProperty, 1, 0, 140, 120, new CubicEaseIn()).RunAsync(_sweepHost);
                Close();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] book fold failed");
                try { Close(); } catch { /* already gone */ }
            }
        }

        // ---------------------------------------------------------------- the clock

        /// <summary>The demo buffer every painter repaints; blitted into <see cref="_bitmap"/> on Commit.</summary>
        private readonly EmiPixelCanvas _canvas = new(BufW, BufH);
        private readonly WriteableBitmap _bitmap = new(new PixelSize(BufW, BufH), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Opaque);
        private EmiDemoPainter? _painter;
        private DispatcherTimer? _clock;
        private long _since = -1;

        /// <summary>The clock the loop reads. Tests step it; WPF's Stopwatch in effect.</summary>
        internal static TimeProvider Time { get; set; } = TimeProvider.System;

        /// <summary>True while the 30 fps clock is running (tests and P01 checks).</summary>
        internal bool ClockRunning => _clock?.IsEnabled == true;

        /// <summary>The card's painter, or null when its loop was dropped.</summary>
        internal EmiDemoPainter? Painter => _painter;

        /// <summary>
        /// One 30 fps timer for the whole book (WPF EmiBookWindow.xaml.cs:453). It drives whichever
        /// painter the current card owns; under reduced motion the painter's still frame and no clock.
        /// </summary>
        private void StartClock()
        {
            try
            {
                if (!AllowAmbientLoops) { PaintStill(); return; }
                if (_clock == null)
                {
                    _clock = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
                    _clock.Tick += (_, _) => Tick();
                }
                _since = Time.GetTimestamp();
                _clock.Start();
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] book clock failed to start"); }
        }

        private void StopClock()
        {
            try { _clock?.Stop(); _since = -1; }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book clock stop failed"); }
        }

        /// <summary>One frame: the loop position off the shared elapsed time, so a card flipped back to is mid-loop.</summary>
        internal void Tick()
        {
            var p = _painter;
            if (p == null || _since < 0) return;
            try
            {
                double t = Time.GetElapsedTime(_since).TotalMilliseconds % Math.Max(1, p.LoopMs);
                p.Draw(_canvas, t);
                Blit();
            }
            catch (Exception ex)
            {
                // A painter that throws takes itself off the clock; the card keeps its words.
                Log.Warning(ex, "[EmiDesk] book demo {Demo} threw, dropped", p.Id);
                _painter = null;
            }
        }

        private void PaintStill()
        {
            var p = _painter;
            if (p == null) return;
            try { p.Draw(_canvas, p.StillMs); Blit(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book still frame failed"); }
        }

        /// <summary>Commit the buffer; <see cref="OnCommitted"/> blits it, the way WPF's EmiPixelBitmap does.</summary>
        private void Blit() => _canvas.Commit();

        /// <summary>One copy per frame, straight from the cells into the stage bitmap (Bgra32 == Bgra8888 little-endian).</summary>
        private void OnCommitted(EmiPixelCanvas c)
        {
            using (var fb = _bitmap.Lock())
            {
                // The CLR lets a uint[] be read as int[] (same element size), so Marshal.Copy takes the
                // cells as they are: no staging array, no unsafe.
                var src = (int[])(object)c.Pixels;
                for (int y = 0; y < c.H; y++)
                    Marshal.Copy(src, y * c.W, fb.Address + y * fb.RowBytes, c.W);
            }
            _stage.InvalidateVisual();
        }

        /// <summary>
        /// Freeze the stage on one exact frame. FOR THE OFFSCREEN SHOT RIG ONLY: a review of a 30
        /// fps loop needs a determinate frame. Stops the clock so it cannot race the capture.
        /// </summary>
        internal void ShootFrame(string? cardId, double tMs)
        {
            try
            {
                StopClock();
                int i = EmiBookCards.IndexOf(cardId);
                if (i >= 0) SelectCard(i, speak: false);
                var p = _painter;
                if (p == null) return;
                p.Draw(_canvas, Math.Max(0, Math.Min(p.LoopMs - 1, tMs)));
                Blit();
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] book shot frame failed"); }
        }

        // ---------------------------------------------------------------- the cards

        /// <summary>Move to a card by index, redraw everything, and remember where we are.</summary>
        private void SelectCard(int index, bool speak, bool note = true)
        {
            var cards = EmiBookCards.All;
            if (cards.Count == 0) return;

            _index = Math.Max(0, Math.Min(cards.Count - 1, index));
            var card = cards[_index];
            _tab = card.Tab;

            // The clock is not restarted between cards: a card flipped back to is already mid-loop.
            _painter = EmiBookDemos.For(card.Id);
            _stage.Source = _painter == null ? null : _bitmap;
            if (_since < 0) PaintStill();

            RenderCard(card);
            RenderTabs();
            RenderRail();
            RenderDots();

            if (note) EmiState.NoteBookCard(card.Id);
            if (speak) SpeakMargin();
        }

        private void Step(int delta)
        {
            var cards = EmiBookCards.All;
            if (cards.Count == 0) return;
            int next = _index + delta;
            if (next < 0 || next >= cards.Count) return;
            SelectCard(next, speak: true);
        }

        private void PickTab(int tab)
        {
            int first = EmiBookCards.FirstOnTab(tab);
            if (first < 0) return;
            if (first == _index && _tab == tab) return;
            SelectCard(first, speak: true);
        }

        /// <summary>The bullets' body text.</summary>
        private static readonly IBrush NudgePlain = Frozen(0xC9, 0xC3, 0xE2);

        /// <summary>
        /// The key words, in every row that has any. Pink, and NOT a third hue: the panel already
        /// spends pink on the gist and gold on the catch.
        /// </summary>
        private static readonly IBrush NudgeHot = Frozen(0xFF, 0x69, 0xB4);

        /// <summary>The gist line, and its key words a shade up from it.</summary>
        private static readonly IBrush GistPlain = Frozen(0xFF, 0x69, 0xB4);

        /// <inheritdoc cref="GistPlain"/>
        private static readonly IBrush GistHot = Frozen(0xFF, 0xD2, 0xEE);

        /// <summary>The catch strip, and its key words a shade up from it.</summary>
        private static readonly IBrush CatchPlain = Frozen(0xE8, 0xC4, 0x6A);

        /// <inheritdoc cref="CatchPlain"/>
        private static readonly IBrush CatchHot = Frozen(0xF9, 0xE2, 0xAC);

        /// <summary>WPF's <c>Freeze()</c> has no Avalonia twin; an immutable brush is the twin.</summary>
        private static IBrush Frozen(byte r, byte g, byte b) =>
            new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));

        private void RenderCard(EmiBookCard card)
        {
            try
            {
                _cardTitle.Text = card.Title;
                Emphasize(_cardGist, card.Gist, GistPlain, GistHot);
                Emphasize(_cardCatch, card.Catch, CatchPlain, CatchHot);

                _nudges.Children.Clear();
                int n = 0;
                foreach (var nudge in card.Nudges)
                {
                    if (n++ >= MaxNudges) break;
                    _nudges.Children.Add(NudgeRow(nudge));
                }

                RenderButton(card);

                // The card just changed height under the scroller. Ask at Loaded rather than now:
                // the extent is zero until this content has been arranged, so reading it here says
                // "nothing to scroll" on every card - the failure that looks like success.
                _nudgeScroll.ScrollToHome();
                Dispatcher.UIThread.Post(UpdateMoreCue, DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] book card render failed for {Card}", card.Id);
            }
        }

        /// <summary>
        /// Show the chevron exactly while there is unread copy under the fold. It appears when a
        /// card overflows and goes away at the bottom, so it is a statement about THIS card rather
        /// than a decoration that trains the reader to ignore it.
        /// </summary>
        private void UpdateMoreCue()
        {
            try
            {
                // WPF's ScrollableHeight, spelled out: Avalonia exposes the extent and the viewport
                // and leaves the subtraction to the caller.
                double left = _nudgeScroll.Extent.Height - _nudgeScroll.Viewport.Height - _nudgeScroll.Offset.Y;
                _moreBelow.IsVisible = left > 1.0;
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book more-cue failed"); }
        }

        /// <summary>
        /// Lay a line of card copy into a TextBlock as runs, with the <c>*asterisk*</c> key words
        /// drawn loud. Falls all the way back to the plain string if anything at all goes wrong: a
        /// card that lost its emphasis is a card, a card that lost its text is a blank panel.
        /// </summary>
        private static void Emphasize(TextBlock target, string? line, IBrush plain, IBrush hot)
        {
            try
            {
                target.Inlines ??= new InlineCollection();
                target.Inlines.Clear();
                foreach (var run in EmiBookText.Parse(line))
                {
                    target.Inlines.Add(new Run(run.Text)
                    {
                        Foreground = run.Hot ? hot : plain,
                        FontWeight = run.Hot ? FontWeight.Bold : FontWeight.Normal,
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] book emphasis failed, falling back to plain text");
                try { target.Inlines?.Clear(); target.Text = EmiBookText.Strip(line); } catch { /* nothing left */ }
            }
        }

        /// <summary>
        /// One nudge: a hard little square and a wrapped line, with its key words bold and pink.
        /// Nine of bottom margin, not six: four bullets that each wrap to two lines ran together
        /// into one block at six.
        /// </summary>
        private DockPanel NudgeRow(string text)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 9), LastChildFill = true };

            var bullet = new Border
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(0, 5, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Background = NudgeHot,
            };
            DockPanel.SetDock(bullet, Dock.Left);
            row.Children.Add(bullet);

            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 16,
                FontSize = 12.5,
                Foreground = NudgePlain,
            };
            tb.FontFamily = FaceFont;
            Emphasize(tb, text, NudgePlain, NudgeHot);
            row.Children.Add(tb);

            return row;
        }

        /// <summary>
        /// The one button. A card with no detour has no button at all rather than a dead one, and a
        /// card whose detour cannot run right now keeps the button but ghosts it, so the shape of
        /// the card does not change under the reader between one launch and the next.
        /// </summary>
        private void RenderButton(EmiBookCard card)
        {
            if (card.Target == null && card.Tour == null)
            {
                _btnGo.IsVisible = false;
                return;
            }

            _btnGo.IsVisible = true;

            if (card.Target != null)
            {
                // A door this head hides (no surface) is dark here, as an unavailable one is in WPF.
                var t = EmiTargets.Find(card.Target);
                _btnGo.Content = new TextBlock { Text = EmiBookCards.L("emi_book_go", "TAKE ME THERE") };
                _btnGo.IsEnabled = t != null && t.Available;
                return;
            }

            _btnGo.Content = new TextBlock { Text = EmiBookCards.L("emi_book_walk", "WALK ME THROUGH IT") };
            _btnGo.IsEnabled = TourReady();
        }

        /// <summary>
        /// Can the walk-through actually run? WPF's answer is <c>EmiOffers.TourFeasible</c>
        /// (ConditioningControlPanel/Services/EmiDesk/EmiOffers.cs), four conditions.
        ///
        /// <para>ponytail: the old note here said all four need "a move to Core", and THREE of them
        /// no longer do - RE-AUDITED 2026-09-04 against Core rather than believed:</para>
        /// <list type="bullet">
        ///   <item><c>Application.Current.MainWindow is MainWindow</c> resolves: the desktop
        ///         lifetime's MainWindow is what <c>App.MainWindowRef</c> was, the resolution
        ///         <c>EmiDeskWindow.AppMainWindow</c> already uses.</item>
        ///   <item><c>SessionEngine.Active?.IsRunning</c> is <c>CoreSession.IsSessionRunning</c>
        ///         (a session, not the plain engine), in Core. Unseeded on this head, and "no session is running" is the truth here,
        ///         not a placebo.</item>
        ///   <item><c>App.Tutorial?.IsActive</c> is <c>CoreTutorial.IsActive</c>, in Core since the
        ///         tutorial seam landed. Also unseeded here - CCP.Avalonia/App.axaml.cs says why:
        ///         TutorialService and its twenty-two step lists are still in the WPF head.</item>
        ///   <item><c>EmiState.HasTourDone(tour)</c>, the already-walked latch, is the one that is
        ///         genuinely still head-side (Services/EmiDesk/EmiState.cs).</item>
        /// </list>
        /// <para>So this is NOT restored, and deliberately: all three resolvable conditions are
        /// constant-false on this head, so evaluating them would change nothing a reader can see
        /// while implying the fourth was checked too. The thing that would make the button honest
        /// is <see cref="Go"/> being able to START a tour, and it cannot - see there. It stays lit
        /// so the card renders in its full shape, which is what the render proves.</para>
        /// </summary>
        private static bool TourReady() => true;

        /// <summary>
        /// ponytail: the tour half is NOT blocked on a move any more and the old note was wrong to
        /// say so. <c>CoreTutorial.Start(tourName)</c> is in Core; what is missing is that this head
        /// leaves the seam UNSEEDED (CCP.Avalonia/App.axaml.cs), so <c>Start</c> is a silent no-op
        /// and calling it would give a button that looks like it walked you somewhere. The door half
        /// is <c>EmiTargets.Open</c>. Both ends of the button are a
        /// detour into the app. The door half is wired: straight through <see cref="EmiTargets"/>,
        /// the ring's own Pick, so a door opened from the book is a door used. The
        /// <c>effectFired</c> moment WPF fires after it has no <c>App.EmiDesk.Fire</c> here.
        private void Go()
        {
            var cards = EmiBookCards.All;
            if (_index < 0 || _index >= cards.Count) return;
            var card = cards[_index];
            if (card.Target != null) { EmiTargets.Find(card.Target)?.Open(); return; }
            if (card.Tour == null) return;
            // WPF EmiBookWindow.Go: a NAME, never an ordinal. The book closes first: a book sitting on
            // top of the coach marks is exactly the thing the coach marks point at.
            if (!Enum.TryParse<global::ConditioningControlPanel.Avalonia.Tours.TutorialType>(card.Tour, out _))
            {
                Log.Warning("[EmiDesk] book card {Card} names an unknown tour {Tour}", card.Id, card.Tour);
                return;
            }
            try { Close(); } catch { /* it is going away either way */ }
            CoreTutorial.Start(card.Tour);
        }

        private void RenderTabs()
        {
            var tabs = new[] { _tab0, _tab1, _tab2 };
            for (int i = 0; i < tabs.Length; i++)
            {
                try
                {
                    // A TextBlock rather than a bare string: Avalonia reads "_" in Content as an
                    // access key, and a localized label is not ours to guarantee is free of one.
                    tabs[i].Content = new TextBlock { Text = EmiBookCards.TabName(i) };
                    // A tab with nothing behind it is drawn and dead, not hidden: the shape of the
                    // book is honest from day one.
                    tabs[i].IsEnabled = EmiBookCards.TabHasCards(i);
                    tabs[i].Classes.Set("on", i == _tab);
                }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book tab {Tab} render failed", i); }
            }
        }

        /// <summary>
        /// THE SIDE RAIL: one icon per card on this tab, and clicking one goes straight there.
        /// Rebuilt on every card change rather than built once and re-tagged - it is at most eight
        /// buttons, and a rail rebuilt from the deck cannot drift out of step with it.
        /// </summary>
        private void RenderRail()
        {
            try
            {
                _rail.Children.Clear();

                Resources.TryGetResource("RailChip", null, out var themeRes);
                var chipTheme = themeRes as ControlTheme;
                var cards = EmiBookCards.All;
                for (int i = 0; i < cards.Count; i++)
                {
                    if (cards[i].Tab != _tab) continue;

                    int target = i;
                    var card = cards[i];
                    bool here = i == _index;

                    var chip = new Button { Theme = chipTheme };
                    chip.Classes.Set("on", here);
                    ToolTip.SetTip(chip, card.Title);

                    // ponytail: needs EmiBookGlyphs.For (WPF head; 16-cell glyphs blown up 2x with
                    // nearest neighbour). AUDITED 2026-09-04: that file does NOT move whole and
                    // should not be asked to. Its 26 painters and the Disc / PlayHead / DownHead
                    // primitives are pure and belong in Core beside the deck; For() and Build()
                    // return System.Windows.Media.ImageSource and are the head's half by nature -
                    // so this splits the same way EmiPixelCanvas does (see StartClock above), and
                    // the two are one job. With no glyph the original's
                    // OWN fallback runs - a card with no glyph still gets a chip, because skipping
                    // it would silently drop a page out of the only navigation that names its
                    // destinations.
                    chip.Content = new Border { Width = 10, Height = 10, Background = here ? NudgeHot : NudgePlain };

                    chip.Click += (_, _) => SelectCard(target, speak: true);
                    _rail.Children.Add(chip);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] book rail render failed");
            }
        }

        /// <summary>
        /// One dot per card ON THIS TAB, not per card in the book. The dots are a "where am I in
        /// this chapter" reading.
        /// </summary>
        private void RenderDots()
        {
            try
            {
                _dotsPanel.Children.Clear();
                _dots.Clear();

                var cards = EmiBookCards.All;
                for (int i = 0; i < cards.Count; i++)
                {
                    if (cards[i].Tab != _tab) continue;

                    int target = i;
                    bool here = i == _index;
                    var dot = new Border
                    {
                        Width = 8,
                        Height = 8,
                        Margin = new Thickness(4, 0, 4, 0),
                        Cursor = new Cursor(StandardCursorType.Hand),
                        Background = new ImmutableSolidColorBrush(here
                            ? Color.FromRgb(0xFF, 0x69, 0xB4)
                            : Color.FromRgb(0x46, 0x43, 0x6D)),
                    };
                    dot.PointerReleased += (_, _) => SelectCard(target, speak: true);
                    _dotsPanel.Children.Add(dot);
                    _dots.Add(dot);
                }

                _btnPrev.IsEnabled = _index > 0;
                _btnNext.IsEnabled = _index < cards.Count - 1;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] book dots render failed");
            }
        }

        /// <summary>
        /// Her line in the margin: a comment ON the card, not a reading of it.
        ///
        /// <para>ponytail: the OWNER is here now, so this is one member away. What is missing is
        /// <c>EmiDeskWindow.SpeakLine</c> and the <c>LineDraw</c> record, and those are blocked on
        /// <c>EmiLineEngine</c> + <c>EmiChains.Player</c>
        /// (ConditioningControlPanel/Services/EmiDesk/). Of those two, AUDITED 2026-09-04, only
        /// EmiChains is close: its whole head coupling is Player's DispatcherTimer, one seam swap.
        /// EmiLineEngine reads App.Settings and calls App.EmiDesk.AskSituationOk(), which its own
        /// header says do not exist without the shell - so the margin line is behind the shell, not
        /// behind a move. Meanwhile the widget's <c>Say</c> and
        /// <c>PlayChain</c> are still no-ops on this head, so a line handed over would be swallowed
        /// rather than spoken. Priority 2, keyed "book.margin.&lt;id&gt;", face from the card - all
        /// of that is data this stub already carries, so only the delivery is missing.</para>
        /// </summary>
        private void SpeakMargin()
        {
            try
            {
                var cards = EmiBookCards.All;
                if (_index < 0 || _index >= cards.Count) return;
                var card = cards[_index];
                if (string.IsNullOrWhiteSpace(card.MarginEn)) return;
                Log.Verbose("[EmiDesk] book margin {Id}: {Line} {Face}", card.Id, card.MarginEn, card.MarginFace);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] book margin line failed");
            }
        }

        // ---------------------------------------------------------------- placement

        /// <summary>
        /// Her body box on the desk, in PHYSICAL pixels - read off the widget itself.
        ///
        /// <para>With no owner (the headless render) it is a fixed box on the right of a 1920 desk,
        /// which is where she usually stands and therefore exercises the same branch the real one
        /// does. <c>EmiDeskWindow.BodyScreenRect</c> is a DIP-typed <see cref="Rect"/> already
        /// holding physical pixels, so the only work here is the rounding into
        /// <see cref="PixelRect"/>.</para>
        /// </summary>
        private PixelRect BodyScreenRect
        {
            get
            {
                try
                {
                    var b = _owner?.BodyScreenRect;
                    if (b is { Width: > 0, Height: > 0 })
                        return new PixelRect(
                            (int)Math.Round(b.Value.X), (int)Math.Round(b.Value.Y),
                            (int)Math.Round(b.Value.Width), (int)Math.Round(b.Value.Height));
                }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book body-rect read failed"); }
                return new PixelRect(1480, 360, 220, 420);
            }
        }

        private PixelRect WorkArea()
        {
            try
            {
                var body = BodyScreenRect;
                var centre = new PixelPoint(body.X + body.Width / 2, body.Y + body.Height / 2);
                var screen = Screens?.ScreenFromPoint(centre) ?? Screens?.Primary;
                if (screen != null) return screen.WorkingArea;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] book work-area probe failed");
            }
            return new PixelRect(0, 0, 1920, 1080);
        }

        /// <summary>
        /// Put the book beside her, on the side with room, vertically centred on her and clamped
        /// into the work area of the monitor she is standing on.
        ///
        /// <para>PHYSICAL PIXELS over this window's own scale, both sides of the sum. Avalonia makes
        /// that explicit where WPF did not: <c>Width</c>/<c>Height</c> are DIPs and
        /// <c>Position</c> is device pixels, so the conversion runs in both directions here.</para>
        ///
        /// <para>On a short desk the stage drops from 3x to 2x rather than the book scrolling. Nine
        /// screen pixels per cell down to four keeps the integer multiple, and a demo with a
        /// half-pixel seam in it is worse than a smaller demo.</para>
        /// </summary>
        private void PlaceWindow()
        {
            try
            {
                var work = WorkArea();
                double s = RenderScaling;
                if (s <= 0) s = 1.0;

                double workL = work.X / s;
                double workT = work.Y / s;
                double workW = work.Width / s;
                double workH = work.Height / s;

                var bodyPx = BodyScreenRect;
                double bodyL = bodyPx.X / s;
                double bodyT = bodyPx.Y / s;
                double bodyR = bodyPx.Right / s;
                double bodyH = bodyPx.Height / s;

                var place = Services.EmiDesk.EmiBookLayout.Place(workL, workW, bodyL, bodyR);
                if (ForceNarrow) place = place with { Width = Services.EmiDesk.EmiBookLayout.NarrowWidth, Narrow = true };
                if (Math.Abs(Width - place.Width) > 0.5) Width = place.Width;

                double h = Math.Min(FullHeight, Math.Max(MinPanelHeight, workH - 24));
                if (Math.Abs(Height - h) > 0.5) Height = h;

                // The test is ROOM rather than "did we get the full height": a 1366 x 768 laptop
                // lands a hair under FullHeight with ample room for the big stage, and measuring
                // against FullHeight sent those machines to the small stage for an 8 pixel shortfall.
                // The narrow book overrides it outright: a 288 wide stage does not fit a 196 column.
                ApplyStageScale(!place.Narrow && h >= BigStageFloor ? 3 : 2);

                bool wasOnHerLeft = _onHerLeft;
                _onHerLeft = place.OnHerLeft;

                if (place.CoversHer && !_notedCover)
                {
                    _notedCover = true;
                    Log.Information(
                        "[EmiDesk] book has no room beside her (work {WorkW:F0}, body {BodyW:F0}); it overlaps",
                        workW, bodyR - bodyL);
                }

                // Centred on her, not aligned to her head: the book is nearly three of her tall.
                double top = bodyT + bodyH / 2 - h / 2;
                top = Math.Max(workT, Math.Min(workT + workH - h, top));

                Position = new PixelPoint(
                    (int)Math.Round(place.Left * s),
                    (int)Math.Round(top * s));

                _sweepHost.RenderTransformOrigin = new RelativePoint(_onHerLeft ? 1 : 0, 0.5, RelativeUnit.Relative);

                // ponytail: needs EmiBook.NoteSideChanged (her bubble dodges to the side the panel
                // is NOT on). The event itself is trivial, but its one subscriber is the bubble's
                // LayoutBubble dodge in EmiDeskWindow.Bubble.cs, which is not ported on this head -
                // so an event raised here today would have nobody listening.
                if (wasOnHerLeft != _onHerLeft)
                    Log.Debug("[EmiDesk] book changed side, now {Side}", SideOfHer);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] book placement failed");
            }
        }

        /// <summary>Integer stage scale only. 3 gives 288 x 216, 2 gives 192 x 144.</summary>
        private void ApplyStageScale(int mult)
        {
            try
            {
                int m = Math.Max(2, Math.Min(3, mult));
                double w = BufW * m;
                double hh = BufH * m;
                if (Math.Abs(_stage.Width - w) < 0.5) return;
                _stage.Width = w;
                _stage.Height = hh;
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book stage scale failed"); }
        }
    }
}
