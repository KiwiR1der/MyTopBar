using System.Collections.ObjectModel;
using MyTopBar.Models.Settings;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 组件注册表：负责创建全部可用组件，并按配置决定启停与顺序。
    /// </summary>
    public sealed class WidgetRegistry
    {
        private readonly List<WidgetViewModelBase> _all = new();

        /// <summary>中区组件（按 Order 升序）。</summary>
        public ObservableCollection<WidgetViewModelBase> Center { get; } = new();

        /// <summary>右区组件（按 Order 升序）。</summary>
        public ObservableCollection<WidgetViewModelBase> Right { get; } = new();

        public IReadOnlyList<WidgetViewModelBase> All => _all;

        public void Add(WidgetViewModelBase widget)
        {
            ArgumentNullException.ThrowIfNull(widget);
            _all.Add(widget);
        }

        /// <summary>
        /// 按配置重建两个区域的组件顺序。未在配置中出现的组件视为启用并追加到末尾。
        /// </summary>
        public void Rebuild(IReadOnlyList<WidgetSettings> settings)
        {
            Center.Clear();
            Right.Clear();

            var orders = settings.ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);

            // 未配置的组件保持注册顺序（即代码中的默认顺序），已配置的按 Order 排。
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
        }

        public void DisposeAll()
        {
            foreach (WidgetViewModelBase widget in _all)
            {
                widget.Dispose();
            }

            _all.Clear();
            Center.Clear();
            Right.Clear();
        }
    }
}
