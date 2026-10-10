using System;
using System.Globalization;
using System.Windows.Data;

namespace ConditioningControlPanel.Controls.NavRail
{
    /// <summary>
    /// Wraps the mod-aware loc converter and upper-cases its result for the rail's section names
    /// (small ExtraBold caps). WPF's TextBlock has no CharacterCasing, and a fake small-caps
    /// typography setting only works on fonts that ship the feature. Scripts with no case
    /// (CJK) come back unchanged.
    /// </summary>
    internal sealed class NavCapsConverter : IValueConverter
    {
        private readonly IValueConverter? _inner;

        public NavCapsConverter(IValueConverter? inner) => _inner = inner;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var v = _inner != null ? _inner.Convert(value!, targetType, parameter!, culture) : value;
            return v is string s ? s.ToUpper(CultureInfo.CurrentUICulture) : v;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
