using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The result of one step of the Dark Patterns "anti-panic" confirm chain.
    /// </summary>
    public enum PanicChoice
    {
        /// <summary>The big, bright, obvious button — a decoy. Picking it keeps you in.</summary>
        Trap,
        /// <summary>The tiny grey link — the genuine escape. Picking it advances toward the real panic.</summary>
        RealStop
    }

    /// <summary>
    /// A deliberately manipulative "are you sure you want to leave?" modal used only while Dark
    /// Patterns mode is active. It is a satire of real-world confirmshaming dark patterns: the
    /// prominent, focused, brightly-coloured button is a <see cref="PanicChoice.Trap"/> whose copy is
    /// authored to LOOK like the way out (and the double-negative body text technically points at it),
    /// while the actual escape is a tiny greyed-out text link (<see cref="PanicChoice.RealStop"/>).
    ///
    /// Safety: the grey escape link ALWAYS works and always advances the chain, so a user who reads
    /// carefully can always get out in a fixed number of steps. Closing the dialog itself (its X /
    /// Alt+F4 / Esc) simply cancels the panic attempt without penalty — it never traps the user.
    /// Built entirely in code so the trap/escape sides and copy can be randomised each step.
    /// </summary>
    public class DarkPatternPanicDialog : Window
    {
        public PanicChoice Choice { get; private set; }

        private static readonly Color Crimson = (Color)ColorConverter.ConvertFromString("#DC143C");
        private static readonly Color DarkRed = (Color)ColorConverter.ConvertFromString("#8B0000");

        // Escalating shaming headers, indexed loosely by how deep the user is.
        private static readonly string[] Headers =
        {
            "Leaving already?",
            "After everything we've built?",
            "You're really doing this?",
            "One more thing before you go.",
            "Wow. Okay.",
        };

        // Double-negative body prompts. Written so the OBVIOUS answer points at the trap button.
        private static readonly string[] Bodies =
        {
            "You don't want to NOT stay conditioned, do you? Confirm below that you'd rather not stop quitting.",
            "So you'd like to quit... quitting the session? Just press the big button to un-cancel your leaving.",
            "Are you sure you don't want to keep not-leaving? The obvious choice keeps the good feelings going.",
            "Please confirm you do not wish to abandon your decision to stay. It's the highlighted one, silly.",
            "Reconsider quitting your reconsideration of quitting. You know which button feels right.",
        };

        // Trap button labels — feel like the exit, but the double-negative body makes them mean "stay".
        private static readonly string[] TrapLabels =
        {
            "YES, GET ME OUT",
            "QUIT THE SESSION",
            "STOP EVERYTHING",
            "LET ME LEAVE",
            "I'M DONE, END IT",
        };

        // The tiny grey link that is the genuine escape — understated on purpose.
        private static readonly string[] RealStopLabels =
        {
            "...actually leave anyway",
            "no — just stop it",
            "quietly quit for real",
            "...i really do want out",
            "end it (the boring way)",
        };

        public DarkPatternPanicDialog(Window? owner, int step, int total, Random rng)
        {
            Owner = owner;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.Height;
            Width = 560;
            ShowInTaskbar = false;
            Topmost = true;

            int idx = Math.Min(step - 1, Headers.Length - 1);
            if (idx < 0) idx = 0;

            // Pick copy for this step (deterministic-ish per step, with a little jitter).
            string header = Headers[idx];
            string body = Bodies[idx];
            string trapLabel = TrapLabels[rng.Next(TrapLabels.Length)];
            string stopLabel = RealStopLabels[rng.Next(RealStopLabels.Length)];
            bool trapOnLeft = rng.Next(2) == 0;

            // --- Root card ---
            var card = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Crimson),
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#140808")),
                Padding = new Thickness(28, 24, 28, 22),
            };

            var root = new StackPanel();
            card.Child = root;
            Content = card;

            // Step counter (tiny, taunting)
            root.Children.Add(new TextBlock
            {
                Text = $"confirmation {step} of {total}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x50, 0x55)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6),
            });

            // Header
            root.Children.Add(new TextBlock
            {
                Text = header,
                Foreground = new SolidColorBrush(Crimson),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            });

            // Body (grey double-negative confirmshame)
            root.Children.Add(new TextBlock
            {
                Text = body,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xB0, 0xB2)),
                FontSize = 14.5,
                LineHeight = 21,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 22),
            });

            // --- Buttons row: big trap on one side, blank on the other ---
            var trap = BuildTrapButton(trapLabel);
            trap.Click += (_, _) => Pick(PanicChoice.Trap);

            var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Trap goes in one random column; the other stays empty so the eye lands on the trap.
            Grid.SetColumn(trap, trapOnLeft ? 0 : 1);
            trap.Margin = trapOnLeft ? new Thickness(0, 0, 8, 0) : new Thickness(8, 0, 0, 0);
            row.Children.Add(trap);
            root.Children.Add(row);

            // The genuine escape: a tiny grey link, alignment also randomised so it never sits
            // predictably under the cursor. It is ALWAYS present and ALWAYS works.
            var realStop = new TextBlock
            {
                Text = stopLabel,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x66)),
                FontSize = 10.5,
                TextDecorations = TextDecorations.Underline,
                Cursor = Cursors.Hand,
                HorizontalAlignment = rng.Next(2) == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            };
            realStop.MouseLeftButtonUp += (_, _) => Pick(PanicChoice.RealStop);
            root.Children.Add(realStop);

            // Focus the trap so Enter/Space picks the decoy — the obvious path is the wrong one.
            Loaded += (_, _) => trap.Focus();
        }

        private static Button BuildTrapButton(string label)
        {
            var btn = new Button
            {
                Content = label,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Cursor = Cursors.Hand,
                Height = 52,
                IsDefault = true,
                FocusVisualStyle = null,
            };

            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            var bg = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
            };
            bg.GradientStops.Add(new GradientStop(Crimson, 0));
            bg.GradientStops.Add(new GradientStop(DarkRed, 1));
            border.SetValue(Border.BackgroundProperty, bg);
            border.SetValue(Border.EffectProperty, new DropShadowEffect
            {
                Color = Crimson,
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.8,
            });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;
            btn.Template = template;
            return btn;
        }

        private void Pick(PanicChoice choice)
        {
            Choice = choice;
            DialogResult = true;
            Close();
        }
    }
}
