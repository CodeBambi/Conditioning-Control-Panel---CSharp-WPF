using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Builds rich help tooltips for the section "?" icons used on the dashboard
    /// and elsewhere. Extracted from MainWindow so non-Window callers (e.g.
    /// <see cref="Features.FeatureCard"/>) can reuse the same styling.
    /// </summary>
    public static class HelpTooltipBuilder
    {
        /// <summary>
        /// Builds a rich help tooltip. <paramref name="host"/> is used for resource
        /// lookup so that styles defined on the owning Window (HelpTooltipStyle,
        /// PinkBrush, ...) are reachable via the logical tree walk. Callers should
        /// only call this once the host is in the visual tree (e.g. from Loaded).
        /// </summary>
        public static ToolTip Build(HelpContent content, FrameworkElement host)
        {
            var tooltip = new ToolTip
            {
                Style = TryFind<Style>(host, "HelpTooltipStyle"),
                Content = BuildPanel(content, host)
            };
            return tooltip;
        }

        private static T? TryFind<T>(FrameworkElement host, string key) where T : class
        {
            // Walk the host's logical tree first (picks up Window.Resources).
            var r = host.TryFindResource(key);
            if (r is T match) return match;
            // Fall back to app-wide resources.
            r = Application.Current?.TryFindResource(key);
            return r as T;
        }

        private static StackPanel BuildPanel(HelpContent content, FrameworkElement host)
        {
            var pinkBrush = TryFind<Brush>(host, "PinkBrush") ?? new SolidColorBrush(Color.FromRgb(255, 105, 180));

            var panel = new StackPanel { MaxWidth = 360 };

            // Header
            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(26, 26, 50)),
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(8, 8, 0, 0)
            };
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
            headerStack.Children.Add(new TextBlock
            {
                Text = content.Icon,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = content.Title,
                Foreground = pinkBrush,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Child = headerStack;
            panel.Children.Add(header);

            // "What It Does" section
            var whatSection = new StackPanel { Margin = new Thickness(12, 12, 12, 8) };
            whatSection.Children.Add(new TextBlock
            {
                Text = "What It Does",
                Foreground = pinkBrush,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            whatSection.Children.Add(new TextBlock
            {
                Text = content.WhatItDoes,
                Foreground = new SolidColorBrush(Color.FromRgb(208, 208, 208)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            });
            panel.Children.Add(whatSection);

            // Tips section (if any)
            if (content.HasTips)
            {
                var tipsSection = new StackPanel { Margin = new Thickness(12, 0, 12, 8) };
                tipsSection.Children.Add(new TextBlock
                {
                    Text = "\uD83D\uDCA1 Tips",
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                foreach (var tip in content.Tips)
                {
                    var tipRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                    tipRow.Children.Add(new TextBlock
                    {
                        Text = "\u2022",
                        Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 144)),
                        Margin = new Thickness(0, 0, 6, 0),
                        FontSize = 12
                    });
                    tipRow.Children.Add(new TextBlock
                    {
                        Text = tip,
                        Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 310
                    });
                    tipsSection.Children.Add(tipRow);
                }
                panel.Children.Add(tipsSection);
            }

            // "How It Works" section (if any)
            if (content.HasHowItWorks)
            {
                var howBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(21, 255, 255, 255)),
                    Margin = new Thickness(12, 4, 12, 12),
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(6)
                };
                var howStack = new StackPanel();
                howStack.Children.Add(new TextBlock
                {
                    Text = "\u2699 How It Works",
                    Foreground = new SolidColorBrush(Color.FromRgb(144, 144, 144)),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                howStack.Children.Add(new TextBlock
                {
                    Text = content.HowItWorks,
                    Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 14,
                    FontStyle = FontStyles.Italic
                });
                howBorder.Child = howStack;
                panel.Children.Add(howBorder);
            }

            return panel;
        }
    }
}
