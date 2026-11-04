using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Services;

namespace MyTopBar.ViewModels
{
    public partial class AudioPopupViewModel : ObservableObject, IDisposable
    {
        private readonly AudioService _audioService;
        private bool _syncing;
        private bool _disposed;

        [ObservableProperty]
        private float _volume;

        [ObservableProperty]
        private bool _isMuted;

        public AudioPopupViewModel(AudioService audioService)
        {
            _audioService = audioService;
            Attach();
        }

        /// <summary>
        /// 订阅系统音量并同步一次当前值。
        ///
        /// Popup.StaysOpen=False 关闭时 Child 会被摘出视觉树触发 Unloaded，
        /// 再次打开又重新 Loaded。这里做成可重复调用，避免弹窗关过一次后
        /// 就再也收不到系统音量变化。
        /// </summary>
        public void Activate()
        {
            Attach();
        }

        /// <summary>弹窗卸载时退订。</summary>
        public void Deactivate()
        {
            _audioService.VolumeChanged -= OnSystemVolumeChanged;
        }

        private void Attach()
        {
            _audioService.VolumeChanged -= OnSystemVolumeChanged;
            _audioService.VolumeChanged += OnSystemVolumeChanged;

            _syncing = true;
            Volume = _audioService.GetVolume();
            IsMuted = _audioService.IsMuted();
            _syncing = false;
        }

        private void OnSystemVolumeChanged(float volume, bool muted)
        {
            _syncing = true;
            Volume = volume;
            IsMuted = muted;
            _syncing = false;
        }

        partial void OnVolumeChanged(float value)
        {
            if (_syncing)
            {
                return;
            }

            _audioService.SetVolume(value);
        }

        partial void OnIsMutedChanged(bool value)
        {
            if (_syncing)
            {
                return;
            }

            _audioService.SetMute(value);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Deactivate();
            _disposed = true;
        }
    }
}
