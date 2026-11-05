using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.Infrastructure.Common;
using MyTopBar.Infrastructure.Interop;
using MyTopBar.Infrastructure.Material;
using MyTopBar.Services;
using MyTopBar.ViewModels;
using MyTopBar.Widgets;

namespace MyTopBar
{
    public partial class MainWindow : Window
    {
        private readonly AppBarController _appBarController;
        private readonly MainViewModel _viewModel;
        private readonly SettingsService _settings;
        private readonly ThemeService _theme;
        private readonly PopupHost _appleMenu;
        private readonly PopupHost _clockMenu;
        private readonly PopupHost _weatherMenu;
        private readonly PopupHost _cpuDetailMenu;
        private readonly PopupHost _memoryDetailMenu;
        private readonly PopupHost _audioMenu;
        private readonly PopupHost _mediaControlMenu;

        private readonly Views.SidebarWindow _sidebar;
        private readonly EdgeTriggerService _edge;

        private readonly DispatcherTimer _sortModeTimer = new();
        private Point _pressOrigin;
        private bool _isSortMode;
        private WidgetViewModelBase? _dragWidget;

        /// <summary>排序模式期间的低级键盘钩子，只用于监听 Esc。</summary>
        private IntPtr _escapeHook = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc? _escapeHookProc;

        public MainWindow()
        {
            _viewModel = App.Current.Services.GetRequiredService<MainViewModel>();
            _settings = App.Current.Services.GetRequiredService<SettingsService>();
            _theme = App.Current.Services.GetRequiredService<ThemeService>();
            DataContext = _viewModel;

            InitializeComponent();

            double height = Math.Clamp(_settings.Current.Appearance.BarHeight, 28, 40);
            Height = height;

            _appBarController = new AppBarController(this, AppBarController.AppBarEdge.Top, height);
            _appBarController.FullScreenAppChanged += OnFullScreenAppChanged;
            _appleMenu = new PopupHost(AppleMenuPopup);
            _clockMenu = new PopupHost(ClockPopup);
            _weatherMenu = new PopupHost(WeatherPopup);
            _audioMenu = new PopupHost(AudioPopup);
            _mediaControlMenu = new PopupHost(MediaControlPopup);

            // CPU / 内存共用同一个视图类型，但各需一份独立的 ViewModel（数据源不同）。
            var metrics = App.Current.Services.GetRequiredService<SystemMetricsService>();
            CpuDetailView.Attach(new SystemDetailViewModel(metrics, Dispatcher, SystemDetailViewModel.DetailKind.Cpu));
            MemoryDetailView.Attach(new SystemDetailViewModel(metrics, Dispatcher, SystemDetailViewModel.DetailKind.Memory));
            _cpuDetailMenu = new PopupHost(CpuDetailPopup);
            _memoryDetailMenu = new PopupHost(MemoryDetailPopup);

            // 侧边栏与边缘触发。窗口先建好但不显示，滑出时再展开。
            _sidebar = new Views.SidebarWindow();
            _sidebar.OpenStateChanged += OnSidebarOpenStateChanged;

            _edge = App.Current.Services.GetRequiredService<EdgeTriggerService>();
            _edge.EdgeActivated += OnEdgeActivated;
            _edge.EdgeDismissed += OnEdgeDismissed;

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closed += OnClosed;

            _theme.ThemeChanged += OnThemeChanged;
            _viewModel.AppearanceChanged += OnAppearanceChanged;

            // 长按左键 3 秒进入组件排序模式
            _sortModeTimer.Interval = TimeSpan.FromSeconds(3);
            _sortModeTimer.Tick += OnSortModeTimerTick;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (PresentationSource.FromVisual(this) is HwndSource source && source.CompositionTarget is not null)
            {
                // 关键：让 WPF 不绘制不透明背景，DWM 材质才能透出。
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            /*
             * 顶栏不能抢走前台窗口的焦点。
             * macOS 的菜单栏点一下就点一下，用户的编辑器不会失焦；
             * 这里靠 WS_EX_NOACTIVATE + ShowActivated="False" 达到同样效果。
             *
             * 顺带加 WS_EX_TOOLWINDOW，避免在 Alt+Tab 里出现一条 30px 的窗口。
             */
            NativeMethods.MakeNonActivating(handle);

            _theme.Apply(_settings.Current.Appearance.Theme);
            ApplyBlurMaterial(_theme.IsDark, handle);
        }

        private void OnThemeChanged(object? sender, bool isDark)
        {
            // 毛玻璃本身不随明暗变化（它只是模糊背景），
            // 但 DWM 的明暗标志要同步，边框/阴影颜色会跟着变。
            ApplyBlurMaterial(isDark, new WindowInteropHelper(this).Handle);
        }

        /// <summary>设置窗口改动后立即应用：高度、组件启停。</summary>
        private void OnAppearanceChanged(object? sender, EventArgs e)
        {
            double height = Math.Clamp(_settings.Current.Appearance.BarHeight, 28, 40);
            if (Math.Abs(Height - height) > 0.5)
            {
                Height = height;
                _appBarController.SetThickness(height);
            }

            // 设置里可能刚改了侧边栏开关，同步启停边缘检测。
            RestartSidebar();
        }

        /// <summary>
        /// 全屏应用进入 / 退出时显隐顶栏。
        ///
        /// macOS 的菜单栏在全屏应用下会自动收起，这里对齐这个行为——
        /// 否则全屏看视频、玩游戏时，顶栏会一直压在画面顶端。
        ///
        /// 用 Visibility 而不是 Window.Hide()：
        ///   - Hide() 会让 WPF 卸载整个视觉树，恢复时组件要重新布局、重新取数，
        ///     表现为顶栏闪一下、歌词/天气短暂空白
        ///   - Visibility.Hidden 只是让窗口不被绘制，布局和绑定都保持，
        ///     恢复时是无闪现的
        ///
        /// 窗口本身不能真正关闭或注销 AppBar：工作区占位要保留，
        /// 否则退出全屏的瞬间系统会把工作区还给最大化窗口，再收缩一次，
        /// 屏幕内容会明显跳一下。
        /// </summary>
        private void OnFullScreenAppChanged(object? sender, bool isFullScreen)
        {
            /*
             * 进入全屏前先把所有弹窗收掉。
             * 顶栏隐藏后弹窗仍会浮在最上层（Popup 是独立 HWND），
             * 用户在全屏画面里看到一块孤零零的日历/音量面板，很怪。
             */
            if (isFullScreen)
            {
                _appleMenu.Close();
                _clockMenu.Close();
                _weatherMenu.Close();
                _audioMenu.Close();
                _mediaControlMenu.Close();
                _cpuDetailMenu.Close();
                _memoryDetailMenu.Close();
            }

            Visibility = isFullScreen ? Visibility.Hidden : Visibility.Visible;

            AppLog.Info(isFullScreen ? "检测到全屏应用，隐藏顶栏" : "全屏应用退出，恢复顶栏");
        }

        #region 屏幕边缘侧边栏

        /// <summary>
        /// 按配置启动侧边栏与边缘检测。
        ///
        /// 剪贴板监听只要侧边栏功能开着就启动 —— 这样用户第一次滑出时
        /// 列表里已经有最近复制过的东西，而不是空的。
        /// </summary>
        private void StartSidebar()
        {
            Models.Settings.SidebarSettings config = _settings.Current.Sidebar;

            ClipboardHistoryService clips = App.Current.Services.GetRequiredService<ClipboardHistoryService>();
            clips.MaxItems = Math.Clamp(config.MaxClipItems, 5, 200);

            if (!config.Enabled)
            {
                return;
            }

            clips.Start();

            _sidebar.BarHeightDip = Math.Clamp(_settings.Current.Appearance.BarHeight, 28, 40);

            _edge.IsEnabled = true;
            _edge.HoldOpen = config.PinOnOpen;
            _edge.PanelWidthPx = (int)Math.Ceiling(300 * GetDpiScale());
            _edge.BarHeightPx = (int)Math.Ceiling(_sidebar.BarHeightDip * GetDpiScale());
            _edge.Start();

            if (config.PinOnOpen)
            {
                _sidebar.IsPinned = true;
            }
        }

        /// <summary>当前窗口的 DPI 缩放比，用于把 DIP 换算成物理像素。</summary>
        private double GetDpiScale()
        {
            if (PresentationSource.FromVisual(this) is HwndSource source
                && source.CompositionTarget is not null)
            {
                double scale = source.CompositionTarget.TransformToDevice.M11;
                if (scale > 0)
                {
                    return scale;
                }
            }

            return 1.0;
        }

        /// <summary>设置窗口里改动侧边栏开关后调用。</summary>
        public void RestartSidebar()
        {
            _edge.Stop();
            _edge.IsEnabled = false;

            if (_sidebar.IsOpen)
            {
                _sidebar.Collapse();
            }

            StartSidebar();
        }

        private void OnEdgeActivated(object? sender, EventArgs e)
        {
            if (!_edge.Suspended)
            {
                _sidebar.Open();
            }
        }

        private void OnEdgeDismissed(object? sender, EventArgs e)
        {
            if (!_sidebar.IsPinned)
            {
                _sidebar.Collapse();
            }
        }

        /// <summary>
        /// 侧边栏开合时同步触发服务的状态。
        ///
        /// 面板开着期间必须 HoldOpen，否则光标移进面板本身会被判为
        /// 「离开边缘」，刚滑出来就自己收回去。
        /// </summary>
        private void OnSidebarOpenStateChanged(object? sender, bool isOpen)
        {
            // 动画期间暂停判定，避免宽度变化过程中反复触发。
            _edge.Suspended = true;

            /*
             * 两个状态必须分开：
             *   IsPanelOpen —— 面板开着，光标在面板矩形内算「还在里面」
             *   HoldOpen    —— 用户钉住了，光标离开也不收
             * 早期这里写成 HoldOpen = isOpen || IsPinned，导致面板一开就
             * 永不收起（收起分支被 HoldOpen 挡住）。
             */
            _edge.IsPanelOpen = isOpen;
            _edge.HoldOpen = _sidebar.IsPinned;

            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(260)
            };

            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _edge.Suspended = false;
            };

            timer.Start();
        }

        #endregion

        /// <summary>
        /// 应用毛玻璃：委托给 GlassEffect。
        /// 材质固定为 BLURBEHIND 的理由见 GlassEffect / MaterialInfo 的注释。
        /// </summary>
        private void ApplyBlurMaterial(bool isDark, IntPtr handle)
        {
            GlassEffect.Apply(handle, isDark, roundCorners: false);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Start();
            StartSidebar();
            HookSortGestures();

            // 句柄在 Loaded 阶段一定有效。
            // CompositionTarget 必须在窗口已渲染后再置透明，否则会被 WPF 的首次合成覆盖成黑色，
            // 这正是之前整条顶栏呈纯黑 RGB(0,0,0) 的原因。
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (PresentationSource.FromVisual(this) is HwndSource source && source.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            // 直接按配置重新解析一次明暗，避免依赖事件到达顺序
            // （SourceInitialized 可能晚于 Loaded，导致这里读到过期的 _theme.IsDark）。
            _theme.Apply(_settings.Current.Appearance.Theme);

            ApplyBlurMaterial(_theme.IsDark, handle);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _appBarController.FullScreenAppChanged -= OnFullScreenAppChanged;

            _edge.EdgeActivated -= OnEdgeActivated;
            _edge.EdgeDismissed -= OnEdgeDismissed;
            _edge.Stop();

            _sidebar.OpenStateChanged -= OnSidebarOpenStateChanged;

            // 应用退出：真正销毁侧边栏窗口，而不只是收起面板。
            _sidebar.Close();
            _theme.ThemeChanged -= OnThemeChanged;
            _viewModel.AppearanceChanged -= OnAppearanceChanged;
            _sortModeTimer.Stop();
            _sortModeTimer.Tick -= OnSortModeTimerTick;

            // 全局钩子必须摘掉，否则进程退出前会一直挂着。
            RemoveEscapeHook();

            SourceInitialized -= OnSourceInitialized;
            Loaded -= OnLoaded;
            Closed -= OnClosed;

            _viewModel.Dispose();
        }

        private void AppleButton_Click(object sender, RoutedEventArgs e) => _appleMenu.Toggle();

        /// <summary>点击右区组件：时钟→日历、天气→天气卡片、CPU/内存→详情弹窗。</summary>
        private void OnRightItemsClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe)
            {
                switch (fe.DataContext)
                {
                    case ClockWidgetViewModel:
                        _clockMenu.Toggle();
                        break;
                    case WeatherWidgetViewModel:
                        _weatherMenu.Toggle();
                        break;
                    case CpuWidgetViewModel:
                        _cpuDetailMenu.Toggle();
                        break;
                    case MemoryWidgetViewModel:
                        _memoryDetailMenu.Toggle();
                        break;
                }
            }
        }

        /// <summary>
        /// 点击中区歌词组件 → 弹出媒体控制浮窗。
        ///
        /// 只在歌词组件在场时响应：无播放时中区显示的是文本/一言，
        /// 那时点一下弹出一个「无播放」的空窗没有意义。
        /// </summary>
        private void OnCenterItemsClick(object sender, MouseButtonEventArgs e)
        {
            if (ResolveWidget(e.OriginalSource as DependencyObject) is LyricsWidgetViewModel)
            {
                _mediaControlMenu.Toggle();
            }
        }

        private void AudioButton_Click(object sender, RoutedEventArgs e) => _audioMenu.Toggle();

        /// <summary>
        /// 自定义弹窗定位：右边缘对齐 PlacementTarget 右边缘，顶部出现在其下方 6px。
        /// 解决 Placement="Bottom" 默认左对齐导致弹窗偏左的问题。
        /// </summary>
        private CustomPopupPlacement[] RightAlignPopupCallback(Size popupSize, Size targetSize, Point offset)
        {
            // 右对齐：x = 目标宽度 - 弹窗宽度；y = 目标高度 + 6px 间距
            double x = targetSize.Width - popupSize.Width;
            double y = targetSize.Height + 6;
            return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal)];
        }

        #region 拖动排序（按住左键 3 秒进入排序模式）

        private void HookSortGestures()
        {
            CenterItems.PreviewMouseLeftButtonDown += OnItemsPreviewMouseDown;
            CenterItems.PreviewMouseMove += OnItemsPreviewMouseMove;
            CenterItems.PreviewMouseLeftButtonUp += OnItemsPreviewMouseUp;
            RightItems.PreviewMouseLeftButtonDown += OnItemsPreviewMouseDown;
            RightItems.PreviewMouseMove += OnItemsPreviewMouseMove;
            RightItems.PreviewMouseLeftButtonUp += OnItemsPreviewMouseUp;
            RootGrid.PreviewMouseLeftButtonDown += OnRootMouseDown;

            /*
             * 这里不再挂 PreviewKeyDown。
             * 窗口带 WS_EX_NOACTIVATE，永远拿不到键盘焦点，
             * 所以 Esc 退出排序改用排序期间的低级键盘钩子（见 InstallEscapeHook）。
             */
        }

        private void OnItemsPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _pressOrigin = e.GetPosition(this);

            if (_isSortMode)
            {
                _dragWidget = ResolveWidget(e.OriginalSource as DependencyObject);
                return;
            }

            _sortModeTimer.Stop();
            _sortModeTimer.Start();
        }

        private void OnItemsPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _sortModeTimer.Stop();
                return;
            }

            Point current = e.GetPosition(this);

            if (!_isSortMode)
            {
                // 移动超过 4px 视为普通点击/拖选，取消进入排序模式。
                if (Math.Abs(current.X - _pressOrigin.X) > 4 || Math.Abs(current.Y - _pressOrigin.Y) > 4)
                {
                    _sortModeTimer.Stop();
                }
                return;
            }

            WidgetViewModelBase? target = ResolveWidget(e.OriginalSource as DependencyObject);
            if (_dragWidget is not null && target is not null && !ReferenceEquals(_dragWidget, target))
            {
                _viewModel.MoveWidget(_dragWidget, target);
                _dragWidget = target;
            }
        }

        private void OnItemsPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _sortModeTimer.Stop();
            if (_isSortMode)
            {
                _dragWidget = null;
                _viewModel.PersistWidgetOrder();
            }
        }

        private void OnSortModeTimerTick(object? sender, EventArgs e)
        {
            _sortModeTimer.Stop();
            _isSortMode = true;
            Cursor = Cursors.SizeWE;

            /*
             * 排序模式需要 Esc 退出，但窗口带 WS_EX_NOACTIVATE，收不到键盘，
             * PreviewKeyDown 不会触发。
             *
             * 不能靠 SetForegroundWindow 抢焦点：NOACTIVATE 窗口从未被激活过，
             * 系统的前台锁定规则会直接拒绝这个调用（实测 hwndActive 始终不变）。
             * 所以改为在排序期间临时挂一个低级键盘钩子，只监听 Esc，
             * 退出时立刻摘掉——既不需要焦点，也不影响用户其他按键。
             */
            InstallEscapeHook();
        }

        /// <summary>排序模式期间监听 Esc，不依赖窗口焦点。</summary>
        private void InstallEscapeHook()
        {
            if (_escapeHook != IntPtr.Zero)
            {
                return;
            }

            try
            {
                _escapeHookProc = OnEscapeHookProc;
                _escapeHook = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_KEYBOARD_LL,
                    _escapeHookProc,
                    IntPtr.Zero,
                    0);

                if (_escapeHook == IntPtr.Zero)
                {
                    AppLog.Error($"安装键盘钩子失败，Esc 将无法退出排序模式（error={Marshal.GetLastWin32Error()}）");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("安装键盘钩子异常", ex);
            }
        }

        private void RemoveEscapeHook()
        {
            if (_escapeHook == IntPtr.Zero)
            {
                return;
            }

            try
            {
                NativeMethods.UnhookWindowsHookEx(_escapeHook);
            }
            catch (Exception ex)
            {
                AppLog.Error("卸载键盘钩子异常", ex);
            }
            finally
            {
                _escapeHook = IntPtr.Zero;
                _escapeHookProc = null;
            }
        }

        private IntPtr OnEscapeHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            const int WM_KEYDOWN = 0x0100;
            const int VK_ESCAPE = 0x1B;

            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vk = Marshal.ReadInt32(lParam);
                if (vk == VK_ESCAPE && _isSortMode)
                {
                    // 钩子回调运行在安装它的线程（UI 线程）上，
                    // 但这里仍投递一次，避免在钩子内部改状态导致重入。
                    Dispatcher.BeginInvoke(() => ExitSortMode());
                    return (IntPtr)1;
                }
            }

            return NativeMethods.CallNextHookEx(_escapeHook, nCode, wParam, lParam);
        }

        private void OnRootMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isSortMode)
            {
                return;
            }

            // 点击空白处退出排序模式。
            if (ResolveWidget(e.OriginalSource as DependencyObject) is null)
            {
                ExitSortMode();
            }
        }

        private void ExitSortMode()
        {
            _isSortMode = false;
            _dragWidget = null;
            Cursor = Cursors.Arrow;

            // 退出排序立刻摘钩子，不做无谓的全局键盘监听。
            RemoveEscapeHook();
        }

        private static WidgetViewModelBase? ResolveWidget(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is FrameworkElement { DataContext: WidgetViewModelBase widget })
                {
                    return widget;
                }

                /*
                 * OriginalSource 可能是 Run 这类 ContentElement，对它们调用
                 * VisualTreeHelper.GetParent 会抛 InvalidOperationException。
                 * 先尝试 ContentOperations 拿到它所在的 FrameworkContentElement 的父级，
                 * 再退化成视觉树遍历。
                 */
                if (source is ContentElement contentElement)
                {
                    source = ContentOperations.GetParent(contentElement)
                        ?? (contentElement as FrameworkContentElement)?.Parent;
                    continue;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        #endregion
    }
}
