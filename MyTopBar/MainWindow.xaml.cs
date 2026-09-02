using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.Infrastructure.Common;
using MyTopBar.ViewModels;

namespace MyTopBar
{
    public partial class MainWindow : Window
    {
        private readonly AppBarController _appBarController;
        private readonly MainViewModel _viewModel;
        private readonly PopupHost _appleMenu;
        private readonly PopupHost _clockMenu;

        public MainWindow()
        {
            _viewModel = App.Current.Services.GetRequiredService<MainViewModel>();
            DataContext = _viewModel;

            InitializeComponent();

            _appBarController = new AppBarController(this, AppBarController.AppBarEdge.Top, 28);
            _appleMenu = new PopupHost(AppleMenuPopup);
            _clockMenu = new PopupHost(ClockPopup);

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Start();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            Loaded -= OnLoaded;
            Closed -= OnClosed;
            _viewModel.Dispose();
        }

        private void AppleButton_Click(object sender, RoutedEventArgs e)
        {
            _appleMenu.Toggle();
        }

        private void ClockButton_Click(object sender, RoutedEventArgs e)
        {
            _clockMenu.Toggle();
        }
    }
}
