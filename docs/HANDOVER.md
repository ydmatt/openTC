# HANDOVER

## v1.0.24

- `FilePaneViewModel.SelectTabAsync` 在激活标签（包括再次点击当前标签）时重新执行目录读取；固定标签仍先回到固定路径。
- `DriveEntry` 增加总容量、可用空间、占用率、低空间判断和格式化显示；盘符 ComboBox 改用容量/进度条模板，低于 10% 可用空间使用红色。
- 文件网格空白处新增鼠标框选状态机和可视选择矩形；拖放源在鼠标按下时保存真实多选路径，修复多选拖放只传递单项。
- Release 全量测试 85/85 通过，包含 WPF 标签刷新和新盘符模板实际渲染；独立启动、不同工作区实例转发和外部更新器黑盒验证通过，模拟升级 `1.0.24 → 1.0.25` 返回 0，`data`、备份和日志均正常。
- v1.0.24 包含 407 个文件，主程序和维护程序文件版本均为 `1.0.24.0`，发布 ZIP 为 `T:\mytc-updates\MYTC-v1.0.24-win-x64.zip`，SHA-256：`A28ED2010356C5B711EC7AD393E7E5C7BE3E843E031B75C5D3BFB308E5230136`；T 盘未生成 v1.0.24 release 目录。

## v1.0.23

- 文件网格拖放会识别鼠标悬停的同级文件夹行；普通左键拖放到文件夹默认移动，`Ctrl` 复制，`Shift` 移动，拖到空白处仍以当前目录为目标并保持旧行为。
- 右键拖放到同级文件夹时，复制、移动、创建快捷方式菜单统一使用该文件夹作为目标；目标解析由 `FileDropGuards.ResolveDropDirectory` 限制为当前目录的直接子文件夹，避免任意路径注入。
- `MainWindow` 的拖放处理和碰撞检测支持显式目标目录；`MainWindowViewModel` 的批量操作刷新仍同时维护源窗格和当前目标窗格。
- 新增混合文件/文件夹移动及同级目标安全边界测试；Release 全量测试 83/83 通过。
- Release 测试 83/83、独立启动、单实例转发和外部更新器黑盒验证均通过；升级器模拟 `1.0.23 → 1.0.24` 返回 0，保留 `data`、备份和日志均正常。
- v1.0.23 包含 407 个文件，主程序和维护程序文件版本均为 `1.0.23.0`，发布 ZIP 为 `T:\mytc-updates\MYTC-v1.0.23-win-x64.zip`，SHA-256：`C26101B05A480F11425FBE6FD03C054908A48474115E51541CED057CE55BAFB2`；T 盘未生成 v1.0.23 release 目录。

## v1.0.22

- `WorkspaceIconCatalog` 现在统一提供 1–9、A–Z 共 35 个徽标及完整的 37 项 UI 选项目录；工作区管理通过绑定动态呈现。
- 自动模式扫描工作区名称中的 ASCII 字母/数字；A–Z 大小写统一，0 不在徽标库中。旧 `1` / `W` 配置保持兼容，工作区 schema 仍为 2。
- STA 测试验证所有自动/手动徽标和 37 项下拉列表；Release 全量测试 81/81 通过。
- 交付继续只向 `T:\mytc-updates` 写入升级 ZIP，不向 T 盘复制完整 release 目录。
- v1.0.22 发布包的单实例转发、不同工作区并行实例和外部更新器测试通过。升级 ZIP 为 `T:\mytc-updates\MYTC-v1.0.22-win-x64.zip`，SHA-256：`6323C6D27CF171F6FCFE3A417FA5DB39D0FEFC682DDDEF8EE1FC221EF9334425`；T 盘未生成 v1.0.22 release 目录。

## v1.0.21

- 修复 WinRAR 同名目录解压启动错误：目标目录在创建后才作为 `WorkingDirectory` 使用，并新增可选的已安装 WinRAR 真实集成测试。
- 工作区 `1` / `W` 使用 Windows 原生任务栏叠加徽标，标题栏图标与任务栏按钮均可区分；默认 TC 不显示徽标。
- `JsonUiPreferencesStore` 的最终原子替换使用按配置路径命名的当前用户跨进程互斥，适配不同工作区多实例共享全局界面设置。
- 全量测试 81/81 通过；发布时只向 `T:\mytc-updates` 投递升级 ZIP，不再向 T 盘复制完整 release 目录。
- v1.0.21 发布包的单实例转发、不同工作区并行实例及外部更新器测试通过。升级 ZIP 为 `T:\mytc-updates\MYTC-v1.0.21-win-x64.zip`，SHA-256：`AA5375D0034345C8A7494932E6179CB311AFF38C87B92DFA3BD63D74CF8BA209`；T 盘未生成 v1.0.21 release 目录。

## v1.0.20

- 支持以 `/work`、`/1test` 或 `--workspace name` 启动不同工作区实例；同工作区重复启动仍会激活已有窗口。不同工作区任务栏分组和会话文件彼此独立。
- 工作区管理可设置“自动、默认 TC、上标 1、上标 W”图标。自动模式中 `work` 命中 `W`，`1test` 命中 `1`；其他首字母暂使用默认 TC，等待用户确认视觉效果后再补齐。
- 右键菜单新增 WinRAR“解压到同名文件夹（R）”，旧菜单配置自动增量迁移。

## v1.0.19

- 文件右键菜单新增 WinRAR“解压到当前文件夹（X）”。首次运行需确认检测到的默认路径；不使用默认路径时可手动选择 `WinRAR.exe`，全局设置可随时修改。配置无效时不会启用该动作。

## 当前结论

- `v1.0.17` 已完成代码、74/74 自动化测试、正式打包和独立启动/单实例转发烟雾测试。完整目录位于 `T:\mytc-releases\MYTC-v1.0.17-win-x64`，升级 ZIP 位于 `T:\mytc-updates\MYTC-v1.0.17-win-x64.zip`。此版本在文件右键菜单增加“刷新（E）”，并自动迁移已有菜单配置。
- 生产候选版 `v1.0.0` 已完成开发、48/48 全量测试、真实发布程序黑盒验收和打包。
- 技术路线：C# + WPF + .NET 10 LTS。
- 发布方式：win-x64 自包含绿色目录及 ZIP。
- 用户配置位于程序旁 `data/`，已被 `.gitignore` 排除。
- Release 自动化测试：48/48 通过。
- STA WPF 冒烟测试：四个可见窗格、两级右键菜单、`Enter` 进入文件夹、标签栏真实双击、第二次被标记为重复事件的单次 `Del`、`Backspace` 上级目录、文本/文件路径粘贴、Windows 打开方式调用、标签完整复制、全局设置及两类右键菜单设置通过。
- 真实临时文件“移到回收站 → 撤销还原”集成测试通过。
- ZIP 解压副本已在 Windows 10 上成功创建主窗口；发布包不包含 `data/`。
- 本地 ZIP：`artifacts/release/MYTC-v1.0.0-win-x64.zip`。
- ZIP SHA-256：`7717507957FDF392AD03DA40A9DCAF343B13BEBA8CCB238983FD4A545FAF7BB7`。
- 发布目录含 407 个清单/程序/说明文件，其中 `MYTC.Maintenance.exe` 是自包含单文件；发布源不含 `data`。
- ZIP 已同步到 `T:\mytc-updates\MYTC-v1.0.0-win-x64.zip`，完整目录已同步到 `T:\mytc-releases\MYTC-v1.0.0-win-x64`；目标 ZIP 哈希一致，406 个受管文件逐项校验无误。
- 主程序单实例目录转发、独立维护窗口、Win+E 桥接启动/退出、清理宿主均通过发布版黑盒测试。
- 单文件更新器模拟 `1.0.0 → 1.0.1` 成功，目标清单、备份、日志、更新内容和 `data` 保留均通过。
- Win+E 桥接注册表写入未在开发机实际启用；必须由用户把生产版复制到本机固定目录后，在维护工具中手动确认启用。

## 当前环境

- 工作区：`D:\CodexProject\MYTC`
- 项目 SDK：`.tools\dotnet\dotnet.exe`（被忽略）
- 解决方案：`MYTC.slnx`
- 发布输出：`artifacts\release\`（被忽略）
- 用户文档：`docs\USER_GUIDE.md`

## 构建命令环境

每次新 PowerShell 需要设置：

```powershell
$env:DOTNET_ROOT='D:\CodexProject\MYTC\.tools\dotnet'
$env:DOTNET_CLI_HOME='D:\CodexProject\MYTC\.tools\dotnet-home'
$env:NUGET_PACKAGES='D:\CodexProject\MYTC\.tools\nuget-packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
```

测试：

```powershell
& "$env:DOTNET_ROOT\dotnet.exe" test MYTC.slnx -c Release
```

发布生产包：

```powershell
& .\packaging\Build-Release.ps1 -Version 1.0.0
```

## 实现说明

- `MYTC.Domain`：文件、工作区、快捷键、右键菜单和文件操作模型。
- `MYTC.Application`：服务抽象、导航、排序、活动/目标窗格逻辑及默认配置。
- `MYTC.Infrastructure`：目录/盘符服务、托管文件操作、默认打开和 JSON 配置存储。
- `MYTC.App`：WPF UI、窗格/标签 VM、快捷键匹配器及设置对话框。
- `MYTC.Maintenance`：注册/恢复 UI、Win+E 桥接、外部更新和临时宿主清理。
- `MYTC.Tests`：领域、导航、配置、文件沙箱和 STA WPF 冒烟测试。

## 已知边界

- `v0.1.7` 由 MYTC 自身文件操作后主动刷新；外部文件变化需要手动刷新。
- 文件列表已接入 Windows Shell 图标；尚未接入完整 Shell 右键菜单。
- 任务栏“固定到任务栏”由 Windows Shell 根据 AppUserModelID 和系统策略生成；MYTC 已提供所需身份和重新启动属性，不绕过系统禁用固定的策略。
- 不浏览 FTP 和压缩包，不包含搜索、配置导入导出和自定义非四窗格布局。
- Win+E 桥接仅让 Win+E 启动 MYTC；Explorer 仍负责文件夹、磁盘、桌面、任务栏、虚拟 Shell 文件夹和登录外壳。
- Win+E 使用当前用户会话中的低级键盘钩子；未运行时安全回退 Explorer。
- 生产版 ZIP 使用文件哈希完整性校验，但尚无商业代码签名证书。
- 文件操作使用托管后台服务；`IFileOperation` 是后续 Shell 深度集成候选，不是当前实现。
- Computer Use 在当前 Windows 10 捕获 WPF 时返回 `SetIsBorderRequired 0x80004002`，因此界面验收使用 STA 应用内渲染测试完成。

## 安全约束

- 文件操作测试只允许在验证后的唯一临时沙箱运行。
- 永久删除必须保持 UI 二次确认。
- 普通删除必须进入回收站。
- 发布包不得包含测试或真实用户 `data/`。
- 不要提交用户工作区 JSON、路径、日志或发布产物。
- 升级 ZIP 放到 `T:\mytc-updates`；自 `v1.0.21` 起不再向 T 盘复制完整 release 目录。本地 release 目录仍用于验证和打包，不覆盖旧 `T:\mytc`。
- 首次生产迁移由用户关闭旧版后把旧 `data` 复制到本机固定安装目录；后续程序内升级永不覆盖 `data`。
- `T:` 为 SMB 映射盘，其他设备运行的 MYTC 不会出现在本机进程列表；还需验证目标核心文件可写，远端锁存在时请用户处理对应设备。
- `T:\mytc` 属于非本地位置，启动会触发 Windows“打开文件 - 安全警告”。同步后仅验证文件版本、数量和 `data` 完整性，不要自动启动 T 盘程序，也不要修改系统安全设置。
