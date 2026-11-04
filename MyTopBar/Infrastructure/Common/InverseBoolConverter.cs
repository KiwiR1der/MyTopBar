using System.Globalization;
using System.Windows.Data;

namespace MyTopBar.Infrastructure.Common
{
    /// <summary>布尔取反，用于「刷新中禁用按钮」这类绑定。</summary>
    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b ? !b : true;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b ? !b : false;
    }
}
