using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using FluentWpfCore.AttachedProperties;
using FluentWpfCore.Interop;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.Infrastructure.Interop;
using MyTopBar.Services;

namespace MyTopBar.Views
{
    /// <summary>
    /// 顶栏弹窗的统一窗口容器。
    ///
    /// 取代原先的 WPF <c>Popup</c>。每个弹窗是一个独立 Window，
    /// 位置在打开时一次性算准（相对顶栏的锚点元素），之后不再移动。
    ///
    /// 【为什么激活式弹窗会导致「点击穿透」】
    ///   早期实现让弹窗可激活，靠 Deactivated 事件收起（等价于 Popup 的
    ///   StaysOpen=False）。这在顶栏上不可靠，原因是「前台锁」：
    ///
    ///   顶栏自身带 WS_EX_NOACTIVATE，本进程因此从不被系统认作前台进程。
    ///   当用户刚操作过别的程序时，本进程没有前台权限，
    ///   Show() + Activate() 会被前台锁规则静默拒绝 —— 弹窗虽然画了出来，
    ///   却不是活动窗口。此时鼠标点击不会投递给它：Windows 把点击送给系统
    ///   判定的活动窗口，也就是菜单底下那个 App。
    ///   用户看到菜单还亮着，点菜单项却操作了后面的程序，这就是「穿透」。
    ///
    /// 【现在的做法】
    ///   1. 弹窗自带 WS_EX_NOACTIVATE，从设计上就不抢焦点，
    ///      也就不存在「前台锁拒绝激活」这回事，行为变得确定。
    ///   2. 收起不再依赖 Deactivated（非激活窗口永远收不到），
    ///      改为打开期间临时挂低级鼠标钩子：点在弹窗外收起，点在自己身上放行。
    ///   3. 用 SW_SHOWNOACTIVATE 显示，保证显示动作本身不激活窗口。
    ///
    ///   代价：弹窗内控件拿不到键盘焦点（当前弹窗全是按钮，不需要键盘输入）。
    ///   收益与顶栏一致：点开菜单不会让用户正在编辑的窗口失焦。
    /// </summary>
    public class PopupWindow : Window
    {
        private readonly ThemeService _theme;
        private readonly WindowMaterial _windowMaterial;

        /// <summary>打开期间的外部点击监视器，收起时立刻摘掉。</summary>
        private readonly OutsideClickMonitor _outsideClick;

        /// <summary>打开前的原前台窗口。仅在没有外部点击时用于兜底归还焦点。</summary>
        private IntPtr _previousForeground = IntPtr.Zero;

        /// <summary>本次打开是否已经算过位置。</summary>
        private bool _positioned;

        /// <summary>
        /// 锚点元素：弹窗相对它定位。由调用方在 Open 前设置。
        /// </summary>
        public FrameworkElement? Anchor { get; set; }

        /// <summary>弹窗相对锚点元素的对齐方式。</summary>
        public PopupAlignment Alignment { get; set; } = PopupAlignment.BottomLeft;

        /// <summary>
        /// 右对齐时的固定右边距（DIP）。
        ///
        /// 非 null 时不再用锚点宽度算右边，而是让弹窗右边缘固定在
        /// 「显示器右边缘 - 该值」。右区面板的宽度会随中区内容变化
        /// （共享列宽），用它当锚点会导致弹窗每次位置都不一样。
        /// </summary>
        public double? FixedRightMargin { get; set; }

        /// <summary>弹窗与锚点之间的间距（DIP）。</summary>
        public double Gap { get; set; } = 6;

        /// <summary>
        /// 内容区。构造时把 UserControl 塞进来。
        /// </summary>
        protected Border ContentHost { get; }

        public PopupWindow(UIElement content, Thickness padding, double cornerRadius, double minWidth)
        {
            _theme = App.Current.Services.GetRequiredService<ThemeService>();

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            AllowsTransparency = false;

            /*
             * 不接管激活：本窗口永不被激活，点击也就不会被前台锁规则吞掉。
             * 收起依靠 OutsideClickMonitor，见类注释。
             */
            ShowActivated = false;

            // 背景必须全透明：毛玻璃靠 DWM 模糊透出，WPF 自己画的底色会盖住它。
            Background = Brushes.Transparent;

            SizeToContent = SizeToContent.WidthAndHeight;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            SetResourceReference(FontFamilyProperty, "AppFont");

            ContentHost = new Border
            {
                Padding = padding,
                CornerRadius = new CornerRadius(cornerRadius),
                MinWidth = minWidth,
                BorderThickness = new Thickness(1),
                Child = content
            };
            ContentHost.SetResourceReference(Border.BackgroundProperty, "PopupGlassBrush");
            ContentHost.SetResourceReference(Border.BorderBrushProperty, "MenuBorderBrush");

            Content = new Grid { Background = Brushes.Transparent, Children = { ContentHost } };

            _outsideClick = new OutsideClickMonitor(this, Collapse);

            WindowMaterial.SetWindowCorner(this, MaterialApis.WindowCorner.Round);
            _windowMaterial = new WindowMaterial
            {
                MaterialMode = MaterialType.Acrylic,
                UseWindowComposition = true,
                WindowChromeEx = new WindowChrome
                {
                    CaptionHeight = 0,
                    CornerRadius = new CornerRadius(cornerRadius),
                    GlassFrameThickness = new Thickness(-1),
                    ResizeBorderThickness = new Thickness(0)
                }
            };
            SyncWindowMaterial();
            WindowMaterial.SetMaterial(this, _windowMaterial);

            SourceInitialized += OnSourceInitialized;
            Closed += OnClosed;
            _theme.BackdropChanged += OnBackdropChanged;
        }

        private void OnBackdropChanged(object? sender, EventArgs e)
            => SyncWindowMaterial();

        private void SyncWindowMaterial()
            => _theme.SyncWindowMaterial(_windowMaterial, isBar: false);

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;

            /*
             * 关键：给弹窗自己加 WS_EX_NOACTIVATE。
             *
             * 必须在这里（HWND 已创建、尚未显示）设置，Show 之前生效。
             * 这样弹窗永远不会成为活动窗口，点击一律由它自己接收，
             * 不会再穿透到菜单下方的程序。
             */
            NativeMethods.MakeNonActivating(handle);
        }

        /// <summary>
        /// 收起弹窗。
        ///
        /// 用 Hide() 而不是 Close()：窗体的 Close 会销毁 HWND，
        /// 之后再也无法 Show()（抛 InvalidOperationException）。
        /// 弹窗需要反复开关，必须只隐藏、不销毁。
        /// </summary>
        public void Collapse()
        {
            if (!IsVisible)
            {
                return;
            }

            Hide();
            _outsideClick.Stop();
            RestoreForeground();
        }

        /// <summary>
        /// 把焦点还给打开前的窗口。
        ///
        /// 仅作为兜底：正常路径是非激活弹窗压根没抢过焦点，
        /// 用户点外部收起时前台本就还是原来那个窗口，
        /// ForceForeground 会把前台再抢一次，反而可能打断用户刚点的新窗口。
        /// 因此只有在「原前台 == 当前前台」时才归还，见下方判断。
        /// </summary>
        private void RestoreForeground()
        {
            if (_previousForeground == IntPtr.Zero)
            {
                return;
            }

            IntPtr saved = _previousForeground;
            _previousForeground = IntPtr.Zero;

            if (!NativeMethods.IsWindow(saved))
            {
                return;
            }

            IntPtr current = NativeMethods.GetForegroundWindow();

            // 用户已经切到别的窗口了，不要打断他。
            if (current != IntPtr.Zero && current != saved)
            {
                return;
            }

            NativeMethods.ForceForeground(saved);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            SourceInitialized -= OnSourceInitialized;
            Closed -= OnClosed;
            _theme.BackdropChanged -= OnBackdropChanged;

            _outsideClick.Dispose();
            RestoreForeground();
        }

        /// <summary>显示弹窗。位置在 Show 之前按锚点算好，显示后不再移动。</summary>
        public void Open()
        {
            if (IsVisible)
            {
                return;
            }

            _previousForeground = NativeMethods.GetForegroundWindow();
            _positioned = false;

            try
            {
                Show();
            }
            catch (InvalidOperationException)
            {
                // 窗口已关闭无法再显示（理论上不会走到，防御性处理）。
                return;
            }

            // SizeToContent 生效后 ActualWidth/Height 才有值，此时定位才准确。
            UpdateLayout();
            PositionOnce();

            /*
             * 显式用「不激活」方式显示。
             *
             * 窗口本身已经带 WS_EX_NOACTIVATE，正常不会激活；
             * 但 Show() 默认走 SW_SHOW，某些情况下仍会尝试抢前台，
             * 被前台锁拒绝后窗口处于「半前台」状态，正是穿透的来源。
             * 这里补一次 SW_SHOWNOACTIVATE，确保语义明确。
             */
            IntPtr handle = new WindowInteropHelper(this).Handle;
            NativeMethods.ShowWindow(handle, NativeMethods.SW_SHOWNOACTIVATE);

            // 保持置顶组内最前，避免被其他 Topmost 窗口压住。
            NativeMethods.SetWindowPos(
                handle,
                NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

            _outsideClick.Start();
        }

        /// <summary>
        /// 以指定锚点打开。每次调用都重新设置锚点与对齐方式，
        /// 保证同一个弹窗在不同组件上打开时位置正确。
        /// </summary>
        public void OpenAround(FrameworkElement anchor, PopupAlignment alignment)
        {
            Anchor = anchor;
            Alignment = alignment;
            Open();
        }

        /// <summary>
        /// 切换显隐。已显示则收起，否则按锚点打开。
        /// </summary>
        public void ToggleAround(FrameworkElement anchor, PopupAlignment alignment)
        {
            if (IsVisible)
            {
                Collapse();
                return;
            }

            OpenAround(anchor, alignment);
        }

        /// <summary>按锚点元素计算位置。只在打开时执行一次。</summary>
        private void PositionOnce()
        {
            if (_positioned || Anchor is null)
            {
                return;
            }

            if (!TryGetAnchorScreenRect(Anchor, out Rect anchorRect))
            {
                return;
            }

            /*
             * 全部用 DIP 计算。
             *
             * TryGetAnchorScreenRect 已经把 PointToScreen 返回的物理像素
             * 换算成 DIP，ActualWidth/Height 也是 DIP，所以这里不能再乘 scale。
             * 早期写法里出现 `anchorRect.Right * scale - width` 这种混用，
             * 右对齐时整体偏移，弹窗跑到屏幕中间而不是贴住右区面板。
             */
            double width = ActualWidth > 0 ? ActualWidth : ContentHost.ActualWidth;
            double top = anchorRect.Bottom + Gap;

            double left = Alignment switch
            {
                // 左对齐：弹窗左边缘 = 锚点左边缘
                PopupAlignment.BottomLeft => anchorRect.Left,

                /*
                 * 右对齐。
                 *
                 * 设了 FixedRightMargin 就按屏幕右边缘计算，不看锚点宽度——
                 * RightPanel 在 SharedSizeGroup 列里，宽度会随中区内容变化，
                 * 拿它当基准会让同一个弹窗每次打开位置都不一样。
                 */
                PopupAlignment.BottomRight => FixedRightMargin is { } margin
                    ? ScreenRightDip - margin - width
                    : anchorRect.Right - width,

                // 居中：弹窗中心 = 锚点中心
                PopupAlignment.BottomCenter => anchorRect.Left + ((anchorRect.Width - width) / 2),

                _ => anchorRect.Left
            };

            // 夹在屏幕内，避免右侧弹窗（音量）溢出到屏幕外只露一角。
            ClampToScreen(ref left, ref top, width);

            Left = left;
            Top = top;

            _positioned = true;
        }

        /// <summary>
        /// 把弹窗限制在锚点所在显示器的可见区域内。
        ///
        /// 音量弹窗锚定最右侧的按钮、宽度却有 248，左对齐会明显溢出屏幕，
        /// 表现为「弹窗被切成一条」。这里做一次边界修正。
        /// </summary>
        private void ClampToScreen(ref double left, ref double top, double width)
        {
            if (!NativeMethods.TryGetMonitorRectAtCursor(out NativeMethods.RECT monitor))
            {
                return;
            }

            double scale = GetDpiScale();
            double monitorLeft = monitor.Left / scale;
            double monitorRight = monitor.Right / scale;
            double monitorTop = monitor.Top / scale;
            double monitorBottom = monitor.Bottom / scale;

            double height = ActualHeight > 0 ? ActualHeight : ContentHost.ActualHeight;

            // 右侧溢出就往左收
            if (left + width > monitorRight)
            {
                left = monitorRight - width;
            }

            // 左侧同样兜底
            if (left < monitorLeft)
            {
                left = monitorLeft;
            }

            if (top + height > monitorBottom)
            {
                top = Math.Max(monitorTop, monitorBottom - height);
            }

            if (top < monitorTop)
            {
                top = monitorTop;
            }
        }

        /// <summary>
        /// 显示器右边缘（DIP）。FixedRightMargin 用得上。
        ///
        /// monitor.Right 是物理像素，必须除以 DPI 缩放才是 WPF 坐标；
        /// 在 125%/150% 缩放下直接用会让弹窗跑到屏幕外。
        /// </summary>
        private double ScreenRightDip
        {
            get
            {
                if (!NativeMethods.TryGetMonitorRectAtCursor(out NativeMethods.RECT monitor))
                {
                    return 0;
                }

                double scale = GetDpiScale();
                return scale > 0 ? monitor.Right / scale : monitor.Right;
            }
        }

        private double GetDpiScale()
        {
            if (PresentationSource.FromVisual(this) is HwndSource source
                && source.CompositionTarget is not null)
            {
                double s = source.CompositionTarget.TransformToDevice.M11;
                if (s > 0)
                {
                    return s;
                }
            }

            return 1.0;
        }

        /// <summary>
        /// 取锚点元素在屏幕上的矩形（DIP，相对虚拟屏幕原点）。
        /// </summary>
        private static bool TryGetAnchorScreenRect(FrameworkElement anchor, out Rect rect)
        {
            rect = Rect.Empty;

            if (PresentationSource.FromVisual(anchor) is not HwndSource source)
            {
                return false;
            }

            try
            {
                Point topLeft = anchor.PointToScreen(new Point(0, 0));
                Point bottomRight = anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight));

                double scale = source.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                if (scale <= 0)
                {
                    scale = 1.0;
                }

                // PointToScreen 返回物理像素，换算回 DIP 以便后续统一处理。
                rect = new Rect(
                    topLeft.X / scale,
                    topLeft.Y / scale,
                    (bottomRight.X - topLeft.X) / scale,
                    (bottomRight.Y - topLeft.Y) / scale);

                return true;
            }
            catch (InvalidOperationException)
            {
                // 锚点不在视觉树里（例如顶栏已隐藏）时放弃定位。
                return false;
            }
        }
    }

    /// <summary>弹窗相对锚点的对齐方式。</summary>
    public enum PopupAlignment
    {
        /// <summary>弹窗左边缘对齐锚点左边缘。</summary>
        BottomLeft,

        /// <summary>弹窗右边缘对齐锚点右边缘。</summary>
        BottomRight,

        /// <summary>弹窗水平居中对齐锚点。</summary>
        BottomCenter
    }
}
