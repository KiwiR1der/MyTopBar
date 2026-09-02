using Microsoft.Win32.SafeHandles;
using MyTopBar.Infrastructure.Interfaces;
using MyTopBar.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace MyTopBar.Services
{
    public class ForegroundWindowService : IForegroundWindowService
    {
        private const uint EventSystemForeground = 0x0003;
        private const uint WineventOutOfContent = 0x0000;
        private const uint WineventSkipOwnProcess = 0x0002;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private readonly Dispatcher _dispatcher;
        private readonly int _ownProcessId;

        private readonly WinEventDelegate _winEventDelegate;

        private IntPtr _eventHook;
        private int _updatePending;
        private bool _disposed;
        public ForegroundWindowService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _ownProcessId = Environment.ProcessId;
            _winEventDelegate = OnWinEvent;
        }


        public ForegroundAppInfo? Current { get; private set; }

        public event EventHandler<ForegroundAppInfo?>? OnForegroundAppInfoChanged;

        public void Start()
        {
            ThrowIfDisposed();
            if (_eventHook != IntPtr.Zero)
                return;

            _eventHook = SetWinEventHook(EventSystemForeground, EventSystemForeground,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                WineventOutOfContent | WineventSkipOwnProcess);
            if (_eventHook == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法安装前台窗口监视器");
            }

            // 主动刷新一次当前前台窗口信息，以便在启动时立即获取
            RequestRefresh();
        }

        public void Stop()
        {
            ThrowIfDisposed();
            if (_eventHook == IntPtr.Zero)
                return;

            UnhookWinEvent(_eventHook);
            _eventHook = IntPtr.Zero;
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (eventType != EventSystemForeground)
                return;
            if (hwnd == IntPtr.Zero)
                return;
            /*
             * WinEvent 回调不保证运行在 WPF UI 线程。
             * 而且不应该在原生回调中执行文件读取等耗时操作，
             * 因此这里只请求稍后刷新。
             */
            RequestRefresh();
        }

        private void RequestRefresh()
        {
            if (_disposed) return;

            // 合并短时间重复的窗口切换通知
            if (Interlocked.Exchange(ref _updatePending, 1) != 0)
                return;

            _dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() =>
                {
                    Interlocked.Exchange(ref _updatePending, 0);
                    if (!_disposed)
                        RefreshCurrentWindow();
                }));
        }

        private void RefreshCurrentWindow()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return;
            if (!IsWindow(hwnd))
                return;
            GetWindowThreadProcessId(hwnd, out uint processIdValue);
            if (processIdValue == 0)
                return;
            int processId = unchecked((int)processIdValue);
            /*
             * 忽略顶栏自身以及设置窗口
             * 
             * 用户点击媒体控制按钮或者打开窗口时，
             * 左侧仍然保留此前使用的外部应用名称
             */
            if (processId == _ownProcessId)
                return;
            ForegroundAppInfo? newValue = TryCreateAppInfo(hwnd, processId);
            if (newValue is null)
                return;
            Current = newValue;
            OnForegroundAppInfoChanged?.Invoke(this, newValue);
        }

        private static ForegroundAppInfo? TryCreateAppInfo(IntPtr hwnd, int processId)
        {
            try
            {
                string windowTitle = GetWindowTitle(hwnd);
                string? executablePath = TryGetExecutablePath(processId);
                string processName;
                try
                {
                    using Process process = Process.GetProcessById(processId);
                    processName = process.ProcessName;
                }
                catch
                {
                    processName = executablePath is not null
                        ? Path.GetFileNameWithoutExtension(executablePath)
                        : $"Process_{processId}";
                }

                string displayName = ResolveDisplayName(processName, executablePath, windowTitle);

                return new ForegroundAppInfo(
                WindowHandle: hwnd,
                ProcessId: processId,
                ProcessName: processName,
                DisplayName: displayName,
                WindowTitle: windowTitle,
                ExecutablePath: executablePath);
            }
            catch
            {
                /*
                 * 前台窗口可能在读取过程中关闭，
                 * 这是正常竞争条件，不应该导致整个顶栏崩溃。
                */
                return null;
            }
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            int length = GetWindowTextLength(hwnd);
            if (length <= 0)
                return string.Empty;
            StringBuilder sb = new StringBuilder(length + 1);
            int copied = GetWindowText(hwnd, sb, sb.Capacity);
            return copied > 0 ? sb.ToString() : string.Empty;
        }

        private static string? TryGetExecutablePath(int processId)
        {
            using SafeProcessHandle processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (processHandle.IsInvalid)
                return null;
            int capacity = 32768;
            var buffer = new StringBuilder(capacity);
            if (!QueryFullProcessImageName(processHandle, 0, buffer, ref capacity))
                return null;
            return buffer.ToString();
        }

        private static string ResolveDisplayName(string processName, string? executablePath, string windowTitle)
        {
            if (IsApplicationFrameHost(processName))
            {
                return ResolveFrameHostDisplayName(windowTitle);
            }

            if (KnownApplicationNames.TryGetValue(processName, out string? knownName))
                return knownName;

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                try
                {
                    FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                    /*
                     * FileDescription 通常比 ProductName 更适合展示。
                     * 
                     * 某些 Windows 程序的 ProductName 可能是 "Microsoft Windows" 或 "Windows"，而 FileDescription 则更具体。
                     */
                    if (IsUsefulDisplayName(versionInfo.FileDescription))
                        return versionInfo.FileDescription!.Trim();
                    if (IsUsefulDisplayName(versionInfo.ProductName))
                        return versionInfo.ProductName!.Trim();
                }
                catch
                {
                    // 文件可能无版本信息，或者当前用户无法读取   
                }
            }
            if (IsUsefulDisplayName(windowTitle))
                return windowTitle.Trim();
            if (!string.IsNullOrWhiteSpace(processName))
                return FormatProcessName(processName);
            return "未知应用";
        }

        private static string ResolveFrameHostDisplayName(string windowTitle)
        {
            if (IsUsefulDisplayName(windowTitle))
                return windowTitle.Trim();

            return "设置";
        }

        private static bool IsApplicationFrameHost(string processName)
        {
            return processName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUsefulDisplayName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim();
            return !normalized.Equals("Microsoft Windows Operating System", StringComparison.OrdinalIgnoreCase)
                && !normalized.Equals("Microsoft® Windows® Operating System", StringComparison.OrdinalIgnoreCase)
                && !normalized.Equals("Application Frame Host", StringComparison.OrdinalIgnoreCase)
                && !normalized.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return "未知应用";
            string result = processName.Trim();
            if (result.EndsWith(
                    ".exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                result = result[..^4];
            }
            return result;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Stop();
            Current = null;
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        //private static readonly Dictionary<string, string> KnownApplicationNames = new();

        private static readonly Dictionary<string, string>
        KnownApplicationNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["explorer"] = "文件资源管理器",
                ["ApplicationFrameHost"] = "设置",
                ["SystemSettings"] = "设置",
                ["chrome"] = "Google Chrome",
                ["msedge"] = "Microsoft Edge",
                ["firefox"] = "Mozilla Firefox",
                ["notepad"] = "记事本",
                ["windowsterminal"] = "Windows Terminal",
                ["powershell"] = "PowerShell",
                ["pwsh"] = "PowerShell",
                ["cmd"] = "命令提示符",
                ["taskmgr"] = "任务管理器",
                ["cloudmusic"] = "网易云音乐",
                ["qqmusic"] = "QQ音乐"
            };


        private delegate void WinEventDelegate(
            IntPtr winEventHook, uint eventType, IntPtr hwnd,
            int objectId, int childId,
            uint eventThread,
            uint eventTime);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr eventHookModule, WinEventDelegate winEventProc, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr winEventHook);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maximumCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint desireAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder executableName, ref int size);
    }
}
