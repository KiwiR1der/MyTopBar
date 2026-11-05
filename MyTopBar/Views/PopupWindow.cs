using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.Infrastructure.Interop;
using MyTopBar.Infrastructure.Material;
using MyTopBar.Services;

namespace MyTopBar.Views
{
    /// <summary>
    /// 顶栏弹窗的统一窗口容器。
    ///
    /// 取代原先的 WPF <c>Popup</c>。每个弹窗是一个独立 Window，
    /// 位置在打开时一次性算准（相对顶栏的锚点元素），之后不再移动。
    ///
    /// 为什么必须能激活：
    ///   WPF 的 Popup 靠 <c>StaysOpen=False</c> + 外部点击钩子收起，
    ///   改成 Window 后最自然的收起方式是 <c>Deactivated</c>。
    ///   但非激活窗口永远收不到 Deactivated，所以弹窗必须可激活。
    ///
    ///   为了不破坏「点顶栏不抢当前窗口焦点」的既有语义，
    ///   打开前先记住原前台窗口，关闭时用 ForceForeground 还回去。
    /// </summary>
    public class PopupWindow : Window
    {
        private readonly ThemeService _theme;

        /// <summary>打开前的原前台窗口，关闭时归还焦点。</summary>
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

            // 背景必须全透明：毛玻璃靠 DWM 模糊透出，WPF 自己画的底色会盖住它。
            Background = Brushes.Transparent;

            SizeToContent = SizeToContent.WidthAndHeight;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            if (Application.Current.Resources["AppFont"] is FontFamily font)
            {
                FontFamily = font;
            }

            ContentHost = new Border
            {
                Padding = padding,
                CornerRadius = new CornerRadius(cornerRadius),
                MinWidth = minWidth,
                Background = (Brush)Application.Current.Resources["PopupGlassBrush"],
                BorderBrush = (Brush)Application.Current.Resources["MenuBorderBrush"],
                BorderThickness = new Thickness(1),
                Child = content
            };

            Content = new Grid { Background = Brushes.Transparent, Children = { ContentHost } };

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Deactivated += OnDeactivated;
            Closed += OnClosed;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;

            if (PresentationSource.FromVisual(this) is HwndSource source && source.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            GlassEffect.Apply(handle, _theme.IsDark, roundCorners: true);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            GlassEffect.SetTransparentClientArea(this);
            InvalidateVisual();
        }

        /// <summary>
        /// 窗口失活即收起——这就是 Popup 的 StaysOpen=False 行为。
        /// </summary>
        private void OnDeactivated(object? sender, EventArgs e)
        {
            Collapse();
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
            RestoreForeground();
        }

        /// <summary>把焦点还给打开前的窗口。</summary>
        private void RestoreForeground()
        {
            if (_previousForeground == IntPtr.Zero)
            {
                return;
            }

            // 只有当原窗口仍然存在、且我们确实拿走了焦点时才归还，
            // 避免把用户主动切换到别的窗口的前台状态抢回来。
            if (NativeMethods.IsWindow(_previousForeground))
            {
                NativeMethods.ForceForeground(_previousForeground);
            }

            _previousForeground = IntPtr.Zero;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            SourceInitialized -= OnSourceInitialized;
            Loaded -= OnLoaded;
            Deactivated -= OnDeactivated;
            Closed -= OnClosed;

            RestoreForeground();
        }

        /// <summary>
        /// 显示弹窗。位置在 Show 之前按锚点算好，显示后不再移动。
        /// </summary>
        public void Open()
        {
            if (IsVisible)
            {
                return;
            }

            _previousForeground = NativeMethods.GetForegroundWindow();
            _positioned = false;

            Show();

            // SizeToContent 生效后 ActualWidth/Height 才有值，此时定位才准确。
            UpdateLayout();
            PositionOnce();

            Activate();
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
        /// 对应原先 PopupHost.Toggle 的行为。
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
