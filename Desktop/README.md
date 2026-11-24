# Avalonia 桌面版

这是接入原有填涂功能的正式界面工程，版本为 **1.7.4**。可配合 Blue Marble 或 WPlace 官方蒙版使用。早期外观样板继续保留在 `prototypes/AvaloniaToolbar`。

## 启动与打包

已打包的 Windows x64 版本位于：

```text
publish/avalonia-v1.7.4/wplace_canYouHelpMe_Avalonia_v1.7.4.exe
```

这是自包含程序，使用者无需安装 .NET。正常双击会启用原有取色、范围选择、扫描和填涂功能。每次启动默认进入完整模式；本版首次启动会显示更新说明。若尚未保存蒙版类型，先完成蒙版选择。

开发需要 .NET 10 SDK；当前工作区也提供了忽略提交的 `artifacts/dotnet10/dotnet.exe`：

```powershell
.\artifacts\dotnet10\dotnet.exe run --project .\Desktop\Wplace.Desktop.csproj
.\Desktop\Publish.ps1
```

`Publish.ps1` 输出单文件 EXE 和仅包含 EXE 的 ZIP，保留旧版目录，不打包用户设置、日志或查看记录。已有同名 ZIP 时会拒绝覆盖；仅重建 EXE 可加 `-SkipZip`。

## 界面

- **完整模式**：双颜色及原始 RGB、范围、点阵显示、颜色规则、CPU 数量与自动选择、步长、速度、任务计数、百分比和预计剩余时间。
- **精简模式**：一行常用图标、两种颜色、步长、固定宽度的百分比和时间。鼠标悬停或 Tab 聚焦显示说明。没有 CPU、停止、更新说明、GitHub 或功能介绍按钮；运行时仍用 Esc 停止。
- **颜色管理**：沿用 63 色以及想填/排除规则；支持 A 键连续采样，也可直接点击色板。相同颜色只能属于一组。清空想填后默认全部颜色，再减去排除。
- **更新说明**按版本离线收录。当前版本默认展开，历史条目可以单独展开、收起。每版首次启动显示一次，也可从完整布局手动打开。正文滚动时，底部“知道了”按钮保持可见。
- **功能介绍**：仅完整模式有入口，包含 10 个目录条目及代码绘制的示例图。图是操作说明；实际划取仍直接作用于鼠标所在屏幕。
- **提示与确认**：未选范围、遗漏检测建议、补涂确认及检测结果统一使用新窗口；“不再提示”只作用于开始前建议。
- **移动窗口**：完整模式和弹窗可按住标题文字或标题栏空白处拖动；精简模式可拖动左侧六点手柄或工具栏空白处。按钮、输入框保持各自的操作。

## 与原功能的衔接

`MainWindow` 及子窗口由 Avalonia 绘制。`WindowsEngineHost` 在独立 STA 消息线程中持有原 `Form1` 内核，原主窗体始终隐藏。命令串行分发至内核，界面只读取不可变的 `DesktopSnapshot`。

屏幕取色、全局键盘钩子、矩形/正交多边形选择及透明范围覆盖层继续使用现有 Windows 实现。选择覆盖层增加了像素风操作提示，但坐标算法不变。因此这一版仍然是 **Windows 应用**，没有宣称跨平台填涂支持。

扫描匹配、半透明颜色归组、颜色筛选、白色优先与白色免检、非白色每次取色后的首次涂色验证、补涂均沿用原流程。Blue Marble 使用孤岛检测，官方蒙版只匹配范围内的半透明采样点。界面关闭时先取消并等待任务退出，再释放钩子、覆盖层和消息线程。

编辑数值时 A 不会抢走输入；功能介绍、更新说明和确认窗口期间暂停全局取色。连续选色支持保持本程序在前台、仅把鼠标移到画布后按 A。

## 设置兼容

- 继续读写 EXE 同目录的 `user_settings.json`，保留 CPU、步长、速度等原有偏好。缺少有效的 `Mask` 值时显示首次选择引导。用户选定后立即保存，完整布局和精简布局共享该值。
- 完整／精简布局仅在本次运行中切换，不写入设置；忽略旧设置中的 `CompactLayout`，每次启动均进入完整模式。
- 颜色规则和范围仍仅对本次运行有效，不写入设置。
- 更新说明仍共用 `%LOCALAPPDATA%\wplace_canYouHelpMe\release_notes_state.json`，以版本区分。
- 保留原命令行算法参数。诊断时可用 `--settings-path <绝对路径>`、`--history-path <绝对路径>` 隔离设置和查看记录。
- 新旧程序共用单实例锁，避免同时安装两组全局快捷键。再次启动会激活已运行版本。

## 验证

只检查界面、不启用桌面读取、快捷键或填涂：

```powershell
& '.\publish\avalonia-v1.7.4\wplace_canYouHelpMe_Avalonia_v1.7.4.exe' --preview-ui
& '.\publish\avalonia-v1.7.4\wplace_canYouHelpMe_Avalonia_v1.7.4.exe' --preview-release-notes
& '.\publish\avalonia-v1.7.4\wplace_canYouHelpMe_Avalonia_v1.7.4.exe' --capture "$PWD\artifacts\avalonia-review"
```

`--capture`/`--smoke-test` 使用真实 Avalonia 控件渲染所有页面，并输出 `checks.json`、`layout.json` 和 PNG；失败写 `failure.txt` 并返回非零退出码。精简进度示例使用模拟数值，应用正常启动时只显示内核实际进度。

```powershell
.\artifacts\dotnet10\dotnet.exe run --project tests\Desktop\Desktop.IntegrationTests.csproj -c Release
dotnet run --project tests\AutoFill\AutoFill.RegressionTests.csproj -c Release
dotnet run --project tests\ColorMatching\ColorMatching.RegressionTests.csproj -c Release
dotnet run --project tests\UpdateCheck.RegressionTests.csproj -c Release
dotnet run --project tests\OverlayCapture\OverlayCapture.RegressionTests.csproj -c Release
dotnet run --project tests\ReleaseNotes\ReleaseNotes.RegressionTests.csproj -c Release
```

桥接集成测试禁用全局钩子，使用自建测试画布执行遗漏扫描，并在自动填涂发送输入前取消任务。覆盖层回归需要能读取和移动鼠标的 Windows 桌面。上述验证不等同于在实际 WPlace 页面配合 BlueMarble 或官方蒙版完成一轮填涂；网页状态、页面缩放与剩余颜料仍需要现场确认。

### 1.7.0 验证记录（2026-10-15）

| 范围 | 结果 |
| --- | --- |
| 最终自包含 EXE 的页面及交互检查 | 29/29；包括真实控件事件、模式切换、固定进度布局、颜色管理增删与重复打开、目录导航、提示确认和 Esc 关闭 |
| Windows 内核桥接 | 22/22；包括设置兼容、双通道取色、颜色规则、受控画布遗漏扫描、取消和退出清理 |
| 原填涂 / 颜色匹配 / 更新检查回归 | 分别 13/13、10/10、9/9 |
| 覆盖层回归 | 正常桌面运行 16/17；鼠标位置恢复的一项受外部鼠标移动影响，单独重试 1/1，通过覆盖全部 17 项 |
| 新窗口的原生桌面检查 | 首次更新说明、查看记录、功能介绍目录、Esc 关闭及正常退出均已验证 |
| 旧 WinForms 公告回归 | 7/8；一项启动焦点断言受前台窗口限制而失败，未计为通过；新公告已另行验证 |
| 独立文件与错误退出码 | 仅 EXE 即可启动；失败的界面检查返回 1，成功返回 0 |

最终界面检查记录与截图：`artifacts/avalonia-v170-final-acceptance-b/`。打包记录为同目录的 `package.json`，含 EXE、ZIP 的 SHA-256。发布目录和 ZIP 只包含程序，不包含本地设置或日志。尚未在实际网页执行完整填涂，也未完成多显示器的现场验收。

### 1.7.1 窗口拖动修复（2026-10-15）

标题栏改为包含内边距的透明命中区域；拖动事件跳过按钮和输入框。精简模式的六点手柄使用专用拖动区域，工具栏通过捕获鼠标、计算屏幕像素位移来移动窗口，释放、失去捕获或退出窗口时结束拖动。

已在最终打包 EXE 上实际验证 6 个拖动位置：精简手柄、工具栏空白、完整标题栏空白、颜色管理、更新说明和功能介绍标题栏。验证了松开后数值按钮、展开布局及关闭弹窗仍可操作。原界面交互检查 29/29 通过，记录与包校验信息在 `artifacts/avalonia-v171-final/`。本次没有修改填涂内核，旧版 1.7.0 包保留。

### 1.7.2 验证记录（2026-10-15）

布局改为仅在本次运行中保留。桥接集成测试 24/24 通过，包括忽略旧设置中的 `CompactLayout: true`、精简模式退出后不保存布局、重新启动恢复完整模式，以及继续保存扫描参数与提示偏好。

最终自包含 EXE 的界面与交互检查 30/30 通过；已查看完整界面、更新说明及功能介绍截图，确认版本为 1.7.2、更新说明移除窗口移动修复段落，并在使用说明中补充 BlueMarble 与 WPlace 官方蒙版。记录位于 `artifacts/avalonia-v172-final/`。ZIP 仅含 EXE，包内外 SHA-256 一致；文件版本为 `1.7.2.0`，旧版 1.7.1 EXE 与 ZIP 的哈希保持不变。官方蒙版相关变更为使用说明更新，本次未在实际网页执行填涂验收。

### 1.7.3 验证记录

| 检查 | 结果 |
| --- | --- |
| 最终 EXE 的只读界面检查 | 33/33 通过，覆盖首次引导、选定后的界面恢复，以及两个布局的蓝色检测按钮 |
| Windows 内核桥接 | 29/29 通过，覆盖旧设置升级、选择后立即保存、重启恢复，以及受控画布上的遗漏检测与颜色排除 |
| 颜色匹配 | 10/10 通过，覆盖 63 种原色及其半透明显示色的取整组合 |
| Windows x64 发布包 | 文件版本为 `1.7.3.0`。ZIP 中只有一个 EXE，其 SHA-256 与独立 EXE 一致 |

界面检查结果在 `artifacts/avalonia-v173-final/checks.json`，本版界面截图见[项目 README](../README.md)。受控画布验证了候选点识别和取消补涂，实际 WPlace 网页上的补涂效果尚待验证。

### 1.7.4 验证记录

Windows x64 发布包通过 37/37 项只读界面检查，覆盖历史版本排序、当前版本默认展开以及旧版说明独立展开和收起。模拟同版本时，页脚保留 GitHub 按钮。模拟更高版本时，检查确认了按钮上的版本号、辅助功能名称，以及橙色背景在不同时间点的颜色变化。[更新提示截图](../README/v1.7.4/update-available.png)

更新检查回归测试通过 9/9 项，覆盖发布页重定向、API 回退和离线错误处理。发布文件版本为 `1.7.4.0`，ZIP 内外的 EXE 哈希一致。界面检查记录位于 `artifacts/avalonia-v174-notes/review/checks.json`。
