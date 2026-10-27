using MyTopBar.Models.Settings;

namespace MyTopBar.Widgets.Plugins
{
    /// <summary>时钟组件插件。</summary>
    public sealed class ClockPlugin : IWidgetPlugin
    {
        public string Id => "clock";
        public string DisplayName => "时钟";
        public WidgetZone DefaultZone => WidgetZone.Right;
        public double MinWidth => 60;

        public WidgetViewModelBase Create(IServiceProvider services)
        {
            return (WidgetViewModelBase)services.GetService(typeof(ClockWidgetViewModel))!;
        }
    }

    /// <summary>歌词组件插件。</summary>
    public sealed class LyricsPlugin : IWidgetPlugin
    {
        public string Id => "lyrics";
        public string DisplayName => "歌词";
        public WidgetZone DefaultZone => WidgetZone.Center;
        public double MinWidth => 120;

        public WidgetViewModelBase Create(IServiceProvider services)
        {
            return (WidgetViewModelBase)services.GetService(typeof(LyricsWidgetViewModel))!;
        }
    }

    /// <summary>天气组件插件。</summary>
    public sealed class WeatherPlugin : IWidgetPlugin
    {
        public string Id => "weather";
        public string DisplayName => "天气";
        public WidgetZone DefaultZone => WidgetZone.Right;
        public double MinWidth => 72;

        public WidgetViewModelBase Create(IServiceProvider services)
        {
            return (WidgetViewModelBase)services.GetService(typeof(WeatherWidgetViewModel))!;
        }
    }

    /// <summary>文本/一言组件插件。</summary>
    public sealed class TextPlugin : IWidgetPlugin
    {
        public string Id => "text";
        public string DisplayName => "文本 / 一言";
        public WidgetZone DefaultZone => WidgetZone.Center;
        public double MinWidth => 80;

        public WidgetViewModelBase Create(IServiceProvider services)
        {
            return (WidgetViewModelBase)services.GetService(typeof(TextWidgetViewModel))!;
        }
    }

    /// <summary>CPU 组件插件。</summary>
    public sealed class CpuPlugin : IWidgetPlugin
    {
        public string Id => "cpu";
        public string DisplayName => "CPU";
        public WidgetZone DefaultZone => WidgetZone.Right;
        public double MinWidth => 72;

        public WidgetViewModelBase Create(IServiceProvider services)
        {
            return (WidgetViewModelBase)services.GetService(typeof(CpuWidgetViewModel))!;
        }
    }

    /// <summary>内存组件插件。</summary>
    public sealed class MemoryPlugin : IWidgetPlugin
    {
        public string Id => "memory";
        public string DisplayName => "内存";
        public WidgetZone DefaultZone => WidgetZone.Right;
        public double MinWidth => 96;

        public WidgetViewModelBase Create(IServiceProvider services)
        {
            return (WidgetViewModelBase)services.GetService(typeof(MemoryWidgetViewModel))!;
        }
    }

    /// <summary>网络组件插件。</summary>
    public sealed class NetworkPlugin : IWidgetPlugin
    {
        public string Id => "network";
        public string DisplayName => "网络";
        public WidgetZone DefaultZone => WidgetZone.Right;
        public double MinWidth => 104;

        public WidgetViewModelBase Create(IServiceProvider services)
        {
            return (WidgetViewModelBase)services.GetService(typeof(NetworkWidgetViewModel))!;
        }
    }
}
