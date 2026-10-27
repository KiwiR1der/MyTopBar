# MyTopBar

一个 Windows 上的 macOS 风格顶栏。通过 Shell 的 AppBar 协议真正占用桌面工作区，所有最大化窗口都会从顶栏下边缘开始。

![platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![license](https://img.shields.io/badge/license-MIT-green)

## 特性

- **工作区占位**：注册为 AppBar，不是普通悬浮窗，最大化窗口不会盖住顶栏
- **多材质**：Mica / Acrylic / 纯色，可在运行时切换，支持明暗模式
- **可插拔组件**：中区与右区各自独立装配，可启停、可拖动排序
- **配置持久化**：自动保存到 `%AppData%\MyTopBar\settings.json`
- **设置窗口**：改完即时生效，无需重启

## 布局

<img width="1920" height="28" alt="image" src="https://github.com/user-attachments/assets/1a0e773e-ced0-451f-87f6-8b06a6602870" />

## 组件

| 组件 | 区域 | 数据来源 | 说明 |
|---|---|---|---|
| 时钟 | 中 | 本地时间 | 对齐到整分刷新；点击弹出日历 |
| 歌词 | 中 | SMTC + [LRCLIB](https://lrclib.net) | 任意支持 SMTC 的播放器；带时间轴滚动 |
| 天气 | 中 | 和风天气 | 需要自备 API Key，未配置时显示占位 |
| 文本 | 中 | 静态文本 / [一言](https://hitokoto.cn) | 可切换模式，失败时保留上次内容 |
| CPU | 右 | `GetSystemTimes` | 采样差值计算使用率 |
| 内存 | 右 | `GlobalMemoryStatusEx` | 显示已用/总量 |
| 网络 | 右 | `NetworkInterface` | 上下行实时速率 |

系统指标由单一定时器统一采样，供右区组件共享，不会为每个组件单独开计时器。

## 交互

- **点击 Apple 图标** → 系统菜单（关于本机 / 系统设置 / 睡眠 / 重启 / 关机 / 退出）
- **点击时钟** → 日历弹窗
- **点击音量** → 音量滑块
- **长按任意组件 3 秒** → 进入排序模式，左右拖动即可换位，松手自动保存
- **Esc 或点击空白处** → 退出排序模式

## 系统要求

- Windows 10 1809 (build 17763) 或更高
- Windows 11 22H2 (build 22621) 及以上才能使用原生 Mica / Acrylic
- .NET 8 SDK（仅编译需要）

## 构建与运行

```bash
git clone https://github.com/KiwiR1der/MyTopBar.git
cd MyTopBar
dotnet build
dotnet run --project MyTopBar
```

构建产物位于 `MyTopBar/bin/Debug/net8.0-windows10.0.19041.0/MyTopBar.exe`。

## 配置

首次启动会在 `%AppData%\MyTopBar\settings.json` 生成默认配置：

```jsonc
{
  "appearance": {
    "material": 3,      // 0=None 1=MicaAlt 2=Acrylic 3=Mica
    "theme": 1,         // 1=浅色 2=深色
    "barHeight": 30     // 28-40
  },
  "widgets": [
    { "id": "clock", "enabled": true, "order": 0, "options": {} }
  ],
  "api": {
    "qWeatherKey": "",
    "hitokoto": { "enabled": true, "intervalMinutes": 30 }
  },
  "metrics": { "intervalMs": 1000, "useIecUnits": true }
}
```

也可以点 Apple 菜单里的「MyTopBar 设置…」用图形界面调整。

### 天气 API Key

前往[和风天气开发平台](https://dev.qweather.com/)申请免费 Key，填入设置窗口即可。天气组件的默认城市代码为 `101010100`（北京），可在配置的 `options` 中修改。

## 技术要点

### 工作区占位

`AppBarController` 通过 `SHAppBarMessage`（`ABM_NEW` / `ABM_QUERYPOS` / `ABM_SETPOS`）向 Shell 注册应用栏。同时处理了：

- `ABN_POSCHANGED` 通知后重新定位
- `TaskbarCreated` 广播（Explorer 重启后重新注册）
- `WM_DPICHANGED` / `WM_DISPLAYCHANGE` 时按新 DPI 重算厚度

### 材质实现

材质交给 [FluentWpfCore](https://github.com/TwilightLemon/FluentWpfCore) 处理，通过附加属性驱动：

```xml
<fluent:WindowMaterial.Material>
    <fluent:WindowMaterial x:Name="WindowMaterialConfig"
                           CompositonColor="#4DF2F2F2"
                           MaterialMode="Acrylic"
                           UseWindowComposition="True" />
</fluent:WindowMaterial.Material>
```

**关键点**：WPF 窗口要让 DWM 材质透出来，必须同时满足

1. `AllowsTransparency="False"`（否则退化为 layered window，材质失效且 ClearType 变灰）
2. `Background="Transparent"`
3. 在窗口渲染后将 `CompositionTarget.BackgroundColor` 设为透明
4. 客户区扩展到整个窗口

任一条缺失，屏幕上就会是一整条纯黑。

### 前台窗口追踪

`ForegroundWindowService` 用 `SetWinEventHook` 监听 `EVENT_SYSTEM_FOREGROUND`。因为原生回调不保证在 UI 线程，且不应在其中执行耗时操作，所以只做「标记待刷新」，实际读取合并后投递到 Dispatcher 执行。

应用名按优先级解析：已知应用映射表 → 可执行文件的 `FileDescription` / `ProductName` → 窗口标题 → 进程名。

## 已知限制

- **Blur 材质**：Windows 11 已废弃 `ACCENT_ENABLE_BLURBEHIND`，会回退到 Acrylic
- **Mica 与 Acrylic 观感不同**：Mica 走原生 SystemBackdrop，颜色均匀、不透壁纸；Acrylic 走合成路径，会透出壁纸
- **歌词匹配**：依赖 LRCLIB 的曲库。纯音乐、小众本地曲目可能匹配不到；只有纯文本歌词时显示首行，无时间轴不滚动
- **单显示器**：顶栏跟随窗口所在屏幕，未做多屏独立配置
- **未实现开机自启**

## 项目结构

```
MyTopBar/
├── Infrastructure/
│   ├── Common/      AppBarController（工作区占位）、PopupHost
│   ├── Interop/     Win32 P/Invoke 集中声明
│   ├── Material/    材质枚举
│   └── Interfaces/  服务契约
├── Models/          数据模型与配置结构
├── Services/        前台窗口、音频、电源、系统指标、主题、配置、媒体会话
├── ViewModels/      主视图模型、弹窗视图模型、设置视图模型
├── Views/           日历、音量、设置窗口
├── Widgets/         各组件视图模型 + 注册表
└── Styles/          主题与组件样式
```

## 依赖

| 包 | 用途 |
|---|---|
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | `[ObservableProperty]` / `[RelayCommand]` 源生成器 |
| [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) | 依赖注入 |
| [NAudio](https://github.com/naudio/NAudio) | CoreAudio 音量控制 |
| [FluentWpfCore](https://github.com/TwilightLemon/FluentWpfCore) | 窗口材质 |

字体使用 HarmonyOS Sans SC。

## 许可证

[MIT](LICENSE.txt)
