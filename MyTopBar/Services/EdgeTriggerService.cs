using System.Windows;
using System.Windows.Threading;
using MyTopBar.Infrastructure.Interop;

namespace MyTopBar.Services
{
    /// <summary>
    /// 屏幕左边缘触发检测。
    ///
    /// 为什么不用 WH_MOUSE_LL：
    ///   低级鼠标钩子会让系统每一次鼠标移动都同步回调到本进程，
    ///   高频移动时会明显拖慢整机响应。顶栏的「点外部收起」只在弹窗打开期间
    ///   短暂挂钩子，那是可以接受的；侧边栏需要常驻监听，用钩子代价太大。
    ///
    /// 改用 GlobalTickService 上 200ms 一次的光标轮询：
    ///   - GetCursorPos 是极轻量的系统调用，200ms 一次几乎无感
    ///   - 只在光标贴近屏幕左边缘时才判定触发，误触率低
    ///   - 复用已有的全局 Tick，不额外开定时器
    ///
    /// 触发模型（与 macOS / Surface 侧边栏一致）：
    ///   光标进入左边缘热区并停留片刻 → 滑出
    ///   光标离开热区且未被钉住 → 延迟后收起
    /// </summary>
    public sealed class EdgeTriggerService : IDisposable
    {
        /// <summary>热区宽度（物理像素）。太窄很难对准，太宽容易误触。</summary>
        private const int HotZoneWidthPx = 3;

        /// <summary>
        /// 停留时长。光标快速划过左边缘（例如去点桌面图标）不该触发，
        /// 必须稳定停留一小段时间才算「想打开」。
        /// </summary>
        private static readonly TimeSpan DwellTime = TimeSpan.FromMilliseconds(220);

        /// <summary>离开后延迟收起，给用户留时间把鼠标移进面板。</summary>
        private static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(400);

        private readonly GlobalTickService _tick;
        private readonly Dispatcher _dispatcher;

        private IDisposable? _subscription;
        private DateTime? _enteredAt;
        private DateTime? _leftAt;
        private bool _disposed;

        /// <summary>功能开关。关闭时不做任何判断。</summary>
        public bool IsEnabled { get; set; }

        /// <summary>光标贴近左边缘并已停留足够久。</summary>
        public event EventHandler? EdgeActivated;

        /// <summary>光标离开左边缘且超过收起延迟。</summary>
        public event EventHandler? EdgeDismissed;

        /// <summary>
        /// 侧边栏被用户钉住时置 true，此时不自动收起。
        ///
        /// 注意：不要用它表示「面板正开着」——那会让收起分支永远进不去。
        /// 面板打开期间光标停留在面板内，由 IsCursorOverPanel 负责。
        /// </summary>
        public bool HoldOpen { get; set; }

        /// <summary>
        /// 面板是否已展开。与 HoldOpen 分开：
        ///   - IsPanelOpen：面板开着，光标在面板矩形内算「还在里面」
        ///   - HoldOpen：用户钉住了，光标离开也不收
        /// 早期把两者合并成一个标志，导致面板一开 HoldOpen 就为 true，
        /// 收起分支永远进不去，面板再也收不起来。
        /// </summary>
        public bool IsPanelOpen { get; set; }

        /// <summary>
        /// 侧边栏面板在屏幕上的宽度（物理像素）。
        /// </summary>
        public int PanelWidthPx { get; set; } = 300;

        /// <summary>顶栏高度（物理像素）。面板从顶栏下沿开始，判定时要用到。</summary>
        public int BarHeightPx { get; set; } = 30;

        /// <summary>
        /// 正在播放展开/收起动画时置 true，暂停判定，避免动画期间反复触发。
        /// </summary>
        public bool Suspended { get; set; }

        public EdgeTriggerService(GlobalTickService tick, Dispatcher dispatcher)
        {
            _tick = tick;
            _dispatcher = dispatcher;
        }

        public void Start()
        {
            _subscription ??= _tick.Subscribe(TimeSpan.FromMilliseconds(200), OnTick);
        }

        public void Stop()
        {
            _subscription?.Dispose();
            _subscription = null;
            ResetState();
        }

        private void ResetState()
        {
            _enteredAt = null;
            _leftAt = null;
        }

        private void OnTick()
        {
            if (_disposed || !IsEnabled || Suspended)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;

            // 面板开着时，光标落在面板矩形内也算「还在里面」，否则一移进面板就收。
            bool inHotZone = IsCursorInHotZone() || (IsPanelOpen && IsCursorOverPanel());
            if (inHotZone)
            {
                // 回到热区：取消收起计时。
                _leftAt = null;

                if (_enteredAt is null)
                {
                    _enteredAt = now;
                    return;
                }

                // 停留够久才触发一次；触发后清空，避免停留期间反复触发。
                if (now - _enteredAt.Value >= DwellTime)
                {
                    _enteredAt = null;
                    Raise(EdgeActivated);
                }

                return;
            }

            // ---- 光标不在热区 ----
            _enteredAt = null;

            if (HoldOpen)
            {
                // 被钉住或正在交互：不收起，也不累计离开时间。
                _leftAt = null;
                return;
            }

            /*
             * 离开热区后开始计时，且每 tick 持续累计。
             *
             * 不能只在「刚离开那一帧」判断一次——那时 CollapseDelay 肯定还没到，
             * 状态被丢掉后计时就永远无法完成，收起逻辑等于失效。
             */
            _leftAt ??= now;

            if (now - _leftAt.Value < CollapseDelay)
            {
                return;
            }

            _leftAt = null;
            Raise(EdgeDismissed);
        }

        /// <summary>
        /// 光标是否落在已展开的面板区域内。
        /// 面板贴着屏幕左边缘、从顶栏下沿开始，所以 y 要加上顶栏高度。
        /// </summary>
        private bool IsCursorOverPanel()
        {
            if (!NativeMethods.TryGetMonitorRectAtCursor(out NativeMethods.RECT monitor)
                || !NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
            {
                return false;
            }

            int panelTop = monitor.Top + BarHeightPx;

            return pt.x >= monitor.Left
                && pt.x <= monitor.Left + PanelWidthPx
                && pt.y >= panelTop
                && pt.y <= monitor.Bottom;
        }

        private static bool IsCursorInHotZone()
        {
            if (!NativeMethods.TryGetMonitorRectAtCursor(out NativeMethods.RECT monitor))
            {
                // 取不到显示器信息时保守处理：不认为在热区，避免误弹。
                return false;
            }

            if (!NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
            {
                return false;
            }

            // 左边缘热区：水平方向紧贴左边界，垂直方向在整个屏幕高度内。
            return pt.x >= monitor.Left
                && pt.x <= monitor.Left + HotZoneWidthPx
                && pt.y >= monitor.Top
                && pt.y <= monitor.Bottom;
        }

        private void Raise(EventHandler? handler)
        {
            if (handler is null)
            {
                return;
            }

            if (_dispatcher.CheckAccess())
            {
                handler(this, EventArgs.Empty);
                return;
            }

            _dispatcher.BeginInvoke(() => handler(this, EventArgs.Empty));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }
    }
}
