using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Borderless popup that plays a short muted, looping tutorial clip for a
    /// <see cref="HelpContent"/> topic, with a caption below and an optional
    /// "watch full tutorial" link.
    ///
    /// PORTED from ConditioningControlPanel/Windows/HelpVideoWindow.xaml.cs. Deviations:
    ///  - LibVLCSharp.WPF's VideoView, the MediaPlayer and the whole load/loop/mute/dispose block
    ///    are gone: they are the WPF head's, hung off
    ///    ConditioningControlPanel/Services/Video/VideoService.cs (SharedLibVLC), and there is no
    ///    Avalonia video surface on this head. Everything up to the player - resolving the clip
    ///    path and proving the file exists - is restored, so StartClip takes the fail-soft branch
    ///    the original already had: hidden video surface, caption and link still shown.
    ///  - The drawn help loop (TryShowLoop) is ported as-is and wins over the clip, as in WPF.
    ///  - App.Logger becomes Serilog's static Log, as everywhere else on this head.
    ///  - DragMove() -> BeginMoveDrag(e); PreviewKeyDown -> KeyDown (tunnelling has no twin, and
    ///    nothing in this window consumes Escape first).
    ///
    /// Fail-soft as the original: never throws to the caller.
    /// </summary>
    public partial class HelpVideoWindow : Window
    {
        // Only one help video may be open at a time. Opening a new one closes whichever is
        // already open, exactly as the WPF original did to keep two players off the box.
        private static HelpVideoWindow? _current;

        private readonly string? _clipPath;
        private readonly string? _fullTutorialUrl;
        private readonly string? _whatItDoes;
        private bool _captionShown;
        private readonly bool _hasLoop;

        private readonly TextBlock _txtCaption;

        /// <summary>Render/design constructor: sample data so --render-view can draw the window.</summary>
        internal HelpVideoWindow() : this(SampleContent()) { }

        /// <summary>
        /// A real topic out of Core's HelpContentService, copied rather than handed over, so the
        /// sample tutorial URL cannot leak into the shared instance. No shipped topic sets
        /// FullTutorialUrl yet (the docs page is not live), and the render should still prove the
        /// button draws.
        /// </summary>
        private static HelpContent SampleContent()
        {
            var real = HelpContentService.GetContent("FlashImages");
            return new HelpContent
            {
                SectionId = real.SectionId,
                Icon = real.Icon,
                Title = real.Title,
                WhatItDoes = real.WhatItDoes,
                ClipFile = real.ClipFile,
                CaptionKey = real.CaptionKey,
                FullTutorialUrl = "https://example.invalid/tutorials/flash-images"
            };
        }

        public HelpVideoWindow(HelpContent content)
        {
            AvaloniaXamlLoader.Load(this);

            _txtCaption = this.FindControl<TextBlock>("TxtCaption")!;

            this.FindControl<TextBlock>("TxtGlyph")!.Text =
                string.IsNullOrEmpty(content.Icon) ? "?" : content.Icon;
            this.FindControl<TextBlock>("TxtTitle")!.Text = content.Title;
            Title = content.Title; // also set Window.Title for accessibility

            // Caption: reproduce {loc:Str CaptionKey} as a live OneWay binding to the
            // LocalizationManager indexer, so it hot-swaps on language change exactly like
            // StrExtension would for a literal key. (This is StrExtension's own binding, built
            // by hand because the key is only known at runtime.)
            if (!string.IsNullOrWhiteSpace(content.CaptionKey))
            {
                _txtCaption.Bind(TextBlock.TextProperty, new Binding($"[{content.CaptionKey}]")
                {
                    Source = LocalizationManager.Instance,
                    Mode = BindingMode.OneWay
                });
                _txtCaption.IsVisible = true;
                _captionShown = true;
            }

            // Fallback so the window is never empty: with no clip and no caption, show the
            // topic's "what it does" blurb.
            _whatItDoes = content.WhatItDoes;

            // A drawn help loop wins over a clip: native, theme-aware, no LibVLC (WPF TryShowLoop).
            _hasLoop = TryShowLoop(content);
            if (!content.HasClip && !_hasLoop) ShowWhatItDoesFallback();

            _fullTutorialUrl = content.FullTutorialUrl;
            var btnFullTutorial = this.FindControl<Button>("BtnFullTutorial")!;
            if (!string.IsNullOrWhiteSpace(_fullTutorialUrl))
            {
                btnFullTutorial.IsVisible = true;
            }

            if (content.HasClip && !_hasLoop)
            {
                _clipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "Resources", "tutorial_videos", content.ClipFile!);
            }

            // Handlers live here rather than in markup, per the porting convention.
            btnFullTutorial.Click += (_, _) => BtnFullTutorial_Click();
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            this.FindControl<Border>("Titlebar")!.PointerPressed += Titlebar_PointerPressed;
            KeyDown += OnKeyDown;

            StartClip();
        }

        /// <summary>
        /// Opens a modeless help video popup for the given topic, centered on owner.
        /// Never throws. Any help video already open is closed first (single live instance).
        /// </summary>
        public static void Show(HelpContent content, Window? owner, bool topmost = false)
        {
            try
            {
                CloseCurrent();

                var win = new HelpVideoWindow(content) { Topmost = topmost };
                _current = win;
                if (owner is not null) win.Show(owner); else win.Show();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HelpVideoWindow: failed to open");
            }
        }

        private static void CloseCurrent()
        {
            var existing = _current;
            _current = null;
            if (existing != null)
            {
                try { existing.Close(); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Shows the topic's "what it does" text in the caption slot when nothing else would fill
        /// it (no clip playing and no localized caption). Idempotent.
        /// </summary>
        private void ShowWhatItDoesFallback()
        {
            if (_captionShown) return;
            if (string.IsNullOrWhiteSpace(_whatItDoes)) return;
            _txtCaption.Text = _whatItDoes;
            _txtCaption.IsVisible = true;
            _captionShown = true;
        }

        /// <summary>
        /// Hosts the topic's drawn help loop (and its step chips) in the video slot when the registry
        /// has one, exactly as WPF HelpVideoWindow.TryShowLoop. The loop replaces the caption too: its
        /// steps say the same thing.
        /// </summary>
        private bool TryShowLoop(HelpContent content)
        {
            try
            {
                if (!ConditioningControlPanel.Avalonia.Controls.HelpLoops.HelpLoopRegistry.TryGet(content.SectionId, out var scene)) return false;
                var view = new ConditioningControlPanel.Avalonia.Controls.HelpLoops.HelpLoopView(scene) { Name = "HelpLoop" };
                IBrush pink = this.TryFindResource("PinkBrush", out var v) && v is IBrush b
                    ? b : new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
                var panel = new StackPanel();
                panel.Children.Add(view);
                panel.Children.Add(new ConditioningControlPanel.Avalonia.Controls.HelpLoops.HelpLoopSteps(view, pink) { Margin = new Thickness(12, 10, 12, 4) });
                var container = this.FindControl<Border>("VideoContainer")!;
                container.Height = double.NaN;
                container.Background = Brushes.Transparent;
                container.Child = panel;
                container.IsVisible = true;
                _txtCaption.IsVisible = false;
                _captionShown = true;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HelpVideoWindow: failed to build help loop");
                return false;
            }
        }

        private void StartClip()
        {
            if (_hasLoop) return; // WPF: _clipPath stays null with a loop, so StartClip had nothing to do
            // Fail soft: no clip configured, or file missing -> leave video hidden. Restored from
            // the WPF original, which logged a Warning here because an absent clip meant a
            // misconfigured topic. On this head it is the steady state - Resources/tutorial_videos
            // is Content in the WPF head and is never laid down beside CCP.Avalonia - so Debug,
            // not a Warning a triager has to learn to ignore.
            if (string.IsNullOrEmpty(_clipPath) || !File.Exists(_clipPath))
            {
                if (!string.IsNullOrEmpty(_clipPath))
                    Log.Debug("HelpVideoWindow: clip not found: {Path}", _clipPath);
                ShowWhatItDoesFallback();
                return;
            }

            // ponytail: playback itself needs LibVLCSharp.WPF's VideoView plus
            // ConditioningControlPanel/Services/Video/VideoService.cs (SharedLibVLC) - both WPF
            // head-side, and this head has no video surface to parent a player into. So a clip
            // that IS on disk still takes the WPF "LibVLC not available" branch, which is this
            // same fail-soft: surface hidden (VideoContainer is IsVisible="False" in the markup),
            // caption and link shown.
            Log.Debug("HelpVideoWindow: no video surface on this head; hiding video for {Path}", _clipPath);
            ShowWhatItDoesFallback();
        }

        private void BtnFullTutorial_Click()
        {
            if (string.IsNullOrWhiteSpace(_fullTutorialUrl)) return;
            try
            {
                Platform.ExternalOpener.Open(_fullTutorialUrl);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HelpVideoWindow: failed to open tutorial url {Url}", _fullTutorialUrl);
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void Titlebar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            try { BeginMoveDrag(e); } catch { /* dragging can throw if not pressed */ }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Drop the single-instance reference if it points at us (whether we were closed by the
            // user or superseded by a newer help video). The WPF player teardown that followed
            // (VideoView detach, MediaPlayer stop/dispose, Media dispose) has nothing to tear down
            // here - StartClip never builds one.
            if (ReferenceEquals(_current, this)) _current = null;
            base.OnClosed(e);
        }
    }
}
