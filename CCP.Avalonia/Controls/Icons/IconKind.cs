// The one place the head names the icon package's enums. Every other file writes IconKind.X /
// IconVariant.Filled, so swapping the package for a bundled font (oracle option B) edits only
// Controls/Icons/*. IconKind is the RESIZABLE Fluent set (FluentIcons.Common.Symbol): an icon
// name that does not exist is a compile error. See docs/avalonia-decisions.md (2026-10-10).
global using IconKind = FluentIcons.Common.Symbol;
global using IconVariant = FluentIcons.Common.IconVariant;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>In-house "extension glyphs" for brand marks Fluent has no icon for. Decision
    /// 2026-10-10: the spiral only (bubbles use Fluent's BubbleMultiple).</summary>
    public enum IconBrand { None, Spiral }

    /// <summary>Resource keys of the semantic icon brushes (Theme/Icons.axaml). Hearts use the
    /// live PinkBrush key itself: a mod switch replaces that resource, so an alias would freeze.</summary>
    public static class IconBrushes
    {
        public const string Tier1 = "IconTier1Brush";
        public const string Tier2 = "IconTier2Brush";
        public const string Success = "IconSuccessBrush";
        public const string Warn = "IconWarnBrush";
        public const string Danger = "IconDangerBrush";
        public const string Gold = "IconGoldBrush";
        public const string Gem = "IconGemBrush";
        public const string Fire = "IconFireBrush";
        public const string Heart = "PinkBrush";

        /// <summary>Every key, for the contrast test.</summary>
        public static readonly string[] All = { Tier1, Tier2, Success, Warn, Danger, Gold, Gem, Fire, Heart };
    }
}
