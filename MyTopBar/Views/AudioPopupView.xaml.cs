using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.ViewModels;

namespace MyTopBar.Views
{
    public partial class AudioPopupView : UserControl
    {
        private readonly AudioPopupViewModel _viewModel;

        public AudioPopupView()
        {
            _viewModel = App.Current.Services.GetRequiredService<AudioPopupViewModel>();
            DataContext = _viewModel;
            InitializeComponent();

            /*
             * Popup.StaysOpen=False 关闭时会把 Child 摘出视觉树 → Unloaded，
             * 再次打开又重新 Loaded。音量订阅必须跟着 Loaded/Unloaded 成对进出，
             * 只在 Unloaded 里退订会导致弹窗关过一次后再也收不到系统音量变化。
             */
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Activate();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Deactivate();
        }
    }
}
