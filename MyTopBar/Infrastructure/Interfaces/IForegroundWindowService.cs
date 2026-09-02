using MyTopBar.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MyTopBar.Infrastructure.Interfaces
{
    public interface IForegroundWindowService : IDisposable
    {
        ForegroundAppInfo? Current { get; }

        event EventHandler<ForegroundAppInfo?>? OnForegroundAppInfoChanged;

        void Start();
        void Stop();
    }
}
