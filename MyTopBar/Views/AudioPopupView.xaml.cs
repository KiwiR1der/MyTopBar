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
            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= OnUnloaded;
            _viewModel.Dispose();
        }
    }
}
