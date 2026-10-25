using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.ViewModels;

namespace MyTopBar.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _viewModel;

        /// <summary>任一设置项改动后触发，供顶栏即时应用。</summary>
        public event EventHandler? SettingsApplied;

        public SettingsWindow()
        {
            InitializeComponent();
            _viewModel = App.Current.Services.GetRequiredService<SettingsViewModel>();
            DataContext = _viewModel;
            _viewModel.SettingsApplied += (_, _) => SettingsApplied?.Invoke(this, EventArgs.Empty);
        }
    }
}
