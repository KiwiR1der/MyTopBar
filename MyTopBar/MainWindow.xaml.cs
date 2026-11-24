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
using MyTopBar.Services;
using MyTopBar.Models.Settings;
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

        /*
         * 7 个顶栏弹窗。全部是独立 Window（PopupWindow），
         * 位置在打开时按锚点元素一次算准，之后不再移动。
         */
        private readonly Views.PopupWindow _appleMenu;
        private readonly Views.PopupWindow _clockMenu;
        private readonly Views.PopupWindow _weatherMenu;
        private readonly Views.PopupWindow _cpuDetailMenu;
        private readonly Views.PopupWindow _memoryDetailMenu;
        private readonly Views.PopupWindow _audioMenu;
        private readonly Views.PopupWindow _mediaControlMenu;

        /// <summary>CPU / 内存详情视图。原先挂在 XAML 的 Popup 里，现在手动创建。</summary>
        private readonly Views.SystemDetailPopupView _cpuDetailView;
        private readonly Views.SystemDetailPopupView _memoryDetailView;


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

            double height = BarHeightRange.Clamp(_settings.Current.Appearance.BarHeight);
            Height = height;

            _appBarController = new AppBarController(this, AppBarController.AppBarEdge.Top, height);
            _appBarController.FullScreenAppChanged += OnFullScreenAppChanged;

            /*
             * 创建 7 个弹窗窗口。
             *
             * 参数顺序：内容控件、内边距、圆角、最小宽度 —— 与原 XAML 里
             * 每个 Popup 的 Border 设置逐项对应，保证视觉与改造前一致。
             */
            _appleMenu = new Views.PopupWindow(
                new Views.AppleMenuView { DataContext = _viewModel },
                new Thickness(6, 4, 6, 4), cornerRadius: 10, minWidth: 220);

            _clockMenu = new Views.PopupWindow(
                new Views.CalendarPopup(),
                new Thickness(10, 8, 10, 8), cornerRadius: 10, minWidth: 248);

            _weatherMenu = new Views.PopupWindow(
                new Views.WeatherPopupView(),
                new Thickness(14, 12, 14, 12), cornerRadius: 14, minWidth: 280);

            /*
             * 右区四个弹窗统一右对齐到「显示器右边 - 20」。
             *
             * 20 = 顶栏外层 Grid 的水平 Margin(12) + 音量按钮的 Padding(8)，
             * 这样弹窗右边缘正好对齐顶栏最右侧可见文字的右端。
             *
             * 不用 RightPanel 当基准：它在 SharedSizeGroup 列里，
             * 宽度会随中区内容（歌词/一言切换）变化，
             * 结果同一个弹窗每次打开位置都不一样。
             */
            const double BarSideMargin = 20;

            _clockMenu.FixedRightMargin = BarSideMargin;
            _weatherMenu.FixedRightMargin = BarSideMargin;

            _audioMenu = new Views.PopupWindow(
                new Views.AudioPopupView(),
                new Thickness(10, 8, 10, 8), cornerRadius: 10, minWidth: 248);

            _mediaControlMenu = new Views.PopupWindow(
                new Views.MediaControlPopupView(),
                new Thickness(14, 12, 14, 12), cornerRadius: 12, minWidth: 280);

            // CPU / 内存共用同一个视图类型，但各需一份独立的 ViewModel（数据源不同）。
            var metrics = App.Current.Services.GetRequiredService<SystemMetricsService>();
            _cpuDetailView = new Views.SystemDetailPopupView();
            _memoryDetailView = new Views.SystemDetailPopupView();
            _cpuDetailView.Attach(new SystemDetailViewModel(metrics, Dispatcher, SystemDetailViewModel.DetailKind.Cpu));
            _memoryDetailView.Attach(new SystemDetailViewModel(metrics, Dispatcher, SystemDetailViewModel.DetailKind.Memory));

            _cpuDetailMenu = new Views.PopupWindow(
                _cpuDetailView,
                new Thickness(14, 12, 14, 12), cornerRadius: 12, minWidth: 260);

            _memoryDetailMenu = new Views.PopupWindow(
                _memoryDetailView,
                new Thickness(14, 12, 14, 12), cornerRadius: 12, minWidth: 260);

            _cpuDetailMenu.FixedRightMargin = BarSideMargin;
            _memoryDetailMenu.FixedRightMargin = BarSideMargin;
            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closed += OnClosed;

            _theme.BackdropChanged += OnBackdropChanged;

            _viewModel.SettingsRequested += OnSettingsRequested;
            _viewModel.AppearanceChanged += OnAppearanceChanged;

            // 长按左键 3 秒进入组件排序模式
            _sortModeTimer.Interval = TimeSpan.FromSeconds(3);
            _sortModeTimer.Tick += OnSortModeTimerTick;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;

            /*
             * 顶栏不能抢走前台窗口的焦点。
             * macOS 的菜单栏点一下就点一下，用户的编辑器不会失焦；
             * 这里靠 WS_EX_NOACTIVATE + ShowActivated="False" 达到同样效果。
             *
             * 顺带加 WS_EX_TOOLWINDOW，避免在 Alt+Tab 里出现一条 30px 的窗口。
             */
            NativeMethods.MakeNonActivating(handle);

            AppearanceSettings appearance = _settings.Current.Appearance;
            _theme.Apply(appearance.Theme, appearance.Translucency);
            SyncWindowMaterial();
        }

        private void OnBackdropChanged(object? sender, EventArgs e)
            => SyncWindowMaterial();

        /// <summary>同步顶栏的亚克力明暗与通透感。</summary>
        private void SyncWindowMaterial()
            => _theme.SyncWindowMaterial(windowMaterial, isBar: true);

        /// <summary>设置窗口改动后立即应用：高度、组件启停。</summary>
        private void OnAppearanceChanged(object? sender, EventArgs e)
        {
            double height = BarHeightRange.Clamp(_settings.Current.Appearance.BarHeight);
            if (Math.Abs(Height - height) > 0.5)
            {
                Height = height;
                _appBarController.SetThickness(height);
            }

        }

        /// <summary>已打开的自用设置窗口，避免重复打开。</summary>
        private Views.SettingsWindow? _settingsWindow;

        /// <summary>
        /// 打开（或提到最前）MyTopBar 设置窗口。
        ///
        /// 窗口生命周期归 View 层：ViewModel 只发 <see cref="MainViewModel.SettingsRequested"/>，
        /// 不再自己 new Window / Show —— 否则 VM 依赖 WPF 窗口类型，无法单元测试。
        /// </summary>
        private void OnSettingsRequested(object? sender, EventArgs e)
        {
            if (_settingsWindow is { IsLoaded: true })
            {
                /*
                 * 同样受前台锁限制：普通 Activate() 会被系统静默拒绝
                 * （本进程因顶栏的 WS_EX_NOACTIVATE 从不被认作前台进程），
                 * 需要走 ForceForeground 才能真正把已有窗口提到最前。
                 */
                _settingsWindow.BringToFront();
                return;
            }

            var window = new Views.SettingsWindow();
            window.SettingsApplied += OnBarSettingsApplied;
            window.Closed += OnSettingsWindowClosed;
            _settingsWindow = window;
            window.Show();
        }

        /// <summary>设置窗口里任一改动生效后，让顶栏立即重设高度 / 重建组件。</summary>
        private void OnBarSettingsApplied(object? sender, EventArgs e)
            => _viewModel.ApplySettingsChange();

        private void OnSettingsWindowClosed(object? sender, EventArgs e)
        {
            if (_settingsWindow is null)
            {
                return;
            }

            _settingsWindow.SettingsApplied -= OnBarSettingsApplied;
            _settingsWindow.Closed -= OnSettingsWindowClosed;
            _settingsWindow = null;
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
                _appleMenu.Collapse();
                _clockMenu.Collapse();
                _weatherMenu.Collapse();
                _audioMenu.Collapse();
                _mediaControlMenu.Collapse();
                _cpuDetailMenu.Collapse();
                _memoryDetailMenu.Collapse();
            }

            Visibility = isFullScreen ? Visibility.Hidden : Visibility.Visible;

            AppLog.Info(isFullScreen ? "检测到全屏应用，隐藏顶栏" : "全屏应用退出，恢复顶栏");
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Start();
            HookSortGestures();

            AppearanceSettings appearance = _settings.Current.Appearance;
            _theme.Apply(appearance.Theme, appearance.Translucency);
            SyncWindowMaterial();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _appBarController.FullScreenAppChanged -= OnFullScreenAppChanged;

            _theme.BackdropChanged -= OnBackdropChanged;
            _viewModel.AppearanceChanged -= OnAppearanceChanged;
            _viewModel.SettingsRequested -= OnSettingsRequested;
            _sortModeTimer.Stop();
            _sortModeTimer.Tick -= OnSortModeTimerTick;

            // 全局钩子必须摘掉，否则进程退出前会一直挂着。
            RemoveEscapeHook();

            SourceInitialized -= OnSourceInitialized;
            Loaded -= OnLoaded;
            Closed -= OnClosed;

            _viewModel.Dispose();
        }

        private void AppleButton_Click(object sender, RoutedEventArgs e)
            => _appleMenu.ToggleAround(AppleButton, Views.PopupAlignment.BottomLeft);

        /// <summary>点击右区组件：时钟→日历、天气→天气卡片、CPU/内存→详情弹窗。</summary>
        private void OnRightItemsClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not FrameworkElement fe)
            {
                return;
            }

            /*
             * 右区四个弹窗都用 PopupAlignment.BottomRight，但**真正的水平基准不是 RightPanel**：
             * 这四者都设了 FixedRightMargin（见构造函数），PopupWindow 会忽略锚点宽度，
             * 直接按「显示器右边缘 - BarSideMargin」对齐。
             *
             * RightPanel 只提供垂直锚点（弹窗开在顶栏下沿）。
             * 之所以不拿 RightPanel 当水平基准：它在 SharedSizeGroup 列里，
             * 宽度会随中区内容（歌词 / 一言切换）变化，同一个弹窗每次打开位置都会变。
             */
            switch (fe.DataContext)
            {
                case ClockWidgetViewModel:
                    _clockMenu.ToggleAround(RightPanel, Views.PopupAlignment.BottomRight);
                    break;
                case WeatherWidgetViewModel:
                    _weatherMenu.ToggleAround(RightPanel, Views.PopupAlignment.BottomRight);
                    break;
                case CpuWidgetViewModel:
                    _cpuDetailMenu.ToggleAround(RightPanel, Views.PopupAlignment.BottomRight);
                    break;
                case MemoryWidgetViewModel:
                    _memoryDetailMenu.ToggleAround(RightPanel, Views.PopupAlignment.BottomRight);
                    break;
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
                _mediaControlMenu.ToggleAround(CenterPanel, Views.PopupAlignment.BottomCenter);
            }
        }

        private void AudioButton_Click(object sender, RoutedEventArgs e)
            => _audioMenu.ToggleAround(AudioButton, Views.PopupAlignment.BottomLeft);


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
