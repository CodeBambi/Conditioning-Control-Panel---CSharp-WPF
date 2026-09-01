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
    /// string - the manager raises PropertyChanged("Item[]") from SetLanguage. The manager lives in
    /// CCP.Core; only this binding shim is per head.
    ///
    /// Why this exists: the first 21 ports each carried a hand-written "string bag" class of
    /// `LocX => Loc.Get("key")` properties. Every key name was transcribed by hand, which is where
    /// the raw-key bug in PR #81 came from. With this the WPF XAML copies nearly verbatim.
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
