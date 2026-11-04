using System.Windows;
using System.Windows.Controls;
using MyTopBar.ViewModels;

namespace MyTopBar.Views
{
    /// <summary>
    /// CPU / 内存详情弹窗共用视图。
    /// 两个弹窗的数据源不同，不能走 DI 自动装同一个 ViewModel 类型，
    /// 改由 MainWindow 在构造后通过 <see cref="Attach"/> 注入对应实例。
    /// </summary>
    public partial class SystemDetailPopupView : UserControl
    {
        private SystemDetailViewModel? _viewModel;

        public SystemDetailPopupView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>注入该弹窗专属的 ViewModel（CPU 或内存）。</summary>
        public void Attach(SystemDetailViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = viewModel;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel?.Activate();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _viewModel?.Deactivate();
        }
    }
}
