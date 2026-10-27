# MyTopBar

一个 Windows 上的 macOS 风格顶栏。通过 Shell 的 AppBar 协议真正占用桌面工作区，所有最大化窗口都会从顶栏下边缘开始。

![platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![license](https://img.shields.io/badge/license-MIT-green)

## 特性

- **工作区占位**：注册为 AppBar，不是普通悬浮窗，最大化窗口不会盖住顶栏
- **多材质**：Mica / Mica Alt / Acrylic / 纯色，可在运行时切换，支持明暗模式
- **可插拔组件**：中区与右区各自独立装配，可启停、可拖动排序
- **不抢焦点**：点击顶栏不会让当前窗口失焦，`WS_EX_NOACTIVATE` 实现
- **配置持久化**：自动保存到 `%AppData%\MyTopBar\settings.json`
- **设置窗口**：外观与组件改动即时生效，无需重启；天气参数点「应用并测试」后生效

## 布局

<img width="1920" height="30" alt="image" src="https://github.com/user-attachments/assets/e7998b9b-2f9f-4d42-8976-2c75988916d6" />


左右两列使用 `Auto` + `SharedSizeGroup` 自适应内容宽度（按配置最多约 490px），
中区始终在屏幕正中。若两列宽度不等，中区会偏到一侧，所以两列被强制成同宽。

## 组件

| 组件 | 区域 | 数据来源 | 说明 |
|---|---|---|---|
| 歌词 | 中 | SMTC + 网易云 / [LRCLIB](https://lrclib.net) | 有播放时显示，与文本组件互斥 |
| 文本 | 中 | 静态文本 / [一言](https://hitokoto.cn) | 无播放时显示，失败保留上次内容 |
| 网络 | 右 | `NetworkInterface` | 上下行实时速率 |
| CPU | 右 | `GetSystemTimes` | 采样差值计算使用率 |
| 内存 | 右 | `GlobalMemoryStatusEx` | 显示已用/总量 |
| 天气 | 右 | 和风天气 GeoAPI + 实时天气 | 支持按城市名搜索，见下方配置 |
| 时钟 | 右 | 本地时间 | 对齐到整分刷新；点击弹出日历 |

系统指标由单一定时器统一采样，供右区组件共享，不会为每个组件单独开计时器。

## 交互

- **点击 Apple 图标** → 系统菜单（关于本机 / 系统设置 / 睡眠 / 重启 / 关机 / 退出）
- **点击时钟** → 日历弹窗
- **点击音量** → 音量滑块
- **长按任意组件 3 秒** → 进入排序模式，左右拖动即可换位，松手自动保存
  （此时临时挂低级键盘钩子监听 Esc；常态下窗口不抢焦点，拿不到键盘）

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
    "material": 3,      // 0=None 1=Blur(废弃) 2=Acrylic 3=Mica 4=MicaAlt
    "theme": 1,         // 1=浅色 2=深色
    "barHeight": 30     // 28-40
  },
  "widgets": [
    { "id": "clock", "enabled": true, "order": 0, "options": {} }
  ],
  "api": {
    "qWeather": {
      "apiHost": "",             // 控制台-设置，形如 xxx.qweatherapi.com
      "authMode": 0,             // 0=API KEY
      "apiKey": "",              // 控制台-项目管理的凭据
      "latitude": "39.92",
      "longitude": "116.41",
      "placeName": "",           // 由城市搜索填入，仅用于界面显示
      "intervalMinutes": 30,
      "language": "zh"
    },
    "hitokoto": { "enabled": true, "intervalMinutes": 30 }
  },
  "metrics": { "intervalMs": 1000, "useIecUnits": true }
}
```

也可以点 Apple 菜单里的「MyTopBar 设置…」用图形界面调整。

### 和风天气配置

使用和风天气**新版** API，两个接口：

```
GET https://{ApiHost}/geo/v2/city/lookup?location={城市名}&number=10
GET https://{ApiHost}/weather/v1/current/{lat}/{lon}?lang=zh
X-QW-Api-Key: {ApiKey}
```

在设置窗口的「和风天气」区块里：

1. 填 **API Host** —— 控制台 → **设置** → API Host，形如 `h2a9cf3mhs.xy.qweatherapi.com`
2. 填 **API Key** —— 控制台 → **项目管理** → 凭据 → 创建凭据，认证方式选 **API KEY**
3. 在「地点」里搜城市名（中文或英文都行），点搜索结果即自动填入经纬度
4. 点 **应用并测试** —— 会立刻请求一次，结果显示在按钮下方

> **常见错误**：把「凭据 ID」（形如 `ABCDE12345`）当成 API Host 填进来。
> 两者完全不同的东西：凭据 ID 只在 JWT 认证时作为 `kid` 使用。
> 程序会做格式校验，填错时直接提示「API Host 格式不对」。

| 配置 | 获取位置 | 说明 |
|---|---|---|
| API Host | 控制台 → 设置 | 每账号随机分配，也是认证的一部分 |
| API Key | 控制台 → 项目管理 → 凭据 | 认证方式选 API KEY |
| 地点 | 设置窗口内搜索 | 经 GeoAPI 转换为经纬度，最多两位小数 |

参数改完必须点 **应用并测试** 才生效——输入过程中不会反复请求接口。

> 旧版的 `devapi.qweather.com/v7/weather/now?key=` 已被官方列入停用计划
> （公共域名 2026 年起逐步停止服务，API KEY 每日请求量 2027 年起受限），
> 程序已迁移到新版接口，不再保留旧路径。

#### 关于认证方式

官方推荐 JWT（Ed25519 签名）。本程序使用 **API KEY 请求头**（`X-QW-Api-Key`），理由：

- .NET 8 没有内置 Ed25519，需额外引入第三方库
- 桌面应用把 Ed25519 私钥存进 `settings.json`，反而比一个可随时吊销的 Key 更危险
- 官方目前只是"限制"API KEY 的每日请求量，并未禁用

若后续需要 JWT，改动点在 `WeatherWidgetViewModel`：把请求头换成
`Authorization: Bearer {token}`，并加一个 Ed25519 签名的 JWT 生成类，其余流程不变。

#### 响应需要 Gzip 解压

和风天气**所有响应都做了 Gzip 压缩**（`Content-Encoding: gzip`）。
共享的 `HttpClient` 必须开启 `AutomaticDecompression`，否则
`ReadAsStringAsync` 拿到的是压缩字节，按 UTF-8 解码成乱码，JSON 解析必然失败。

这是当初"天气一直不显示"的真实原因——凭据是对的，只是数据没解压。

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

**运行时切换材质的坑**：库的 `Apply()` 两条分支不对称——走系统背景时会把旧的
合成层清掉，走合成路径时却**不更新** `DWMWA_SYSTEMBACKDROP_TYPE`。
结果就是 Mica → Acrylic 之后 DWM 仍按 Mica 渲染，必须重启才看得到变化。
代码里的做法是 Win11 上让 `UseWindowComposition` 恒为 `false`，只切 `MaterialMode`，
让库每次都完整写入该属性；只有 Windows 10 才退回合成路径。
赋值顺序也有讲究：`IsDarkMode → CompositonColor → UseWindowComposition → MaterialMode`，
因为每个都是依赖属性，写一个就触发一次 `Apply()`。

### 前台窗口追踪

`ForegroundWindowService` 用 `SetWinEventHook` 监听 `EVENT_SYSTEM_FOREGROUND`。因为原生回调不保证在 UI 线程，且不应在其中执行耗时操作，所以只做「标记待刷新」，实际读取合并后投递到 Dispatcher 执行。

应用名按优先级解析：已知应用映射表 → 可执行文件的 `FileDescription` / `ProductName` → 窗口标题 → 进程名。

### 不抢焦点

顶栏加了 `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` 扩展样式，配合 `ShowActivated="False"`，
点击时前台窗口保持不变——和 macOS 菜单栏一致。

代价是窗口永远拿不到键盘焦点，所以「长按 3 秒进入排序模式」里的 Esc 退出
不能靠 `PreviewKeyDown`。实测 `SetForegroundWindow` 对这种窗口无效
（系统前台锁定规则会拒绝，`hwndActive` 始终不变），改为排序期间临时挂一个
`WH_KEYBOARD_LL` 低级键盘钩子只监听 Esc，退出时立刻摘掉。

### 配置编码

`SettingsService` 读写配置都显式使用 `UTF8Encoding(false)`（无 BOM）。
不指定编码时会走系统 ANSI 代码页，中文 Windows 上是 GBK 一类——
`placeName` 这类中文字段会写成乱码，乱码字节还可能撞上引号边界破坏 JSON 结构，
导致整个配置无法解析、所有设置丢失。

## 已知限制

- **Blur 材质**：Windows 11 已废弃 `ACCENT_ENABLE_BLURBEHIND`，效果与 Acrylic 相同，
  已从设置项移除（旧配置里的 `material: 1` 自动归一到 Acrylic）
- **Mica 与 Acrylic 观感不同**：Mica 走原生 SystemBackdrop，取壁纸配色、不透壁纸内容；
  Acrylic 走系统背景模糊，会透出下方内容
- **歌词匹配**：优先网易云（中文曲库覆盖好，用的是非公开接口，随时可能失效），
  回退 LRCLIB。纯音乐、小众本地曲目可能匹配不到；只有纯文本歌词时显示首行
- **单显示器**：顶栏跟随窗口所在屏幕，未做多屏独立配置
- **全屏应用**：未处理 `ABN_FULLSCREENAPP`，全屏视频/游戏时顶栏仍压在画面上
- **切换音频设备**：未实现 `IMMNotificationClient`，插耳机或切蓝牙后音量数字不再跟随
- **未实现开机自启**

## 项目结构

```
MyTopBar/
├── Assets/
│   ├── Fonts/       HarmonyOS Sans SC
│   └── Icons/       顶栏图标几何（Apple / 音量）
├── Infrastructure/
│   ├── Common/      AppBarController（工作区占位）、PopupHost、AppLog
│   ├── Interop/     Win32 P/Invoke 集中声明
│   ├── Material/    材质枚举
│   └── Interfaces/  服务契约
├── Models/          数据模型与配置结构
├── Services/        前台窗口、音频、电源、系统指标、主题、配置、媒体会话
├── ViewModels/      主视图模型、弹窗视图模型、设置视图模型
├── Views/           日历、音量、设置窗口
├── Widgets/         各组件视图模型 + 注册表 + 天气/城市搜索封装
└── Styles/          主题、组件样式、设置窗口控件样式
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
