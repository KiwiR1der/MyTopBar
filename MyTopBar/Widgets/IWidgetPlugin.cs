using MyTopBar.Models.Settings;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 组件插件契约：元数据 + 工厂。
    ///
    /// 内置组件和第三方组件都实现此接口，WidgetRegistry 按插件配置装配。
    ///
    /// 第三方插件的**已知限制**（别按「即插即用」宣传）：
    ///   - DLL 必须放在 exe 旁的 plugins/ 目录，启动时扫描；
    ///   - 加载走默认 AssemblyLoadContext，插件自带的依赖（例如另一个版本的
    ///     CommunityToolkit.Mvvm）会被绑定到宿主已加载的版本，容易 FileLoadException
    ///     或类型不匹配。详见 <see cref="PluginLoader"/> 的安全模型说明；
    ///   - 插件是完全信任的代码，与本进程同权限运行；
    ///   - MainViewModel 的配置分发仍按内置 Id 硬编码，第三方插件拿不到自己的
    ///     配置入口，只能读 <c>WidgetSettings.Options</c> 自解析。
    /// </summary>
    public interface IWidgetPlugin
    {
        /// <summary>稳定标识，用于配置持久化（如 "clock"、"weather"）。</summary>
        string Id { get; }

        /// <summary>设置界面显示名。</summary>
        string DisplayName { get; }

        /// <summary>默认所在区域。</summary>
        WidgetZone DefaultZone { get; }

        /// <summary>
        /// 创建组件实例。
        /// 内置插件从 DI 解析单例（同一实例会被重复返回），
        /// 第三方插件可自行决定实例策略——WidgetRegistry 只调用一次并缓存。
        /// </summary>
        WidgetViewModelBase Create(IServiceProvider services);
    }
}
