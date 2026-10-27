using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.Infrastructure.Common;
using MyTopBar.Infrastructure.Interop;
using MyTopBar.Infrastructure.Material;
using FluentWpfCore.AttachedProperties;
using FluentWpfCore.Interop;
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
        private readonly PopupHost _audioMenu;

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
            _appleMenu = new PopupHost(AppleMenuPopup);
            _clockMenu = new PopupHost(ClockPopup);
            _audioMenu = new PopupHost(AudioPopup);

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
            ApplyMaterial(_settings.Current.Appearance.Material, _theme.IsDark, handle);
        }

        private void OnThemeChanged(object? sender, bool isDark)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            ApplyMaterial(_settings.Current.Appearance.Material, isDark, handle);

            // 材质跟着明暗变了，根元素那层补色也必须同步重算，
            // 否则深色下会残留浅色主题的白色半透明叠层。
            ApplyRootBackground(_settings.Current.Appearance.Material, isDark);
        }

        /// <summary>设置窗口改动后立即应用：材质、高度、组件启停。</summary>
        private void OnAppearanceChanged(object? sender, EventArgs e)
        {
            double height = Math.Clamp(_settings.Current.Appearance.BarHeight, 28, 40);
            if (Math.Abs(Height - height) > 0.5)
            {
                Height = height;
                _appBarController.SetThickness(height);
            }

            ApplyMaterial(
                _settings.Current.Appearance.Material,
                _theme.IsDark,
                new WindowInteropHelper(this).Handle);

            ApplyRootBackground(_settings.Current.Appearance.Material, _theme.IsDark);
        }

        /// <summary>
        /// None 材质需要 WPF 自己铺不透明底色（否则透明合成会露黑）；
        /// 其余材质必须让根元素保持透明，DWM 材质才能透出来。
        /// </summary>
        private void ApplyRootBackground(MaterialKind kind, bool isDark)
        {
            if (kind == MaterialKind.None)
            {
                RootGrid.Background = new SolidColorBrush(isDark
                    ? Color.FromRgb(0x20, 0x20, 0x20)
                    : Color.FromRgb(0xF2, 0xF2, 0xF2));
                return;
            }

            /*
             * 材质由 DWM 自己绘制，这一层必须保持完全透明，
             * 否则会把材质盖住——这正是「切了材质看不出区别」的另一半原因。
             *
             * 早期为了让原生 Acrylic 的观感更接近截图，这里叠过一层 32% 的浅灰
             * （#52D0D0D2）。实测表明它会把深色下的白字压灰，而且和
             * CompositonColor / IsDarkMode 叠加后色调不可控，已移除。
             * 需要调整顶栏浓淡时，应该改 ApplyMaterial 里的 CompositonColor（合成路径）
             * 或材料本身的 IsDarkMode，而不是在根元素上再糊一层。
             */
            RootGrid.Background = Brushes.Transparent;
        }

        /// <summary>
        /// 应用窗口材质。
        ///
        /// 关键决策：在 Windows 11 上**不要**让 FluentWpfCore 走合成路径
        /// （<c>UseWindowComposition</c> 恒为 false），只切 <c>MaterialMode</c>。
        ///
        /// 原因（实测验证，见 REVIEW.md「材质实测」）：库的 Apply() 有两条分支，
        /// 走合成路径时只调 SetWindowCompositionAttribute，**不会**复位
        /// DWMWA_SYSTEMBACKDROP_TYPE。于是 Mica -> Acrylic 之后 DWM 仍在按 Mica
        /// 渲染，界面上毫无变化，必须重启（新窗口没有历史状态）才看得到。
        ///
        /// 而始终走系统背景路径时，Apply() 每次都完整地把
        /// MaterialMode 写进 DWMWA_SYSTEMBACKDROP_TYPE，切换精确且可重复：
        ///   Mica=2 / Acrylic=3 / MicaAlt=4 / None=1
        ///
        /// 只有 Windows 10（没有 SystemBackdrop API）才必须退回合成路径。
        /// </summary>
        private void ApplyMaterial(MaterialKind kind, bool isDark, IntPtr handle)
        {
            MaterialType target = kind switch
            {
                MaterialKind.Mica => MaterialType.Mica,
                MaterialKind.MicaAlt => MaterialType.MicaAlt,
                MaterialKind.Acrylic => MaterialType.Acrylic,
                // 旧配置里的 Blur 在 Win11 上等同 Acrylic（合成 API 只有 Acrylic 一种）。
                MaterialKind.Blur => MaterialType.Acrylic,
                _ => MaterialType.None
            };

            // Win11 有能力用原生 SystemBackdrop；Win10 只能走旧的合成 API。
            bool useComposition = !SupportsSystemBackdrop;

            /*
             * 这些都是 DependencyProperty，每次赋值都会立即触发 Apply()。
             * 必须先定下"走哪条路径"，最后再设材质本身，
             * 否则 Apply() 会先用旧路径跑一次再被纠正。
             */
            WindowMaterialConfig.IsDarkMode = isDark;

            // 合成路径下用它给 Acrylic 着色；系统背景路径不使用这个值。
            WindowMaterialConfig.CompositonColor = isDark
                ? Color.FromArgb(0x40, 0x20, 0x20, 0x24)
                : Color.FromArgb(0x40, 0xF2, 0xF2, 0xF2);

            WindowMaterialConfig.UseWindowComposition = useComposition;
            WindowMaterialConfig.MaterialMode = target;

            _viewModel.OnMaterialApplied(new MaterialApplyResult(kind, kind, false, "fluentwpfcore"));
        }

        /// <summary>
        /// 当前系统是否支持 DWMWA_SYSTEMBACKDROP_TYPE（Windows 11 22H2 / build 22621 起）。
        /// 不支持时必须退回 SetWindowCompositionAttribute 的旧路径。
        /// </summary>
        private static bool SupportsSystemBackdrop =>
            Environment.OSVersion.Version.Build >= 22621;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Start();
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

            ApplyMaterial(_settings.Current.Appearance.Material, _theme.IsDark, handle);
            ApplyRootBackground(_settings.Current.Appearance.Material, _theme.IsDark);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
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

        /// <summary>点击右区时钟区域弹出日历；点到其他组件则忽略。</summary>
        private void OnRightItemsClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement fe && fe.DataContext is ClockWidgetViewModel)
            {
                _clockMenu.Toggle();
            }
        }

        private void AudioButton_Click(object sender, RoutedEventArgs e) => _audioMenu.Toggle();

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
            if (_escapeHook == IntPtr.Zero)
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
