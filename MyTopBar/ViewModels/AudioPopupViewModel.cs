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
            _syncing = true;
            Volume = _audioService.GetVolume();
            IsMuted = _audioService.IsMuted();
            _syncing = false;

            _audioService.VolumeChanged += OnSystemVolumeChanged;
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

            _audioService.VolumeChanged -= OnSystemVolumeChanged;
            _disposed = true;
        }
    }
}
