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
    /// 安全模型（重要）：
    /// - **插件是完全信任的代码**。Assembly.LoadFrom 会执行模块初始化器
    ///   （[ModuleInitializer]）与静态构造函数，并让插件运行在与宿主相同的
    ///   进程、相同的权限、相同的 AppDomain 里。放入 plugins/ 的任何 DLL
    ///   都等同于把它的代码直接编进宿主。
    ///   plugins/ 目录必须只由本机管理员可写；不要从网络下载的插件直接丢进去。
    /// - 旧注释声称「只加载类型，不执行任意代码」，那是错的，已更正。
    /// - 加载上下文：当前用默认 AssemblyLoadContext（LoadFrom），
    ///   所以插件与宿主共享依赖解析，插件自带的 CommunityToolkit.Mvvm 等
    ///   不同版本库会被绑定到已加载版本（可能 FileLoadException 或类型不匹配）。
    ///   要支持第三方生态时，应换成每个插件独立的可收集 AssemblyLoadContext
    ///   + AssemblyDependencyResolver 指向插件目录。
    ///
    /// 健壮性：
    /// - 失败粒度到「单个类型」而非「单个 DLL」：
    ///   一个类型解析失败不能拖垮同 DLL 里其他可用的插件。
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

            string[] dllPaths;
            try
            {
                dllPaths = Directory.GetFiles(pluginDir, "*.dll");
            }
            catch (Exception ex)
            {
                AppLog.Error($"枚举插件目录失败：{pluginDir}", ex);
                return plugins;
            }

            foreach (string dllPath in dllPaths)
            {
                LoadFromFile(dllPath, plugins);
            }

            return plugins;
        }

        /// <summary>
        /// 加载单个 DLL。分成「加载程序集 / 枚举类型 / 实例化插件」三层，
        /// 任何一层失败只影响当前这一层，其余插件继续加载。
        /// </summary>
        private static void LoadFromFile(string dllPath, List<IWidgetPlugin> plugins)
        {
            string fileName = Path.GetFileName(dllPath);
            Assembly assembly;

            try
            {
                assembly = Assembly.LoadFrom(dllPath);
            }
            catch (Exception ex)
            {
                AppLog.Error($"插件程序集加载失败：{fileName}", ex);
                return;
            }

            foreach (Type type in GetLoadableTypes(assembly, fileName))
            {
                if (!IsPluginCandidate(type))
                {
                    continue;
                }

                try
                {
                    if (Activator.CreateInstance(type) is IWidgetPlugin plugin)
                    {
                        plugins.Add(plugin);
                        AppLog.Info($"插件加载成功：{plugin.Id} ({plugin.DisplayName}) from {fileName}");
                    }
                }
                catch (Exception ex)
                {
                    // 单个插件构造失败（缺无参构造、静态构造抛异常、依赖缺失）不应影响其他插件。
                    AppLog.Error($"插件实例化失败：{type.FullName} from {fileName}", ex);
                }
            }
        }

        /// <summary>
        /// 取程序集里可加载的类型。
        ///
        /// GetTypes() 只要有一个类型解析失败（引用了缺失程序集）就整体抛
        /// ReflectionTypeLoadException，旧实现因此会让整个 DLL 的所有插件全部跳过。
        /// 这里改用 ReflectionOnly 语义的容错版本：能加载的照常返回，坏的单独记日志。
        /// </summary>
        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly, string fileName)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                Type?[] loaded = ex.Types;
                int failed = 0;
                foreach (Exception? loaderException in ex.LoaderExceptions)
                {
                    if (loaderException is not null)
                    {
                        failed++;
                        AppLog.Error($"插件类型解析失败：{fileName}", loaderException);
                    }
                }

                AppLog.Error($"插件 {fileName} 有 {failed} 个类型无法加载，其余类型继续尝试");

                return loaded
                    .Where(t => t is not null)
                    .Select(t => t!)
                    .ToList();
            }
            catch (Exception ex)
            {
                AppLog.Error($"枚举插件类型失败：{fileName}", ex);
                return [];
            }
        }

        /// <summary>
        /// 是否为可实例化的插件类型。
        /// 必须排除：
        /// - 抽象类
        /// - 接口（IWidgetPlugin 自身 IsAbstract 为 true，属于侥幸被挡住，
        ///   这里显式声明意图，避免以后换接口时漏掉）
        /// - 开放泛型定义（Activator.CreateInstance 会抛）
        /// - 无公共无参构造的类型（契约要求，但接口没有强制）
        /// </summary>
        private static bool IsPluginCandidate(Type type)
        {
            return !type.IsAbstract
                && !type.IsInterface
                && !type.ContainsGenericParameters
                && typeof(IWidgetPlugin).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null;
        }
    }
}
