using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MyTopBar.Infrastructure.Common;

namespace MyTopBar.Services
{
    /// <summary>一条剪贴板记录。</summary>
    public sealed partial class ClipItem : ObservableObject
    {
        [ObservableProperty]
        private string _preview = string.Empty;

        /// <summary>文本内容。图片条目的文本为缩略图文件路径。</summary>
        public string Content { get; init; } = string.Empty;

        public bool IsImage { get; init; }

        /// <summary>图片条目的缩略图（已解码并冻结），文本条目为 null。</summary>
        public BitmapImage? Thumbnail { get; init; }

        public DateTime CapturedAt { get; init; } = DateTime.Now;

        /// <summary>显示用的时间文本。</summary>
        public string TimeText => CapturedAt.ToString("HH:mm");

        /// <summary>内容指纹，用于去重。</summary>
        public string Fingerprint { get; init; } = string.Empty;
    }

    /// <summary>
    /// 剪贴板历史。
    ///
    /// 监听方式：AddClipboardFormatListener 让系统在剪贴板变化时
    /// 给注册窗口发 WM_CLIPBOARDUPDATE，比轮询 Clipboard.GetText 便宜得多，
    /// 也不会因为别的进程占用剪贴板而抛异常。
    ///
    /// 存储：只把最近 N 条放内存，图片另存 PNG 到 %AppData%\MyTopBar\clips\。
    /// 不做跨会话持久化 —— 剪贴板历史属于敏感数据，落盘越少越好。
    /// </summary>
    public sealed class ClipboardHistoryService : ObservableObject, IDisposable
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        private readonly Dispatcher _dispatcher;
        private readonly string _imageDirectory;

        private IntPtr _handle;
        private HwndSource? _source;
        private HwndSourceHook? _hook;
        private bool _disposed;
        private bool _updating;

        /// <summary>最多保留条数，由配置决定。</summary>
        public int MaxItems { get; set; } = 30;

        /// <summary>最近的记录，最新在最前。</summary>
        public ObservableCollection<ClipItem> Items { get; } = new();

        public ClipboardHistoryService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _imageDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MyTopBar",
                "clips");
        }

        /// <summary>
        /// 挂上剪贴板监听。
        ///
        /// 需要真实 HWND：WM_CLIPBOARDUPDATE 只能投递到有窗口的进程。
        /// 这里创建一个 MessageOnly 的 HwndSource，不占屏幕、不进 Alt+Tab。
        /// </summary>
        public void Start()
        {
            if (_disposed || _source is not null)
            {
                return;
            }

            try
            {
                var parameters = new HwndSourceParameters("MyTopBar.ClipboardListener")
                {
                    Width = 0,
                    Height = 0,
                    WindowStyle = 0,
                    ExtendedWindowStyle = 0
                };

                _source = new HwndSource(parameters);
                _handle = _source.Handle;
                _hook = OnWindowMessage;
                _source.AddHook(_hook);

                if (!AddClipboardFormatListener(_handle))
                {
                    AppLog.Error("注册剪贴板监听失败");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("创建剪贴板监听窗口失败", ex);
            }
        }

        private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                _ = CaptureAsync();
                handled = true;
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// 抓取当前剪贴板内容。
        ///
        /// 加一点延迟：WM_CLIPBOARDUPDATE 在数据源刚写入时就到达，
        /// 有些应用（尤其是从浏览器复制图片）此时还没把数据完全准备好，
        /// 立刻读取会拿到空内容。
        /// </summary>
        private async Task CaptureAsync()
        {
            if (_disposed || _updating)
            {
                return;
            }

            _updating = true;

            try
            {
                await Task.Delay(120);

                // 回到 UI 线程再碰 Clipboard：WPF 的剪贴板封装要求 STA/UI 线程。
                await _dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        CaptureCore();
                    }
                    catch (Exception ex)
                    {
                        // 剪贴板被其他进程独占时 OpenClipboard 会失败，忽略即可。
                        AppLog.Error("读取剪贴板失败", ex);
                    }
                });
            }
            finally
            {
                _updating = false;
            }
        }

        private void CaptureCore()
        {
            if (Clipboard.ContainsImage())
            {
                CaptureImage();
                return;
            }

            if (Clipboard.ContainsText())
            {
                CaptureText(Clipboard.GetText());
            }
        }

        private void CaptureText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string fingerprint = Hash(text);
            if (Items.Count > 0 && Items[0].Fingerprint == fingerprint)
            {
                // 和最新一条相同，不重复插入。
                return;
            }

            Insert(new ClipItem
            {
                Content = text,
                Preview = BuildPreview(text),
                IsImage = false,
                Fingerprint = fingerprint
            });
        }

        private void CaptureImage()
        {
            BitmapSource? source = Clipboard.GetImage();
            if (source is null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_imageDirectory);

                // 缩略图：限制宽度，避免大截图在列表里撑爆内存。
                var thumb = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(
                    Math.Min(1.0, 240.0 / Math.Max(1, source.PixelWidth)),
                    Math.Min(1.0, 240.0 / Math.Max(1, source.PixelWidth))));

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(thumb));

                string name = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..6] + ".png";
                string path = Path.Combine(_imageDirectory, name);

                using (var fs = File.Create(path))
                {
                    encoder.Save(fs);
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();

                Insert(new ClipItem
                {
                    Content = path,
                    Preview = $"{source.PixelWidth} × {source.PixelHeight} 图片",
                    IsImage = true,
                    Thumbnail = bitmap,
                    Fingerprint = "img:" + Hash(path + source.PixelWidth + "x" + source.PixelHeight)
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("保存剪贴板图片失败", ex);
            }
        }

        private void Insert(ClipItem item)
        {
            Items.Insert(0, item);

            // 超出上限就从尾部丢弃；图片顺带删掉磁盘文件。
            while (Items.Count > Math.Max(1, MaxItems))
            {
                ClipItem last = Items[^1];
                Items.RemoveAt(Items.Count - 1);
                DeleteImageFile(last);
            }
        }

        /// <summary>清空历史。</summary>
        public void Clear()
        {
            foreach (ClipItem item in Items)
            {
                DeleteImageFile(item);
            }

            Items.Clear();
        }

        /// <summary>删除单条。</summary>
        public void Remove(ClipItem item)
        {
            DeleteImageFile(item);
            Items.Remove(item);
        }

        /// <summary>把某条写回剪贴板。</summary>
        public void CopyBack(ClipItem item)
        {
            try
            {
                if (item.IsImage)
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(item.Content);
                    bitmap.EndInit();
                    Clipboard.SetImage(bitmap);
                    return;
                }

                Clipboard.SetText(item.Content);
            }
            catch (Exception ex)
            {
                AppLog.Error("写回剪贴板失败", ex);
            }
        }

        private static void DeleteImageFile(ClipItem item)
        {
            if (!item.IsImage)
            {
                return;
            }

            try
            {
                if (File.Exists(item.Content))
                {
                    File.Delete(item.Content);
                }
            }
            catch
            {
                // 文件被占用时留到下次清理，不影响功能。
            }
        }

        /// <summary>把长文本压成一行预览。</summary>
        private static string BuildPreview(string text)
        {
            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            while (flat.Contains("  ", StringComparison.Ordinal))
            {
                flat = flat.Replace("  ", " ", StringComparison.Ordinal);
            }

            return flat.Length <= 80 ? flat : flat[..80] + "…";
        }

        private static string Hash(string value)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes)[..16];
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                if (_handle != IntPtr.Zero)
                {
                    RemoveClipboardFormatListener(_handle);
                }

                if (_source is not null && _hook is not null)
                {
                    _source.RemoveHook(_hook);
                }

                _source?.Dispose();
            }
            catch
            {
                // 退出阶段失败无所谓。
            }
            finally
            {
                _source = null;
                _handle = IntPtr.Zero;
            }
        }
    }
}
