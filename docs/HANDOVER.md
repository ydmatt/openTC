# HANDOVER

## 当前结论

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
- Windows 接管注册表写入未在开发机实际启用；必须由用户把生产版复制到本机固定目录后，在维护工具中手动确认注册。

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
- Windows 接管仅覆盖普通文件夹、磁盘和 Win+E；Explorer 仍负责桌面、任务栏、虚拟 Shell 文件夹和登录外壳。
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
- `v1.0.0` 起把升级 ZIP 放到 `T:\mytc-updates`，把可复制目录放到按版本命名的 `T:\mytc-releases`；不覆盖旧 `T:\mytc`。
- 首次生产迁移由用户关闭旧版后把旧 `data` 复制到本机固定安装目录；后续程序内升级永不覆盖 `data`。
- `T:` 为 SMB 映射盘，其他设备运行的 MYTC 不会出现在本机进程列表；还需验证目标核心文件可写，远端锁存在时请用户处理对应设备。
- `T:\mytc` 属于非本地位置，启动会触发 Windows“打开文件 - 安全警告”。同步后仅验证文件版本、数量和 `data` 完整性，不要自动启动 T 盘程序，也不要修改系统安全设置。
