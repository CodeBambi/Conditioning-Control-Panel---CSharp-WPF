using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.Theme;

/// <summary>
/// Applies the active mod's <see cref="ModTheme"/> to the Avalonia application
/// resources so the whole UI re-skins when the mod changes. This is the Avalonia
/// equivalent of the WPF <c>MainWindow.RefreshThemeAwareElements()</c> path.
/// </summary>
public sealed class AvaloniaThemeService : IDisposable
{
    private readonly IModService _modService;
    private bool _disposed;

    public event EventHandler? ThemeChanged;

    public AvaloniaThemeService(IModService modService)
    {
        _modService = modService ?? throw new ArgumentNullException(nameof(modService));
        _modService.ActiveModChanged += OnActiveModChanged;
    }

    /// <summary>
    /// Applies the currently active mod's palette immediately. Call once after
    /// the mod service has initialized and before the main window is shown.
    /// </summary>
    public void ApplyCurrentTheme()
    {
        Apply(_modService.ActiveMod);
    }

    private void OnActiveModChanged(object? sender, ModPackage mod)
    {
        Apply(mod);
    }

    private void Apply(ModPackage mod)
    {
        if (_disposed) return;

        var app = Application.Current;
        if (app is null) return;

        var res = app.Resources;

        var accentHex = _modService.GetAccentColorHex();
        var darkHex = _modService.GetAccentDarkColorHex();
        var lightHex = _modService.GetAccentLightColorHex();
        var secondaryHex = _modService.GetSecondaryColorHex();
        var bgHex = _modService.GetBackgroundColorHex();
        var panelHex = _modService.GetPanelColorHex();
        var surfaceHex = _modService.GetSurfaceColorHex();

        System.Diagnostics.Debug.WriteLine($"[Theme] Applying {mod.Id}: accent={accentHex}, bg={bgHex}");

        if (!TryParseColor(accentHex, out var accent)) accent = Color.Parse("#FF69B4");
        if (!TryParseColor(darkHex, out var dark)) dark = Color.Parse("#FF1493");
        if (!TryParseColor(lightHex, out var light)) light = Color.Parse("#FF8FAF");
        if (!TryParseColor(secondaryHex, out var secondary)) secondary = Color.Parse("#9B59B6");
        if (!TryParseColor(bgHex, out var bgColor)) bgColor = Color.Parse("#1A1A2E");
        if (!TryParseColor(panelHex, out var panelColor)) panelColor = Color.Parse("#252542");
        if (!TryParseColor(surfaceHex, out var surfaceColor)) surfaceColor = Color.Parse("#1E1E3A");

        var transparent30 = Color.FromArgb(0x30, accent.R, accent.G, accent.B);
        var transparent20 = Color.FromArgb(0x20, accent.R, accent.G, accent.B);
        var transparent40 = Color.FromArgb(0x40, accent.R, accent.G, accent.B);
        var transparent50 = Color.FromArgb(0x50, accent.R, accent.G, accent.B);
        var transparent60 = Color.FromArgb(0x60, accent.R, accent.G, accent.B);
        var accentPressed = Color.FromArgb(0xFF,
            (byte)Math.Max(0, accent.R - 30),
            (byte)Math.Max(0, accent.G - 30),
            (byte)Math.Max(0, accent.B - 30));

        var panelAccentColor = Lighten(panelColor, 0.15);
        var panelAccentHoverColor = Lighten(panelColor, 0.25);
        var previewBgColor = Darken(bgColor, 0.15);
        var panelBgTransparent = Color.FromArgb(0xB0, panelColor.R, panelColor.G, panelColor.B);

        var tintedBg = Blend(bgColor, accent, 0.15);
        var tintedBgHover = Blend(bgColor, accent, 0.20);
        var midGradient = Blend(bgColor, accent, 0.10);

        // Background colors. The matching *Brush resources in App.axaml bind their
        // Color via {DynamicResource <key>}, so updating the color key alone recolors
        // every consumer live without swapping the brush instance (which would strand
        // StaticResource references created between theme switches).
        SetColor(res, "DarkerBg", bgColor);
        SetColor(res, "PanelBg", panelColor);
        SetColor(res, "SurfaceBg", surfaceColor);
        SetColor(res, "PanelAccent", panelAccentColor);
        SetColor(res, "PanelAccentHover", panelAccentHoverColor);
        SetColor(res, "PreviewBg", previewBgColor);
        SetColor(res, "PanelBgTransparent", panelBgTransparent);

        // Semantic surface, border, and text colors derived from the active palette.
        var elevatedSurfaceColor = Lighten(panelColor, 0.10);
        var glassBorderColor = Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF);
        var textLightColor = Color.FromRgb(0xFF, 0xFF, 0xFF);
        var textMutedColor = Blend(textLightColor, panelColor, 0.55);
        var dangerColor = Color.Parse("#FFE53935");

        SetColor(res, "ElevatedSurface", elevatedSurfaceColor);
        SetColor(res, "GlassBorder", glassBorderColor);
        SetColor(res, "TextLight", textLightColor);
        SetColor(res, "TextMuted", textMutedColor);
        SetColor(res, "Danger", dangerColor);

        // TextLightBrush has no backing *Color key in App.axaml (it is hardcoded
        // white), so recolor the existing brush instance in place rather than
        // swapping the object, keeping any StaticResource consumers in sync.
        SetBrushColorInPlace(res, "TextLightBrush", textLightColor);

        // Accent colors. The matching *Brush resources (PinkBrush, SecondaryBrush,
        // TransparentPink*Brush, etc.) all bind Color via {DynamicResource <key>} in
        // App.axaml, so updating the color keys alone live-reskins every consumer.
        SetColor(res, "PinkColor", accent);
        SetColor(res, "DarkPink", dark);
        SetColor(res, "DarkPinkColor", dark);
        SetColor(res, "PinkButtonHovered", light);
        SetColor(res, "TransparentPink", transparent30);
        SetColor(res, "TransparentPink20", transparent20);
        SetColor(res, "TransparentPink40", transparent40);
        SetColor(res, "TransparentPink50", transparent50);
        SetColor(res, "TransparentPink60", transparent60);
        SetColor(res, "AccentPressed", accentPressed);
        SetColor(res, "PatreonPurple", secondary);
        SetColor(res, "AccentTintedBg", tintedBg);
        SetColor(res, "AccentTintedBgHover", tintedBgHover);
        SetColor(res, "AccentMidGradient", midGradient);

        // Accent gradient: CCP Default keeps the brand gradient, other mods use solid accent.
        if (mod.Id == BuiltInMods.CCPDefaultId && res.TryGetResource("BrandGradient", ThemeVariant.Default, out var brandObj) && brandObj is Brush brandGradient)
        {
            res["AccentGradientBrush"] = brandGradient;
        }
        else
        {
            res["AccentGradientBrush"] = new SolidColorBrush(accent);
        }

        // Drive the FluentTheme system accent so default-styled controls
        // (Slider, ToggleSwitch, CheckBox, RadioButton) follow the active mod.
        UpdateFluentAccent(app, accent);

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void UpdateFluentAccent(Application app, Color accent)
    {
        try
        {
            foreach (var style in app.Styles)
            {
                if (style is not FluentTheme fluent) continue;
                if (fluent.Palettes[ThemeVariant.Light] is ColorPaletteResources lightPalette)
                    lightPalette.Accent = accent;
                if (fluent.Palettes[ThemeVariant.Dark] is ColorPaletteResources darkPalette)
                    darkPalette.Accent = accent;
            }
        }
        catch
        {
            // Best-effort: if the palette API changes, fall back to resource overrides.
        }
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        if (!string.IsNullOrWhiteSpace(hex) && hex.StartsWith("#", StringComparison.Ordinal))
        {
            try
            {
                color = Color.Parse(hex);
                return true;
            }
            catch { }
        }

        color = default;
        return false;
    }

    private static void SetColor(IResourceDictionary res, string key, Color color)
    {
        res[key] = color;
    }

    private static void SetBrushColorInPlace(IResourceDictionary res, string key, Color color)
    {
        // Recolor the existing brush instance so StaticResource consumers created
        // between theme switches update too; only allocate a new brush as a fallback
        // when the resource is missing or is not a mutable SolidColorBrush.
        if (res.TryGetResource(key, ThemeVariant.Default, out var existing) && existing is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        res[key] = new SolidColorBrush(color);
    }

    private static Color Lighten(Color c, double amount)
    {
        return Color.FromRgb(
            (byte)Math.Min(255, c.R + (255 - c.R) * amount),
            (byte)Math.Min(255, c.G + (255 - c.G) * amount),
            (byte)Math.Min(255, c.B + (255 - c.B) * amount));
    }

    private static Color Darken(Color c, double amount)
    {
        return Color.FromRgb(
            (byte)(c.R * (1 - amount)),
            (byte)(c.G * (1 - amount)),
            (byte)(c.B * (1 - amount)));
    }

    private static Color Blend(Color baseColor, Color accent, double amount)
    {
        return Color.FromRgb(
            (byte)(baseColor.R + (accent.R - baseColor.R) * amount),
            (byte)(baseColor.G + (accent.G - baseColor.G) * amount),
            (byte)(baseColor.B + (accent.B - baseColor.B) * amount));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _modService.ActiveModChanged -= OnActiveModChanged;
    }
}
