using System.Collections.ObjectModel;
using MyTopBar.Infrastructure.Common;
using MyTopBar.Models.Settings;
using MyTopBar.Widgets.Plugins;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 组件注册表：负责加载插件、创建组件实例，并按配置决定启停与顺序。
    ///
    /// Step B（插件化）：内置组件和第三方插件统一走 IWidgetPlugin 接口，
    /// WidgetRegistry 不再硬编码组件列表，而是按插件配置装配。
    ///
    /// 关键约束：内置组件是 DI 单例，实例只创建一次并缓存。
    /// Rebuild 只重排 Center/Right 集合，**绝不 Dispose 实例**——
    /// 组件的数据管道（定时器、事件订阅）在构造时建立，
    /// Dispose 后从 DI 再拿的还是同一个已销毁的单例，表现为
    /// 「改组件开关后时钟停走、指标定格」。
    /// </summary>
    public sealed class WidgetRegistry
    {
        private readonly IServiceProvider _services;
        private readonly List<IWidgetPlugin> _plugins = new();
        private readonly List<WidgetViewModelBase> _all = new();
        private bool _created;

        /// <summary>中区组件（按 Order 升序）。</summary>
        public ObservableCollection<WidgetViewModelBase> Center { get; } = new();

        /// <summary>右区组件（按 Order 升序）。</summary>
        public ObservableCollection<WidgetViewModelBase> Right { get; } = new();

        /// <summary>所有已创建的组件实例（含未启用的）。</summary>
        public IReadOnlyList<WidgetViewModelBase> All => _all;

        public WidgetRegistry(IServiceProvider services)
        {
            _services = services;

            // 加载内置插件（顺序即默认顺序：中区歌词/文本，右区网络/CPU/内存/天气/时钟）
            _plugins.Add(new LyricsPlugin());
            _plugins.Add(new TextPlugin());
            _plugins.Add(new NetworkPlugin());
            _plugins.Add(new CpuPlugin());
            _plugins.Add(new MemoryPlugin());
            _plugins.Add(new WeatherPlugin());
            _plugins.Add(new ClockPlugin());

            // 加载第三方插件（plugins/ 目录）
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (IWidgetPlugin plugin in PluginLoader.LoadPlugins(baseDir))
            {
                // 插件 ID 与内置冲突时，插件优先（允许覆盖内置行为）
                _plugins.RemoveAll(p => string.Equals(p.Id, plugin.Id, StringComparison.OrdinalIgnoreCase));
                _plugins.Add(plugin);
            }
        }

        /// <summary>
        /// 惰性创建全部组件实例。只执行一次，之后 Rebuild 复用缓存。
        /// </summary>
        private void EnsureCreated()
        {
            if (_created)
            {
                return;
            }

            _created = true;
            foreach (IWidgetPlugin plugin in _plugins)
            {
                try
                {
                    WidgetViewModelBase widget = plugin.Create(_services);
                    _all.Add(widget);
                }
                catch (Exception ex)
                {
                    AppLog.Error($"组件实例化失败：{plugin.Id}", ex);
                }
            }
        }

        /// <summary>
        /// 按配置重排两个区域的组件顺序。未在配置中出现的组件视为启用并追加到末尾。
        /// 只操作集合，不销毁实例。
        /// </summary>
        public void Rebuild(IReadOnlyList<WidgetSettings> settings)
        {
            EnsureCreated();
            Center.Clear();
            Right.Clear();

            var orders = settings.ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);

            // 未配置的组件保持创建顺序（即插件注册顺序），已配置的按 Order 排。
            // 用 OrderBy 的稳定性即可实现：先按是否配置分组，再按 Order，最后保留原索引。
            var indexed = _all.Select((w, i) => (Widget: w, Index: i)).ToList();

            var enabled = indexed
                .Where(x => !orders.TryGetValue(x.Widget.Id, out WidgetSettings? s) || s.Enabled)
                .OrderBy(x => orders.TryGetValue(x.Widget.Id, out WidgetSettings? s) ? s.Order : int.MaxValue)
                .ThenBy(x => x.Index)
                .Select(x => x.Widget)
                .ToList();

            foreach (WidgetViewModelBase widget in enabled)
            {
                (widget.DefaultZone == WidgetZone.Center ? Center : Right).Add(widget);
            }

            // 同步 IsEnabled：被配置禁用的组件即使不在渲染集合里，实例状态也要一致，
            // 否则 PersistWidgetOrder 读到的仍是旧状态，会把刚关掉的组件又写成启用。
            foreach (WidgetViewModelBase widget in _all)
            {
                widget.IsEnabled = !orders.TryGetValue(widget.Id, out WidgetSettings? s) || s.Enabled;
            }
        }

        /// <summary>按 ID 查找插件（设置界面用）。</summary>
        public IWidgetPlugin? FindPlugin(string id)
        {
            return _plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>所有可用插件（含未启用的），供设置界面展示。</summary>
        public IReadOnlyList<IWidgetPlugin> AllPlugins => _plugins;

        /// <summary>
        /// 销毁全部实例。**只在进程退出时调用**（MainViewModel.Dispose）。
        /// </summary>
        public void DisposeAll()
        {
            foreach (WidgetViewModelBase widget in _all)
            {
                widget.Dispose();
            }

            _all.Clear();
            Center.Clear();
            Right.Clear();
            _created = false;
        }
    }
}
