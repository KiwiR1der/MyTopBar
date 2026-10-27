using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyTopBar.ViewModels;
using MyTopBar.Widgets;

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

        /// <summary>
        /// 城市搜索结果被选中时填入经纬度。
        ///
        /// ListBox 的 SelectionChanged 无法直接绑定命令，这里转发给 ViewModel，
        /// 保持"选中即应用坐标"的交互在代码里只有一处实现。
        /// </summary>
        private void OnCitySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox list)
            {
                return;
            }

            if (list.SelectedItem is GeoLocation location)
            {
                _viewModel.SelectCityCommand.Execute(location);
            }
        }
    }
}
