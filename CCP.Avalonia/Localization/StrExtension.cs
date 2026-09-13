using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Localization
{
    /// <summary>
    /// Avalonia twin of the WPF head's {loc:Str key} markup extension
    /// (ConditioningControlPanel/Localization/LocExtension.cs). Same usage:
    ///
    ///   xmlns:loc="clr-namespace:ConditioningControlPanel.Avalonia.Localization"
    ///   Content="{loc:Str btn_cancel}"
    ///
    /// Returns a binding on LocalizationManager's indexer, so a language change re-renders every
    /// string. The Core manager raises both indexer notification spellings: "Item[]" for WPF and
    /// "Item" for Avalonia. Only this binding shim is per head.
    ///
    /// Why this exists: the first ports each carried a hand-written "string bag" class of
    /// `LocX => Loc.Get("key")` properties. Every key name was transcribed by hand, which is where
    /// raw-key bugs can come from. With this, Avalonia XAML can copy the WPF localization seam.
    /// Formatted strings (Loc.GetF) stay in code-behind, exactly as in WPF.
    /// </summary>
    public sealed class StrExtension : MarkupExtension
    {
        public string Key { get; set; }

        public StrExtension() { Key = string.Empty; }
        public StrExtension(string key) { Key = key; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key))
                return string.Empty;

            return new Binding($"[{Key}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay,
            };
        }
    }
}
