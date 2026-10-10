using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The Home dashboard's numbers and small decisions, as pure data (parity lane E3). Every value
    /// is the WPF 7.1.5 one, lifted from where that head kept it inline:
    /// <list type="bullet">
    /// <item>the favourites drawer: Views/Tabs/SettingsTabView.xaml.cs, "the favorites drawer";</item>
    /// <item>the animated logo dial: Controls/AnimatedLogoImage.cs;</item>
    /// <item>the account strip's bubbles: Controls/HoverBubbleBar.cs;</item>
    /// <item>the tile gestures: Features/FeatureCard.xaml.cs (DashboardInvertClicks).</item>
    /// </list>
    /// The Avalonia head only draws; anything here is unit tested without a window.
    /// </summary>
    public static class HomeDashboardRules
    {
        // ---- the favourites drawer (nav polish wave 3) --------------------------------------

        /// <summary>Open width of the drawer body. Closed is 0; the 22 px handle never moves.</summary>
        public const double FavoritesDrawerWidth = 92;
        public const double FavoritesHandleWidth = 22;

        /// <summary>The rail inside the body: 92 - 5 margin = 87, pinned and right-aligned so a
        /// half-open body slides it in from the edge instead of squeezing the chips.</summary>
        public const double FavoritesRailWidth = 87;

        /// <summary>The chip stack: 87 - 2 border - 8 scrollbar - 8 stack margin = 69.</summary>
        public const double FavoritesChipWidth = 69;
        public const double FavoritesChipHeight = 36;

        public const int FavoritesDrawerSlideMs = 200;

        /// <summary>A new pin opens a closed drawer for this long, with the glow on the chip.</summary>
        public const int FavoritesDrawerPeekMs = 2500;

        public const double FavoritesGlowLow = 0.30, FavoritesGlowHigh = 0.85;
        public const int FavoritesGlowBreathMs = 2600;
        public const int FavoritesTwinkleEveryMs = 6500;

        /// <summary>The handle's chevron points the way a click goes: left opens, right closes.</summary>
        public static string DrawerChevron(bool open) => open ? "›" : "‹";

        /// <summary>The body width for a state.</summary>
        public static double DrawerBodyWidth(bool open) => open ? FavoritesDrawerWidth : 0;

        /// <summary>
        /// What the drawer shows: the saved preference, unless a pin is peeking (then open). A
        /// peek never writes the preference; only the handle does.
        /// </summary>
        public static bool DrawerShownOpen(bool savedOpen, bool peeking) => savedOpen || peeking;

        // ---- the animated logo dial ----------------------------------------------------------

        /// <summary>The logo lives at rest (owner, 2026-10-06: "animated even when not on hover,
        /// but animate faster when on hover"): rest sits on this drive floor.</summary>
        public const double LogoIdleFloor = 0.35;

        /// <summary>The dial's clock, frames per second (the effect clock rate).</summary>
        public const int LogoFps = 30;

        /// <summary>Seconds per dial turn at rest.</summary>
        public const double LogoTurnSeconds = 8;

        /// <summary>Hover energy eases up with this time constant (s) and down with the slower one.</summary>
        public const double LogoEnergyRiseS = 0.18, LogoEnergyFallS = 0.48;

        /// <summary>Renderer drive for a hover energy of 0 (rest) to 1 (full hover).</summary>
        public static double LogoDrive(double energy) => LogoIdleFloor + (1 - LogoIdleFloor) * Math.Clamp(energy, 0, 1);

        /// <summary>Phase speed in radians per second: one turn per 8 s at rest (owner, 2026-10-09: "bump up the speed a bit"; WPF 7.1.5 was 12 s), three times that on hover.</summary>
        public static double LogoPhaseRate(double energy) => Math.Tau / LogoTurnSeconds * (1 + 2 * Math.Clamp(energy, 0, 1));

        /// <summary>One step of the hover energy ease toward <paramref name="target"/> over <paramref name="dt"/> seconds.</summary>
        public static double LogoEnergyStep(double energy, double target, double dt)
        {
            dt = Math.Clamp(dt, 0, 0.1);
            double tau = target > energy ? LogoEnergyRiseS : LogoEnergyFallS;
            return energy + (target - energy) * (1 - Math.Exp(-dt / tau));
        }

        /// <summary>The bundled wordmarks take the animated dial; a mod's own logo art keeps its picture.</summary>
        public static bool LogoTakesDial(string? logoResource) =>
            logoResource != null
            && (logoResource.EndsWith("/logo2.png", StringComparison.OrdinalIgnoreCase)
                || logoResource.EndsWith("/logo.png", StringComparison.OrdinalIgnoreCase));

        // ---- the account strip's hover bubbles ----------------------------------------------

        public const double BubbleSize = 34;
        public const double BubbleGap = 8;
        public const double BubbleHoverScale = 1.06;
        public const double BubblePulseScale = 1.15;
        public const int BubbleExpandMs = 160;
        public static readonly TimeSpan BubblePulseEvery = TimeSpan.FromSeconds(9);

        /// <summary>Resting plate: white at 10%.</summary>
        public const uint BubbleRestFill = 0x1AFFFFFF;

        /// <summary>The dark base a hover tint is laid over.</summary>
        public const uint BubbleOpenBase = 0xFF1E1B33;

        /// <summary>Alpha-composite <paramref name="top"/> over an opaque <paramref name="under"/> (ARGB).</summary>
        public static uint Over(uint top, uint under)
        {
            double a = (top >> 24) / 255.0;
            uint Ch(int shift) => (uint)Math.Round(((top >> shift) & 0xFF) * a + ((under >> shift) & 0xFF) * (1 - a));
            return 0xFF000000u | (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
        }

        public static uint WithAlpha(uint argb, double a) =>
            ((uint)Math.Round(Math.Clamp(a, 0, 1) * 255) << 24) | (argb & 0x00FFFFFF);

        public static uint Lighten(uint argb, double t)
        {
            t = Math.Clamp(t, 0, 1);
            uint L(int shift) { uint v = (argb >> shift) & 0xFF; return (uint)Math.Round(v + (255 - v) * t); }
            return (argb & 0xFF000000u) | (L(16) << 16) | (L(8) << 8) | L(0);
        }

        /// <summary>A plain bubble's hover colour: the hue at 30% over the open base. A filled
        /// bubble (Logout, Discord) lightens its own fill by 14% instead.</summary>
        public static uint BubbleHover(uint hue, uint fill) =>
            (fill >> 24) > 0 ? Lighten(fill, 0.14) : Over(WithAlpha(hue, 0.30), BubbleOpenBase);

        /// <summary>The bubble rim: a filled bubble lightens its fill by 25%, a plain one wears the hue at 55%.</summary>
        public static uint BubbleBorder(uint hue, uint fill) =>
            (fill >> 24) > 0 ? Lighten(fill, 0.25) : WithAlpha(hue, 0.55);

        /// <summary>
        /// The idle pulse walks the line: the next visible bubble after <paramref name="index"/>,
        /// or -1 when none is visible. Returns the bubble to pulse and the index to resume from.
        /// </summary>
        public static (int Pick, int Next) NextPulse(int index, bool[] visible)
        {
            int n = visible.Length;
            if (n == 0) return (-1, 0);
            index = ((index % n) + n) % n;
            for (int tries = 0; tries < n; tries++)
            {
                int c = (index + tries) % n;
                if (visible[c]) return (c, (c + 1) % n);
            }
            return (-1, index);
        }

        // ---- tile gestures --------------------------------------------------------------------

        /// <summary>The "right-click a tile to switch it on or off" caption on the logo face retires
        /// after this many toggles (WPF 7.1.5 DashboardToggleHintRule).</summary>
        public const int ToggleHintMaxUses = 3;

        public static bool ShowToggleHint(int usesSoFar) => usesSoFar < ToggleHintMaxUses;

        /// <summary>
        /// Home tile gesture: by default left-click opens and right-click switches the feature on
        /// or off. "Left or right?" (AppSettings.DashboardInvertClicks) swaps them on Home tiles
        /// only. True = this press toggles; false = it opens.
        /// </summary>
        public static bool GestureToggles(bool rightClick, bool invert) => rightClick != invert;
    }
}
