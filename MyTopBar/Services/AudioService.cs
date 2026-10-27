using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MyTopBar.Services
{
    /// <summary>
    /// 系统音量控制。
    ///
    /// 订阅两路通知：
    /// 1. AudioEndpointVolume.OnVolumeNotification —— 音量/静音变化
    /// 2. IMMNotificationClient.OnDefaultDeviceChanged —— 默认输出设备切换
    ///
    /// 第 2 点是必须的：插耳机、连蓝牙后默认设备换了，
    /// 旧设备上的音量通知不再触发，顶栏的数字会一直停在换设备前的值。
    /// </summary>
    public sealed class AudioService : IDisposable, IMMNotificationClient
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

            // 设备变化通知注册一次即可，跟随 App 生命周期。
            try
            {
                _deviceEnumerator.RegisterEndpointNotificationCallback(this);
            }
            catch
            {
                // 某些精简系统上回调注册会失败，退化为不支持自动切换。
            }

            InitDefaultDevice();
        }

        /// <summary>
        /// 绑定当前默认输出设备。设备切换后需要重新绑定，
        /// 否则后续读写都作用在已失效的旧设备上。
        /// </summary>
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

        /// <summary>释放旧设备并重新绑定到新的默认设备。</summary>
        private void RebindDefaultDevice()
        {
            if (_disposed)
            {
                return;
            }

            if (_defaultDevice is not null)
            {
                try
                {
                    _defaultDevice.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
                }
                catch
                {
                    // 设备已被移除时会抛，忽略。
                }

                try
                {
                    _defaultDevice.Dispose();
                }
                catch
                {
                    // 同上。
                }

                _defaultDevice = null;
            }

            InitDefaultDevice();

            // 立即把新设备的音量推给界面，避免顶栏停在旧值。
            RaiseVolumeChanged(GetVolume(), IsMuted());
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
            try
            {
                return _defaultDevice?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0f;
            }
            catch
            {
                return 0f;
            }
        }

        public void SetVolume(float volume)
        {
            InitDefaultDevice();
            if (_defaultDevice is null)
            {
                return;
            }

            try
            {
                _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
            }
            catch
            {
                // 设备刚被拔掉时写入会失败，忽略即可。
            }
        }

        public bool IsMuted()
        {
            InitDefaultDevice();
            try
            {
                return _defaultDevice?.AudioEndpointVolume.Mute ?? false;
            }
            catch
            {
                return false;
            }
        }

        public void SetMute(bool mute)
        {
            InitDefaultDevice();
            if (_defaultDevice is null)
            {
                return;
            }

            try
            {
                _defaultDevice.AudioEndpointVolume.Mute = mute;
            }
            catch
            {
                // 同 SetVolume。
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _deviceEnumerator.UnregisterEndpointNotificationCallback(this);
            }
            catch
            {
                // 忽略注销失败。
            }

            if (_defaultDevice is not null)
            {
                try
                {
                    _defaultDevice.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
                }
                catch
                {
                    // 忽略。
                }

                _defaultDevice.Dispose();
                _defaultDevice = null;
            }

            _deviceEnumerator.Dispose();
        }

        #region IMMNotificationClient

        /// <summary>
        /// 默认输出设备变化。这是修「换音频设备后音量失灵」的关键回调。
        ///
        /// 注意：回调在 COM 线程上触发，不在 UI 线程，所以只投递到 Dispatcher，
        /// 不在回调里直接做重绑定（涉及 COM 调用与事件解绑，容易死锁）。
        /// </summary>
        void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string deviceId)
        {
            if (_disposed || flow != DataFlow.Render)
            {
                return;
            }

            // 只关心多媒体用途的默认设备，与 InitDefaultDevice 的选择保持一致。
            if (role != Role.Multimedia && role != Role.Console)
            {
                return;
            }

            _dispatcher.BeginInvoke(RebindDefaultDevice);
        }

        void IMMNotificationClient.OnDeviceAdded(string deviceId) { }

        void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }

        void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) { }

        void IMMNotificationClient.OnPropertyValueChanged(string deviceId, PropertyKey key) { }

        #endregion
    }
}
