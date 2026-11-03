using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MyTopBar.Infrastructure.Common
{
    /// <summary>
    /// 把天气图标 key（qingtian/duoyun/...）转成 WeatherIcons.xaml 里的 DrawingImage 资源。
    ///
    /// 背景：WPF 的 emoji 字体回退在深色背景下渲染成单色轮廓，彩色不可控；
    /// 这里改用阿里 iconfont 的彩色 SVG 转成的 DrawingImage，颜色固定、清晰、深色可见。
    /// key 动态变化，无法 StaticResource，故用转换器在运行时 FindResource。
    /// </summary>
    public sealed class WeatherIconConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string key || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            object? res = Application.Current?.TryFindResource("WeatherIcon_" + key);
            return res as ImageSource;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
