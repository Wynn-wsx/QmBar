# QmBar

一个常驻 Windows 任务栏的迷你播控条：**毛玻璃胶囊**样式，显示封面、滚动歌名/歌手和**逐字歌词**，
带上一首 / 播放暂停 / 下一首、音量、可拖拽进度条。

用 C# 写成，**零第三方依赖**——只用系统自带的 .NET Framework 和 `csc.exe` 编译，
不需要装 .NET SDK、NuGet 包或任何运行时。

> A tiny frosted-glass media control bar that floats on the Windows taskbar.
> Album art, scrolling title/artist, word-by-word lyrics, transport buttons, volume and a seek bar.
> Built with nothing but the in-box .NET Framework `csc.exe` — no SDK, no NuGet, no third-party libraries.

```
┌──────────────────────────────────────────────────────────────┐
│ ▓▓▓  你听得到 - 周杰伦          🔊  ⏮  ▶  ⏭  ‹              │
│ ▓▓▓  多想藏着你的好                                          │
│ ▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔  ← 进度条
└──────────────────────────────────────────────────────────────┘
  封面        歌名 - 歌手（过长走马灯）        音量 上/播放/下 收起
                        逐字歌词（已唱部分绿色高亮）
```

## 功能

| | |
| --- | --- |
| 播放信息 | 通过系统 [GSMTC / SMTC](https://learn.microsoft.com/uwp/api/windows.media.control) 读取，**任何**把播放状态暴露给系统的播放器都能跟随（QQ音乐、网易云、酷狗、酷我、Spotify、抖音…） |
| 封面 | 直接从 SMTC 的缩略图流解码，按曲目缓存，不重复解码 |
| 歌名 | `歌名 - 歌手`，超出宽度自动走马灯 |
| 歌词 | 本地缓存 → QQ音乐 QRC（**逐字**时间轴）→ QQ音乐 LRC → LRCLIB；已唱部分按字绿色高亮 |
| 控制 | 上一首 / 播放暂停 / 下一首；音量：单击图标静音、按住左右拖调系统主音量、滚轮 ±2 |
| 进度条 | 可拖拽跳转（**播放源必须支持 SMTC 定位**，QQ 音乐不支持，见「已知限制」） |
| 收起 | 向左收缩成一个胶囊，左边缘不动、右边缘往左回收 |
| 位置 | 默认自动贴靠托盘左侧；拖一下即切成手动位置，之后由设置页的「左边缘 X」决定，范围覆盖整条任务栏 |
| 设置页 | 右键 →「设置…」，改动即时生效；也可 `QmBar.exe --settings` 直接打开 |
| 开机自启 | 设置页开关或右键菜单，写 `HKCU\...\CurrentVersion\Run` |

## 运行

```powershell
# 直接跑
.\bin\QmBar.exe

# 打开设置页
.\bin\QmBar.exe --settings
```

## 编译（零下载）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Run      # 编完直接启动
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Clean    # 清理
```

`build.ps1` 调的是 Windows 自带的
`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`。
注意它是 **C# 5** 编译器（`/langversion:6` 都不认），所以源码里不能用字符串插值、`?.`、`nameof`、
表达式体成员和自动属性初始化器；中文源码靠 `/codepage:65001` 保证不乱码。

## 快捷方式 / 自启 / 卸载

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1              # 桌面 + 开始菜单快捷方式
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -StartMenuOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -AutoStart
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Uninstall
```

开始菜单快捷方式落在 `%APPDATA%\Microsoft\Windows\Start Menu\Programs\`，搜 `QmBar` 就能找到；
想钉到开始屏幕的磁贴区，右键它选「固定到"开始"屏幕」（这一步 Windows 不允许程序代劳）。

## 配置

所有设置落在 `%APPDATA%\QmBar\config.ini`（纯 INI，可直接编辑），歌词缓存在
`%APPDATA%\QmBar\lyrics\`，日志在 `%APPDATA%\QmBar\QmBar.log`（超过 256 KB 自动重建）。

常用键：`Width`（面板宽度）、`Opacity`（毛玻璃不透明度 15–100）、`Glass`、`FontSize`、
`AutoPlace` / `PosX` / `OffsetY`（位置）、`SourceAppId`（锁定播放源）、`Lyrics` / `Karaoke`、
`ShowProgress` / `ShowVolume`、`HideOnFullscreen`、`Locked`、`AutoStart`、`PollMs`。

## 交互

| 操作 | 效果 |
| --- | --- |
| 左键 上一首 / 播放暂停 / 下一首 | 控制当前播放源 |
| 左键 音量图标 | 静音 / 取消静音 |
| 按住音量图标左右拖 | 调**系统默认输出设备**的主音量 |
| 滚轮 | 音量 ±2 |
| 按住进度条拖 | 跳转（需播放源支持 SMTC 定位） |
| 左键最右的 `‹` | 向左收起 |
| 按住空白处拖 | 移动控件，并自动切成手动位置 |
| 双击 | 收起 / 展开 |
| 右键 | 菜单（第一项「设置…」） |

## 项目结构

```
build.ps1          零下载编译脚本（in-box csc.exe）
install.ps1        快捷方式 / 开机自启 / 卸载
使用说明.md         完整中文说明（交互、菜单、配置项、实现细节、已知限制）
src/
  Program.cs       入口、单实例、日志、全局异常捕获
  BarForm.cs       窗口本体：布局、绘制、后台轮询线程 MediaWorker
  SettingsForm.cs  设置页
  Glass.cs         分层窗口毛玻璃：DIBSection + 预乘 alpha + UpdateLayeredWindow
  Smtc.cs          GSMTC 会话枚举与播放控制（反射调用 WinRT）
  Lyrics.cs        歌词：缓存 → QQ音乐 QRC/LRC → LRCLIB，LRC/QRC 解析
  QqDes.cs         QQ音乐 QRC 的自定义 TripleDES + zlib 解密
  Audio.cs         系统主音量（Core Audio IAudioEndpointVolume）
  Native.cs        P/Invoke：任务栏探测、窗口样式、DPI
  Config.cs        INI 配置持久化 + 开机自启
research/          逆向 QQ音乐歌词接口的调研脚本与实测报告（REPORT.md）
tools/             调试探针：任务栏探测、抓屏比对、会话/歌词/卡拉OK 自测
```

## 实现要点

三个值得记下来的坑，细节都在 `使用说明.md`：

1. **为什么不是任务栏子窗口。** 最初把控件 `SetParent` 到 `Shell_TrayWnd` 做成真正的任务栏子窗口，
   窗口层级、样式、z 序全都对，`PrintWindow` 也能画出完整内容，但屏幕上就是空的——
   Win11 任务栏上的 XAML 岛 `Windows.UI.Composition.DesktopWindowContentBridge`
   带 `WS_EX_NOREDIRECTIONBITMAP`，走 DirectComposition 直接合成，**不受 Win32 同级 z 序约束**，
   整片把 GDI 子窗口压住。最后改成「算出任务栏空白槽位 → 把无边框置顶窗摆上去」，
   视觉一致，还能正常收鼠标消息。
2. **分层窗口的 alpha。** 把 `Graphics.FromImage(bmp).GetHdc()` 拿到的 DC 交给
   `UpdateLayeredWindow` 会**丢掉 alpha 通道**（函数照样返回 `true`，窗口整个透明）。
   必须走 `CreateDIBSection` + `Marshal.Copy` 手工拷预乘 alpha 位图。
   另外分层窗口在第一次 `UpdateLayeredWindow` 之前收不到 `WM_PAINT`，绘制得由定时器驱动。
3. **`TextRenderer` 不认 `Graphics.SetClip`。** 它走 GDI，裁剪要自己做离屏位图再贴回来，
   否则走马灯会直接画到封面和按钮上。

## 已知限制

- **QQ 音乐不支持 SMTC 定位**：`Controls.IsPlaybackPositionEnabled` 为 `false`，
  `TryChangePlaybackPositionAsync` 返回 `true` 但位置不变（实测：暂停时请求 137s 仍停在 25.4s）。
  所以进度条在 QQ 音乐下是只读的；要真跳转得用 UIAutomation 去点它自己的进度条。
- 「嵌入任务栏」保留了实验开关（`Embed=1`），Win11 下会被上面那层合成岛盖住，默认关闭。
- 歌词是按「歌名 + 歌手 + 时长」去 QQ音乐搜索匹配的（SMTC 不给 songmid），
  同名翻唱、live 版可能匹配到别的版本。
- 只支持 Windows 10 1809+ / Windows 11（依赖 GSMTC）。

## 第三方与致谢

- QRC 歌词的**自定义 TripleDES** 算法实现参考了
  [WXRIW/Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper)（Apache-2.0）
  的 `Decrypter/Qrc/DESHelper.cs`；本仓库中的 `src/QqDes.cs` 与 `research/qqmusic_des.py`
  是对它的再实现，依 Apache-2.0 第 4 条保留来源声明。上游文件未随本仓库分发。
- 无时间轴歌词兜底来自 [LRCLIB](https://lrclib.net/)。
- 播放信息与控制基于 Windows 的
  [GlobalSystemMediaTransportControlsSessionManager](https://learn.microsoft.com/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager)，
  通过反射调用，不需要 WinRT 投影程序集。

## 免责声明

本项目仅供个人学习与自用。它通过**未公开的** QQ 音乐接口获取歌词，这些接口随时可能变更；
请遵守相应服务的使用条款，不要用于商业用途或大规模抓取。

## License

[MIT](LICENSE)
