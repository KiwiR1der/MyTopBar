using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MyTopBar.Infrastructure.Common
{
    /// <summary>
    /// 把图标 key（cpu / memory / network / volume）转成 GaugeIcons.xaml 里的 DrawingImage。
    ///
    /// 与 <see cref="WeatherIconConverter"/> 同一套机制：图标 key 在运行时由数据源决定，
    /// 无法用 StaticResource 绑定，故用转换器 FindResource。
    ///
    /// 为什么不复用 WeatherIconConverter：那个把前缀固定成 "WeatherIcon_"，且只服务天气；
    /// 这里把前缀参数化，后续新增图标组（例如磁盘、电池）不必再写一个转换器。
    /// </summary>
    public sealed class IconKeyToImageConverter : IValueConverter
    {
        /// <summary>资源 key 前缀，默认 "GaugeIcon_"。</summary>
        public string Prefix { get; set; } = "GaugeIcon_";

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string key || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            string prefix = parameter as string ?? Prefix;
            return Application.Current?.TryFindResource(prefix + key) as ImageSource;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
