using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    // WhatMovedRow and WhatMovedPlan live in CCP.Core/Services/WhatMovedPlan.cs (both heads draw them).

    /// <summary>
    /// The one-time "What moved" card (nav rework, 2026-10-06). Owned, non-modal, launcher glass.
    /// Show me closes the card, navigates and asks the rail/strip to glow the target once.
    /// Replay lives in Help (read mode: same card, nothing counted).
    /// </summary>
    public partial class WhatMovedCard : Window
    {
        private readonly Action<WhatMovedRow>? _showMe;

        /// <summary>True when opened from Help: a replay never touches the one-time counter.</summary>
        public bool ReadMode { get; }

        public WhatMovedCard() : this(null, readMode: true) { }

        public WhatMovedCard(Action<WhatMovedRow>? showMe, bool readMode)
        {
            InitializeComponent();
            _showMe = showMe;
            ReadMode = readMode;
            Rows.ItemsSource = WhatMovedPlan.Rows.ToList();
        }

        private void ShowMe_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not WhatMovedRow row) return;
            var go = _showMe;
            Close();
            try { go?.Invoke(row); }
            catch (Exception ex) { App.Logger?.Warning(ex, "What moved: Show me {Id} failed", row.Id); }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Enter closes. Escape is the panic key's job and reaches the ladder on its own; the
            // card closes with it so the press does not leave a card over a page being torn down.
            if (e.Key == Key.Enter || e.Key == Key.Escape)
            {
                Close();
                if (e.Key == Key.Enter) e.Handled = true;
            }
        }
    }
}
