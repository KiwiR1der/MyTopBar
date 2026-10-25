using CommunityToolkit.Mvvm.ComponentModel;

namespace MyTopBar.Widgets
{
    /// <summary>组件所在的顶栏区域。</summary>
    public enum WidgetZone
    {
        Center = 0,
        Right = 1
    }

    /// <summary>
    /// 所有顶栏组件的基类。
    /// 组件自身只负责展示与数据刷新，不感知布局与持久化细节。
    /// </summary>
    public abstract partial class WidgetViewModelBase : ObservableObject, IDisposable
    {
        private bool _disposed;

        /// <summary>稳定标识，用于配置持久化。</summary>
        public abstract string Id { get; }

        /// <summary>设置界面显示名。</summary>
        public abstract string DisplayName { get; }

        /// <summary>默认所在区域。</summary>
        public abstract WidgetZone DefaultZone { get; }

        [ObservableProperty]
        private bool _isEnabled = true;

        /// <summary>渲染宽度被压缩时的最小宽度，供低优先级裁剪使用。</summary>
        public virtual double MinWidth => 0;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeCore();
            GC.SuppressFinalize(this);
        }

        /// <summary>派生类在此释放事件订阅与定时器。</summary>
        protected virtual void DisposeCore()
        {
        }
    }
}
