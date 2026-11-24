using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.Infrastructure.Common;
using MyTopBar.Infrastructure.Interop;
using MyTopBar.Services;

namespace MyTopBar.Views
{
    /// <summary>
    /// 设置窗口。
    ///
    /// 和顶栏一样使用毛玻璃背景：这里不能设 AllowsTransparency="True"
    /// （那会让窗口变成分层窗口，DWM 模糊失效），
    /// 而是走 CompositionTarget.BackgroundColor + DwmExtendFrameIntoClientArea
    /// + SetWindowCompositionAttribute 这条路。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly ViewModels.SettingsViewModel _viewModel;
        private readonly ThemeService _theme;

        /// <summary>任一设置项改动后触发，供顶栏即时应用。</summary>
        public event EventHandler? SettingsApplied;

        public SettingsWindow()
        {
            InitializeComponent();

            _viewModel = App.Current.Services.GetRequiredService<ViewModels.SettingsViewModel>();
            _theme = App.Current.Services.GetRequiredService<ThemeService>();
            DataContext = _viewModel;
            /*
             * 用命名方法而不是 lambda：OnClosed 里要退订，lambda 无法 -=。
             * VM 是 transient，窗口关闭时必须 Dispose，否则它订阅的单例
             * LyricsWidgetViewModel 会一直持有它（每开一次窗口泄漏一个 VM）。
             */
            _viewModel.SettingsApplied += OnViewModelSettingsApplied;

            SyncWindowMaterial();
            _theme.BackdropChanged += OnBackdropChanged;

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnBackdropChanged(object? sender, EventArgs e)
            => SyncWindowMaterial();

        private void SyncWindowMaterial()
            => _theme.SyncWindowMaterial(windowMaterial, isBar: false);

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                /*
                 * 抢前台。
                 *
                 * 本窗口由顶栏菜单打开，而顶栏带 WS_EX_NOACTIVATE 从不激活，
                 * 本进程因此不被系统认作前台进程，Show() 内部的激活请求会被
                 * 前台锁定规则静默拒绝 —— 表现为窗口能显示但落在其他窗口后面。
                 * 这里在渲染完成后显式把窗口推到最前。
                 */
                BringToFront();
            }));
        }

        /// <summary>
        /// 把窗口带到前台。
        ///
        /// 直接 Window.Activate() 会被 Windows 前台锁定规则拒绝
        /// （本进程从不被认作前台进程），因此统一走 ForceForeground。
        /// 供 OnLoaded 与外部（菜单命令重复打开时）调用。
        /// </summary>
        public void BringToFront()
        {
            NativeMethods.ForceForeground(new WindowInteropHelper(this).Handle);

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Focus();
        }

        private void OnViewModelSettingsApplied(object? sender, EventArgs e)
            => SettingsApplied?.Invoke(this, EventArgs.Empty);

        private void OnClosed(object? sender, EventArgs e)
        {
            Loaded -= OnLoaded;
            Closed -= OnClosed;
            _theme.BackdropChanged -= OnBackdropChanged;

            _viewModel.SettingsApplied -= OnViewModelSettingsApplied;
            _viewModel.Dispose();
        }

        /// <summary>
        /// 城市搜索结果被选中时填入经纬度。
        ///
        /// ListBox 的 SelectionChanged 无法直接绑定命令，这里转发给 ViewModel，
        /// 保持"选中即应用坐标"的交互在代码里只有一处实现。
        /// </summary>
        private void OnCitySelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.ListBox list)
            {
                return;
            }

            if (list.SelectedItem is Widgets.GeoLocation location)
            {
                _viewModel.SelectCityCommand.Execute(location);
            }
        }
    }
}
