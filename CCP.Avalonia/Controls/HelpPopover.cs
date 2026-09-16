using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// Rich, interactive help for an existing "?" button. The card is built from the shared Core
    /// <see cref="HelpContent"/> rather than keeping a second catalogue in the head.
    ///
    /// <para>The popup deliberately remains text-only. Core topics may carry clip metadata, but
    /// this head has no playback surface; opening a window that cannot play the clip would make
    /// media parity look complete when it is not.</para>
    ///
    /// <para>Hover opens after a short delay and keeps the card alive while the pointer crosses the
    /// gap into it. A click pins it; clicking again, pressing Escape, clicking away, deactivation,
    /// and detach all close it. Only one attached card may be open at a time.</para>
    /// </summary>
    public sealed class HelpPopover
    {
        private const int OpenDelayMs = 100;
        private const int CloseGraceMs = 250;
        private const double CardMaxWidth = 380;
        private const double BodyMaxWidth = 360;
        private const double CardMaxHeight = 560;

        private static HelpPopover? _active;

        private static readonly AttachedProperty<HelpPopover?> InstanceProperty =
            AvaloniaProperty.RegisterAttached<Button, HelpPopover?>(
                "Instance", typeof(HelpPopover));

        private readonly Button _button;
        private readonly HelpContent _content;
        private readonly Popup _popup;
        private readonly DispatcherTimer _openTimer;
        private readonly DispatcherTimer _closeTimer;
        private Control? _popupRoot;
        private Window? _hostWindow;
        private bool _pinned;
        private bool _detached;

        private HelpPopover(Button button, HelpContent content)
        {
            _button = button;
            _content = content;
            _popup = new Popup
            {
                PlacementTarget = button,
                Placement = PlacementMode.Right,
                HorizontalOffset = 10,
                IsLightDismissEnabled = false,
                Focusable = false,
                ShouldUseOverlayLayer = true,
            };
            _popup.Closed += OnPopupClosed;
            ((ISetLogicalParent)_popup).SetParent(_button);

            _openTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(OpenDelayMs),
            };
            _openTimer.Tick += OnOpenTimerTick;
            _closeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CloseGraceMs),
            };
            _closeTimer.Tick += OnCloseTimerTick;

            _button.PointerEntered += OnButtonPointerEntered;
            _button.PointerExited += OnButtonPointerExited;
            _button.Click += OnButtonClick;
            _button.Unloaded += OnButtonUnloaded;

            // The styled button draws the glyph, while automation gets the real topic and body.
            AutomationProperties.SetName(_button, content.Title ?? "Help");
            AutomationProperties.SetHelpText(_button, content.WhatItDoes ?? string.Empty);
        }

        /// <summary>Attaches a popover to <paramref name="button"/>, replacing any old attachment.</summary>
        public static void Attach(Button button, HelpContent content)
        {
            if (button is null || content is null) return;
            Clear(button);
            button.SetValue(InstanceProperty, new HelpPopover(button, content));
        }

        /// <summary>Detaches the popover and all of its input/timer handlers.</summary>
        public static void Clear(Button button)
        {
            if (button?.GetValue(InstanceProperty) is not HelpPopover existing) return;
            existing.Detach();
            button.ClearValue(InstanceProperty);
        }

        /// <summary>Closes the one currently open card, if any.</summary>
        public static void CloseActive() => _active?.Close();

        internal static bool IsOpen(Button button) =>
            button?.GetValue(InstanceProperty) is HelpPopover popover && popover._popup.IsOpen;

        internal static bool IsPinned(Button button) =>
            button?.GetValue(InstanceProperty) is HelpPopover popover && popover._pinned;

        internal static Control? PopupContent(Button button) =>
            button?.GetValue(InstanceProperty) is HelpPopover popover ? popover._popupRoot : null;

        private void OnButtonPointerEntered(object? sender, PointerEventArgs e)
        {
            _closeTimer.Stop();
            if (_pinned || _popup.IsOpen || _detached) return;
            _openTimer.Start();
        }

        private void OnButtonPointerExited(object? sender, PointerEventArgs e)
        {
            _openTimer.Stop();
            if (!_pinned && _popup.IsOpen) _closeTimer.Start();
        }

        private void OnPopupPointerEntered(object? sender, PointerEventArgs e)
        {
            _openTimer.Stop();
            _closeTimer.Stop();
        }

        private void OnPopupPointerExited(object? sender, PointerEventArgs e)
        {
            if (!_pinned) _closeTimer.Start();
        }

        private void OnOpenTimerTick(object? sender, EventArgs e)
        {
            _openTimer.Stop();
            if (!_detached && _button.IsPointerOver) Open();
        }

        private void OnCloseTimerTick(object? sender, EventArgs e)
        {
            _closeTimer.Stop();
            if (!_pinned) Close();
        }

        private void OnButtonClick(object? sender, RoutedEventArgs e)
        {
            if (_pinned)
            {
                Close();
                return;
            }

            _openTimer.Stop();
            _closeTimer.Stop();
            _pinned = true;
            Open();
        }

        private void Open()
        {
            if (_detached || TopLevel.GetTopLevel(_button) is null) return;

            if (_active is not null && !ReferenceEquals(_active, this))
                _active.Close();
            _active = this;

            EnsureContent();
            SubscribeHostWatchers();
            if (_popup.IsOpen) return;

            try
            {
                _popup.IsOpen = true;
            }
            catch (Exception ex)
            {
                Log.Debug("HelpPopover open failed: {Error}", ex.Message);
                Close();
            }
        }

        private void Close()
        {
            _openTimer.Stop();
            _closeTimer.Stop();
            _pinned = false;

            try
            {
                if (_popup.IsOpen) _popup.IsOpen = false;
            }
            catch (Exception ex)
            {
                Log.Debug("HelpPopover close failed: {Error}", ex.Message);
            }
            finally
            {
                UnsubscribeHostWatchers();
                if (ReferenceEquals(_active, this)) _active = null;
            }
        }

        private void OnPopupClosed(object? sender, EventArgs e)
        {
            _openTimer.Stop();
            _closeTimer.Stop();
            _pinned = false;
            UnsubscribeHostWatchers();
            if (ReferenceEquals(_active, this)) _active = null;
        }

        private void SubscribeHostWatchers()
        {
            if (_hostWindow is not null) return;
            _hostWindow = TopLevel.GetTopLevel(_button) as Window;
            if (_hostWindow is null) return;

            _hostWindow.Deactivated += OnHostDeactivated;
            _hostWindow.PropertyChanged += OnHostPropertyChanged;
            _hostWindow.AddHandler(InputElement.PointerPressedEvent,
                OnHostPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            _hostWindow.AddHandler(InputElement.KeyDownEvent,
                OnHostKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        private void UnsubscribeHostWatchers()
        {
            if (_hostWindow is null) return;

            _hostWindow.Deactivated -= OnHostDeactivated;
            _hostWindow.PropertyChanged -= OnHostPropertyChanged;
            _hostWindow.RemoveHandler(InputElement.PointerPressedEvent, OnHostPointerPressed);
            _hostWindow.RemoveHandler(InputElement.KeyDownEvent, OnHostKeyDown);
            _hostWindow = null;
        }

        private void OnHostDeactivated(object? sender, EventArgs e) => Close();

        private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty &&
                _hostWindow?.WindowState != WindowState.Normal)
                Close();
        }

        private void OnHostKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private void OnHostPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source is not Visual source) { Close(); return; }
            if (ReferenceEquals(source, _button) || _button.IsVisualAncestorOf(source) ||
                (_popupRoot is Visual root && (ReferenceEquals(root, source) || root.IsVisualAncestorOf(source))))
                return;
            Close();
        }

        private void OnButtonUnloaded(object? sender, RoutedEventArgs e) => Close();

        private void EnsureContent()
        {
            if (_popupRoot is not null) return;

            var pink = Brush("PinkBrush", Color.FromRgb(0xFF, 0x69, 0xB4));
            var body = new StackPanel { MaxWidth = BodyMaxWidth };

            var header = new Border
            {
                Background = Brush("PanelBgBrush", Color.FromRgb(0x1A, 0x1A, 0x32)),
                Padding = new Thickness(12, 10),
                CornerRadius = new CornerRadius(8, 8, 0, 0),
            };
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal };
            headerRow.Children.Add(new TextBlock
            {
                Text = _content.Icon ?? "?",
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
            headerRow.Children.Add(new TextBlock
            {
                Text = _content.Title ?? string.Empty,
                Foreground = pink,
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            header.Child = headerRow;
            body.Children.Add(header);

            var what = new StackPanel { Margin = new Thickness(12, 12, 12, 8) };
            what.Children.Add(Heading("help_heading_what_it_does", pink));
            what.Children.Add(new TextBlock
            {
                Text = _content.WhatItDoes ?? string.Empty,
                Foreground = Brush("TextSecondaryBrush", Color.FromRgb(0xD0, 0xD0, 0xD0)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
            });
            body.Children.Add(what);

            if (_content.HasTips)
            {
                var tips = new StackPanel { Margin = new Thickness(12, 0, 12, 8) };
                var tipsGold = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
                var tipsHeading = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                };
                tipsHeading.Children.Add(new TextBlock
                {
                    Text = "💡",
                    Foreground = tipsGold,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 4, 4),
                });
                tipsHeading.Children.Add(Heading("help_heading_tips", tipsGold));
                tips.Children.Add(tipsHeading);
                foreach (var tip in _content.Tips ?? new())
                {
                    var row = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 2, 0, 0),
                    };
                    row.Children.Add(new TextBlock
                    {
                        Text = "•",
                        Foreground = Brush("TextDimBrush", Color.FromRgb(0x80, 0x80, 0x90)),
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 6, 0),
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = tip ?? string.Empty,
                        Foreground = Brush("TextMutedBrush", Color.FromRgb(0xB0, 0xB0, 0xB0)),
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 310,
                    });
                    tips.Children.Add(row);
                }
                body.Children.Add(tips);
            }

            if (_content.HasHowItWorks)
            {
                var howForeground = Brush("TextMutedBrush", Color.FromRgb(0xA0, 0xA0, 0xBC));
                var how = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(12, 4, 12, 12),
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(6),
                };
                var howBody = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                };
                howBody.Children.Add(new TextBlock
                {
                    Text = "⚙",
                    Foreground = howForeground,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 4, 4),
                });
                howBody.Children.Add(Heading("help_heading_how_it_works", howForeground));
                var howContent = new StackPanel();
                howContent.Children.Add(howBody);
                howContent.Children.Add(new TextBlock
                {
                    Text = _content.HowItWorks ?? string.Empty,
                    Foreground = howForeground,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 14,
                    FontStyle = FontStyle.Italic,
                });
                how.Child = howContent;
                body.Children.Add(how);
            }

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x25, 0x25, 0x42)),
                BorderBrush = pink,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                MaxWidth = CardMaxWidth,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                    BlurRadius = 10,
                    OffsetX = 0,
                    OffsetY = 0,
                    Opacity = 0.15,
                },
                Child = new ScrollViewer
                {
                    Content = body,
                    Focusable = false,
                    MaxHeight = CardMaxHeight,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
            };

            // The transparent ring gives the pointer room to cross the placement gap and keeps
            // the close timer from racing a normal hand movement into the card.
            var root = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(12),
                Child = card,
            };
            root.PointerEntered += OnPopupPointerEntered;
            root.PointerExited += OnPopupPointerExited;

            _popupRoot = root;
            _popup.Child = root;
        }

        private static TextBlock Heading(string key, IBrush foreground)
        {
            var heading = new TextBlock
            {
                Foreground = foreground,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 0, 0, 4),
            };
            heading.Bind(TextBlock.TextProperty, new Binding($"[{key}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay,
            });
            return heading;
        }

        private IBrush Brush(string key, Color fallback)
        {
            if (_button.TryFindResource(key, out var value) && value is IBrush brush)
                return brush;
            if (Application.Current?.TryFindResource(key, out value) == true && value is IBrush appBrush)
                return appBrush;
            return new SolidColorBrush(fallback);
        }

        private void Detach()
        {
            if (_detached) return;
            _detached = true;
            Close();

            _popup.Closed -= OnPopupClosed;
            _openTimer.Tick -= OnOpenTimerTick;
            _closeTimer.Tick -= OnCloseTimerTick;
            _button.PointerEntered -= OnButtonPointerEntered;
            _button.PointerExited -= OnButtonPointerExited;
            _button.Click -= OnButtonClick;
            _button.Unloaded -= OnButtonUnloaded;

            if (_popupRoot is not null)
            {
                _popupRoot.PointerEntered -= OnPopupPointerEntered;
                _popupRoot.PointerExited -= OnPopupPointerExited;
            }
            _popup.Child = null;
            _popup.PlacementTarget = null;
            ((ISetLogicalParent)_popup).SetParent(null);
            _popupRoot = null;
        }
    }
}
