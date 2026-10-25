
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.Infrastructure.Common;
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

            _theme.Apply(_settings.Current.Appearance.Theme);
            ApplyMaterial(_settings.Current.Appearance.Material, _theme.IsDark, handle);
        }

        private void OnThemeChanged(object? sender, bool isDark)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            ApplyMaterial(_settings.Current.Appearance.Material, isDark, handle);
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

            // 实测：原生 Acrylic 深色底约 (84,84,84)，目标观感（用户截图）约 (137,137,145)。
            // 叠一层约 32% 的中性浅灰补亮度差，保留 DWM 模糊与壁纸透色。
            RootGrid.Background = isDark
                ? new SolidColorBrush(Color.FromArgb(0x52, 0xD0, 0xD0, 0xD2))
                : new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        }

        private void ApplyMaterial(MaterialKind kind, bool isDark, IntPtr handle)
        {
            MaterialType target = kind switch
            {
                MaterialKind.Mica => MaterialType.Mica,
                MaterialKind.Acrylic => MaterialType.Acrylic,
                MaterialKind.Blur => MaterialType.Acrylic,
                _ => MaterialType.None
            };

            WindowMaterialConfig.MaterialMode = target;
            WindowMaterialConfig.IsDarkMode = isDark;
            // Mica 走原生 SystemBackdrop（更接近系统观感）；Acrylic/Blur 走合成路径以透出壁纸。
            WindowMaterialConfig.UseWindowComposition = kind != MaterialKind.Mica;

            // 顶层透明窗口用 Composition 才能透出壁纸；alpha 控制浓度。
            WindowMaterialConfig.CompositonColor = isDark
                ? Color.FromArgb(0x40, 0x20, 0x20, 0x24)
                : Color.FromArgb(0x40, 0xF2, 0xF2, 0xF2);

            _viewModel.OnMaterialApplied(new MaterialApplyResult(kind, kind, false, "fluentwpfcore"));
        }

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
            _sortModeTimer.Stop();
            _sortModeTimer.Tick -= OnSortModeTimerTick;

            SourceInitialized -= OnSourceInitialized;
            Loaded -= OnLoaded;
            Closed -= OnClosed;

            _viewModel.Dispose();
        }

        private void AppleButton_Click(object sender, RoutedEventArgs e) => _appleMenu.Toggle();

        private void ClockButton_Click(object sender, RoutedEventArgs e) => _clockMenu.Toggle();

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
            PreviewKeyDown += OnWindowKeyDown;
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

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isSortMode)
            {
                ExitSortMode();
            }
        }

        private void ExitSortMode()
        {
            _isSortMode = false;
            _dragWidget = null;
            Cursor = Cursors.Arrow;
        }

        private static WidgetViewModelBase? ResolveWidget(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is FrameworkElement { DataContext: WidgetViewModelBase widget })
                {
                    return widget;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        #endregion
    }
}
