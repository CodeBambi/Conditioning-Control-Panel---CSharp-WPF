using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace ConditioningControlPanel.Localization
{
    /// <summary>
    /// XAML markup extension for localized strings.
    /// Usage: Content="{loc:Str btn_cancel}"
    ///
    /// Register namespace in XAML:
    ///   xmlns:loc="clr-namespace:ConditioningControlPanel.Localization"
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public class StrExtension : MarkupExtension
    {
        public string Key { get; set; }

        public StrExtension() { Key = string.Empty; }
        public StrExtension(string key) { Key = key; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key))
                return string.Empty;

            // Create a binding to LocalizationManager's indexer so it updates on language change
            var binding = new Binding($"[{Key}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay
            };

            return binding.ProvideValue(serviceProvider);
        }
    }
}
