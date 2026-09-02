using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MyTopBar.Services
{
    public class PowerService
    {
        public void OpenSettings(string uri)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }

        public void Sleep()
        {
            SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
        }

        public void Restart()
        {
            RunShutdown("/r /t 0");
        }

        public void Shutdown()
        {
            RunShutdown("/s /t 0");
        }

        private static void RunShutdown(string arguments)
        {
            Process.Start(new ProcessStartInfo("shutdown", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        [DllImport("powrprof.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
    }
}
