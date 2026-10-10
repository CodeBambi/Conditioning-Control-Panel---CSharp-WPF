using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The one-time "What moved" card (nav rework, 2026-10-06). Owned, non-modal, launcher glass.
    /// Show me closes the card, navigates and asks the rail/strip to glow the target once.
    /// Replay lives in Help (read mode: same card, nothing counted).
    ///
    /// PORTED from ConditioningControlPanel/Windows/WhatMovedCard.xaml.cs; the rows and the
    /// one-time rule are Core's <see cref="WhatMovedPlan"/>. PreviewKeyDown becomes a tunnelling
    /// KeyDown so a focused Show me button does not swallow Enter first (P51).
    /// </summary>
    public partial class WhatMovedCard : Window
    {
        private readonly Action<WhatMovedRow>? _showMe;

        /// <summary>True when opened from Help: a replay never touches the one-time counter.</summary>
        public bool ReadMode { get; }

        /// <summary>Render constructor (--render-all): read mode, no navigation.</summary>
        public WhatMovedCard() : this(null, readMode: true) { }

        public WhatMovedCard(Action<WhatMovedRow>? showMe, bool readMode)
        {
            AvaloniaXamlLoader.Load(this);
            _showMe = showMe;
            ReadMode = readMode;
            this.FindControl<ItemsControl>("Rows")!.ItemsSource = WhatMovedPlan.Rows.ToList();
            this.FindControl<Button>("BtnX")!.Click += (_, _) => Close();
            this.FindControl<Button>("BtnDone")!.Click += (_, _) => Close();
            // Show me buttons live in the item template (no name scope): one bubbling handler.
            AddHandler(Button.ClickEvent, ShowMe_Click, RoutingStrategies.Bubble);
            AddHandler(KeyDownEvent, Window_PreviewKeyDown, RoutingStrategies.Tunnel);
        }

        private void ShowMe_Click(object? sender, RoutedEventArgs e)
        {
            if ((e.Source as Control)?.Tag is not WhatMovedRow row) return;
            e.Handled = true;
            var go = _showMe;
            Close();
            try { go?.Invoke(row); }
            catch (Exception ex) { Log.Warning(ex, "What moved: Show me {Id} failed", row.Id); }
        }

        private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            // Enter closes. Escape is the panic key's job and reaches the ladder on its own; the
            // card closes with it so the press does not leave a card over a page being torn down.
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            Close();
            if (e.Key == Key.Enter) e.Handled = true;
        }
    }
}
