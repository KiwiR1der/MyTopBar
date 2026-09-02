using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyTopBar.Models
{
    public sealed record ForegroundAppInfo(
        IntPtr WindowHandle,
        int ProcessId,
        string ProcessName,
        string DisplayName,
        string WindowTitle,
        string? ExecutablePath
        );
}
