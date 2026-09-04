using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace MyTopBar.Services
{
    public sealed class AudioService : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly MMDeviceEnumerator _deviceEnumerator;
        private MMDevice? _defaultDevice;
        private bool _disposed;

        public event Action<float, bool>? VolumeChanged;

        public AudioService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _deviceEnumerator = new MMDeviceEnumerator();
            InitDefaultDevice();
        }

        private void InitDefaultDevice()
        {
            if (_defaultDevice is not null || _disposed)
            {
                return;
            }

            try
            {
                _defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _defaultDevice.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
            }
            catch
            {
                _defaultDevice = null;
            }
        }

        private void OnVolumeNotification(AudioVolumeNotificationData data)
        {
            float volume = data.MasterVolume;
            bool muted = data.Muted;
            RaiseVolumeChanged(volume, muted);
        }

        private void RaiseVolumeChanged(float volume, bool muted)
        {
            if (_dispatcher.CheckAccess())
            {
                VolumeChanged?.Invoke(volume, muted);
                return;
            }

            _dispatcher.BeginInvoke(() => VolumeChanged?.Invoke(volume, muted));
        }

        public float GetVolume()
        {
            InitDefaultDevice();
            return _defaultDevice?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0f;
        }

        public void SetVolume(float volume)
        {
            InitDefaultDevice();
            if (_defaultDevice is null)
            {
                return;
            }

            _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
        }

        public bool IsMuted()
        {
            InitDefaultDevice();
            return _defaultDevice?.AudioEndpointVolume.Mute ?? false;
        }

        public void SetMute(bool mute)
        {
            InitDefaultDevice();
            if (_defaultDevice is null)
            {
                return;
            }

            _defaultDevice.AudioEndpointVolume.Mute = mute;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_defaultDevice is not null)
            {
                _defaultDevice.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
                _defaultDevice.Dispose();
                _defaultDevice = null;
            }

            _deviceEnumerator.Dispose();
            _disposed = true;
        }
    }
}
