using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Models.Settings;
using MyTopBar.Services;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 中区文本组件：纯展示，数据由 <see cref="TextDataService"/> 提供。
    /// </summary>
    public sealed partial class TextWidgetViewModel : WidgetViewModelBase
    {
        private readonly TextDataService _data;

        [ObservableProperty]
        private string _text = "MyTopBar";

        [ObservableProperty]
        private string _source = string.Empty;

        public override string Id => "text";
        public override string DisplayName => "文本 / 一言";
        public override WidgetZone DefaultZone => WidgetZone.Center;

        public override double MinWidth => 80;

        public TextWidgetViewModel(TextDataService data)
        {
            _data = data;
            _data.DataChanged += OnDataChanged;
            _text = _data.CurrentText;
            _source = _data.CurrentSource;
        }

        public void Configure(TextWidgetMode mode, string staticText, int intervalMinutes)
        {
            _data.Configure(mode, staticText, intervalMinutes);
        }

        public void Start()
        {
            _data.Start();
        }

        private void OnDataChanged(string text, string source)
        {
            Text = text;
            Source = source;
        }

        protected override void DisposeCore()
        {
            _data.DataChanged -= OnDataChanged;
            _data.Stop();
        }
    }
}
