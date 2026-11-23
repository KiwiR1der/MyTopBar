using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MyTopBar.Infrastructure.Common;
using MyTopBar.Infrastructure.Interop;

namespace MyTopBar.Views
{
    /// <summary>
    /// 装订在某个窗口上的外部点击监视器。
    ///
    /// 为什么需要它：
    ///   弹窗带 WS_EX_NOACTIVATE，永远不会成为活动窗口，
    ///   因此也永远收不到 Deactivated —— 无法再靠它实现
    ///   「点外面就收起」（Popup 的 StaysOpen=False 语义）。
    ///
    ///   这里用低级鼠标钩子补上这个语义。
    ///
    /// 为什么用钩子而不是定时轮询 GetAsyncKeyState：
    ///   轮询要么延迟明显（用户点完外部，菜单还挂着），要么频率高到浪费 CPU。
    ///   钩子只在鼠标事件发生时回调，零延迟、零空转。
    ///
    ///   代价是钩子期间每次鼠标移动都会回调到本进程。为了把影响压到最低：
    ///   - 只在弹窗打开期间挂钩，收起立刻摘掉
    ///   - 只在左/右键按下时判定（移动、滚轮直接放行）
    ///   - 回调里只做窗口矩形判定，不碰 UI 树
    ///
    /// 线程模型：
    ///   WH_MOUSE_LL 是全局钩子，回调在「安装它的线程」上执行，即 UI 线程。
    ///   所以这里可以安全地直接操作 WPF 窗口状态，不需要 Dispatcher 往返。
    ///   钩子回调里用 BeginInvoke 收起，避免在钩子内部修改窗口导致重入。
    /// </summary>
    internal sealed class OutsideClickMonitor : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;

        private readonly Window _owner;
        private readonly Action _onOutsideClick;

        /// <summary>委托必须存字段：否则会被 GC 回收，钩子回调踩空导致崩溃。</summary>
        private NativeMethods.LowLevelMouseProc? _proc;

        private IntPtr _hook = IntPtr.Zero;
        private bool _disposed;

        public OutsideClickMonitor(Window owner, Action onOutsideClick)
        {
            _owner = owner;
            _onOutsideClick = onOutsideClick;
        }

        /// <summary>开始监视。重复调用无副作用。</summary>
        public void Start()
        {
            if (_disposed || _hook != IntPtr.Zero)
            {
                return;
            }

            try
            {
                _proc = OnMouseEvent;
                _hook = NativeMethods.SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);

                if (_hook == IntPtr.Zero)
                {
                    AppLog.Error($"安装外部点击钩子失败，弹窗将无法通过点击外部收起（error={Marshal.GetLastWin32Error()}）");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("安装外部点击钩子异常", ex);
                _hook = IntPtr.Zero;
                _proc = null;
            }
        }

        /// <summary>停止监视。重复调用无副作用。</summary>
        public void Stop()
        {
            if (_hook == IntPtr.Zero)
            {
                return;
            }

            try
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
            }
            catch (Exception ex)
            {
                AppLog.Error("卸载外部点击钩子异常", ex);
            }
            finally
            {
                _hook = IntPtr.Zero;
                _proc = null;
            }
        }

        private IntPtr OnMouseEvent(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // nCode < 0 时必须直接交给下一个钩子，不能做任何处理。
            if (nCode < 0)
            {
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            int message = wParam.ToInt32();
            if (message != WM_LBUTTONDOWN && message != WM_RBUTTONDOWN)
            {
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            try
            {
                if (IsOutside())
                {
                    /*
                     * 不在钩子里直接收起：修改窗口可见性会触发一系列
                     * 消息与布局，在钩子内部做容易重入（例如 Hide 过程中
                     * 又产生一次 WM_LBUTTONDOWN）。投递到消息队列之后再做。
                     */
                    _owner.Dispatcher.BeginInvoke(_onOutsideClick);

                    // 外部点击照常透传给目标窗口，用户点哪儿就是点哪儿。
                    return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("外部点击判定异常", ex);
            }

            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>
        /// 当前光标是否落在弹窗矩形之外。
        ///
        /// 用屏幕坐标直接比对窗口矩形，而不是走 WPF 的 HitTest：
        /// 后者要遍历视觉树，放在钩子里太重。窗口是不规则圆角也不影响，
        /// 圆角处几像素的误差对「点外面收起」这个语义无感。
        /// </summary>
        private bool IsOutside()
        {
            if (!_owner.IsVisible)
            {
                return false;
            }

            IntPtr handle = new WindowInteropHelper(_owner).Handle;
            if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle))
            {
                return false;
            }

            if (!NativeMethods.GetWindowRect(handle, out NativeMethods.RECT rect))
            {
                // 取不到矩形时保守处理：当作在内部，不收起。
                // 误收起会打断用户操作，误保留只是多点一下。
                return false;
            }

            if (!NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
            {
                return false;
            }

            return pt.x < rect.Left || pt.x >= rect.Right
                || pt.y < rect.Top || pt.y >= rect.Bottom;
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
