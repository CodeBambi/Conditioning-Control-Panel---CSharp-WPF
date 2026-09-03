using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Borderless popup that plays a short muted, looping tutorial clip for a
    /// <see cref="HelpContent"/> topic, with a caption below and an optional
    /// "watch full tutorial" link.
    ///
    /// PORTED from ConditioningControlPanel/Windows/HelpVideoWindow.xaml.cs. Deviations:
    ///  - LibVLCSharp.WPF's VideoView, the MediaPlayer and the whole load/loop/mute/dispose block
    ///    are gone: they are the WPF head's, hung off Services.VideoService.SharedLibVLC, and no
    ///    service may move in this layer. StartClip is the ponytail stub below. The fail-soft path
    ///    the original already had - hidden video surface, caption and link still shown - is
    ///    exactly what that stub produces, so the window is correct, just always silent.
    ///  - App.Logger is the head's; the catch blocks swallow as before.
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

        private readonly string? _fullTutorialUrl;
        private readonly string? _whatItDoes;
        private bool _captionShown;

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
            if (!content.HasClip) ShowWhatItDoesFallback();

            _fullTutorialUrl = content.FullTutorialUrl;
            var btnFullTutorial = this.FindControl<Button>("BtnFullTutorial")!;
            if (!string.IsNullOrWhiteSpace(_fullTutorialUrl))
            {
                btnFullTutorial.IsVisible = true;
            }

            // Handlers live here rather than in markup, per the porting convention.
            btnFullTutorial.Click += (_, _) => BtnFullTutorial_Click();
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            this.FindControl<Border>("Titlebar")!.PointerPressed += Titlebar_PointerPressed;
            KeyDown += OnKeyDown;

            StartClip(content);
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
            catch
            {
                // ponytail: needs App.Logger, wired when logging moves to Core
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

        private void StartClip(HelpContent content)
        {
            // ponytail: needs Services.VideoService (LibVLC) and an Avalonia video surface, wired
            // when the player moves to Core. Until then this takes the fail-soft branch the WPF
            // original already had for a missing clip: video surface stays hidden, caption and
            // link still show. VideoContainer is IsVisible="False" in the markup.
            _ = content;
            ShowWhatItDoesFallback();
        }

        private void BtnFullTutorial_Click()
        {
            if (string.IsNullOrWhiteSpace(_fullTutorialUrl)) return;
            try
            {
                Process.Start(new ProcessStartInfo(_fullTutorialUrl) { UseShellExecute = true });
            }
            catch
            {
                // ponytail: needs App.Logger, wired when logging moves to Core
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
            // user or superseded by a newer help video). The WPF player teardown that followed has
            // no counterpart until StartClip is wired.
            if (ReferenceEquals(_current, this)) _current = null;
            base.OnClosed(e);
        }
    }
}
