using MyTopBar.Models.Settings;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 组件插件契约：元数据 + 工厂。
    ///
    /// 内置组件和第三方组件都实现此接口，WidgetRegistry 按插件配置装配。
    /// 第三方插件只需把 DLL 放进 plugins/ 目录，实现 IWidgetPlugin，
    /// 程序启动时会自动扫描加载。
    /// </summary>
    public interface IWidgetPlugin
    {
        /// <summary>稳定标识，用于配置持久化（如 "clock"、"weather"）。</summary>
        string Id { get; }

        /// <summary>设置界面显示名。</summary>
        string DisplayName { get; }

        /// <summary>默认所在区域。</summary>
        WidgetZone DefaultZone { get; }

        /// <summary>渲染宽度被压缩时的最小宽度。</summary>
        double MinWidth { get; }

        /// <summary>
        /// 创建组件实例。
        /// 内置插件从 DI 解析单例（同一实例会被重复返回），
        /// 第三方插件可自行决定实例策略——WidgetRegistry 只调用一次并缓存。
        /// </summary>
        WidgetViewModelBase Create(IServiceProvider services);
    }
}
