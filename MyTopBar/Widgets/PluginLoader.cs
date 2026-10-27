using System.IO;
using System.Reflection;
using MyTopBar.Infrastructure.Common;

namespace MyTopBar.Widgets
{
    /// <summary>
    /// 插件加载器：扫描 plugins/ 目录，加载实现 <see cref="IWidgetPlugin"/> 的第三方组件。
    ///
    /// 约定：
    /// - 插件 DLL 放在可执行文件旁的 plugins/ 子目录
    /// - 每个 DLL 可以包含多个 IWidgetPlugin 实现
    /// - 插件 ID 与内置组件冲突时，插件优先（允许覆盖内置行为）
    ///
    /// 安全说明：
    /// - 只加载 .dll，不执行任意代码（Assembly.LoadFrom 只加载类型）
    /// - 插件构造失败时跳过并记日志，不影响内置组件
    /// </summary>
    public static class PluginLoader
    {
        /// <summary>
        /// 扫描 plugins/ 目录，返回所有可用的插件实例。
        /// </summary>
        public static IReadOnlyList<IWidgetPlugin> LoadPlugins(string baseDirectory)
        {
            var plugins = new List<IWidgetPlugin>();
            string pluginDir = Path.Combine(baseDirectory, "plugins");

            if (!Directory.Exists(pluginDir))
            {
                return plugins;
            }

            foreach (string dllPath in Directory.EnumerateFiles(pluginDir, "*.dll"))
            {
                try
                {
                    Assembly assembly = Assembly.LoadFrom(dllPath);
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (type.IsAbstract || !typeof(IWidgetPlugin).IsAssignableFrom(type))
                        {
                            continue;
                        }

                        if (Activator.CreateInstance(type) is IWidgetPlugin plugin)
                        {
                            plugins.Add(plugin);
                            AppLog.Info($"插件加载成功：{plugin.Id} ({plugin.DisplayName}) from {Path.GetFileName(dllPath)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error($"插件加载失败：{Path.GetFileName(dllPath)}", ex);
                }
            }

            return plugins;
        }
    }
}
