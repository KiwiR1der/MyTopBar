using CommunityToolkit.Mvvm.ComponentModel;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 中区时钟。纯展示，数据由 <see cref="ClockDataService"/> 提供。
    /// </summary>
    public sealed partial class ClockWidgetViewModel : WidgetViewModelBase
    {
        private readonly ClockDataService _data;

        [ObservableProperty]
        private string _clockText = string.Empty;

        public override string Id => "clock";

        public override string DisplayName => "时钟";

        public override double MinWidth => 60;

        public override WidgetZone DefaultZone => WidgetZone.Right;

        public bool ShowWeekday
        {
            get => _data.ShowWeekday;
            set => _data.ShowWeekday = value;
        }

        /// <summary>是否使用 24 小时制。</summary>
        public bool Use24Hour
        {
            get => _data.Use24Hour;
            set => _data.Use24Hour = value;
        }

        public ClockWidgetViewModel(ClockDataService data)
        {
            _data = data;
            _data.TextChanged += OnDataTextChanged;
            _clockText = _data.CurrentText;
            _data.Start();
        }

        private void OnDataTextChanged(string text) => ClockText = text;

        protected override void DisposeCore()
        {
            _data.TextChanged -= OnDataTextChanged;
            _data.Stop();
        }
    }
}
