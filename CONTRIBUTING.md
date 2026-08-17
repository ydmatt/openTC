# Contributing to openTC

感谢你关注 openTC。openTC 是一个面向 Windows 10/11 的开源四窗格文件管理器，项目使用 C#、WPF 和 .NET 10。

## 开始之前

- 先阅读 [README](README.md)、[AI_RULES](AI_RULES.md) 和 [架构说明](docs/ARCHITECTURE.md)。
- 使用 Windows 10/11 x64；仓库提供项目本地 .NET SDK 的构建脚本，通常不需要修改系统环境。
- 不要把个人配置、真实工作路径、网络共享内容、`data/`、`logs/`、`backups/` 或发布输出提交到 Git。

## 修改与验证

1. 为可观察的行为先增加或更新自动化测试。
2. 文件操作测试只能使用测试创建的临时目录。
3. 运行 `dotnet test MYTC.slnx -c Release`，并确认没有把用户目录作为测试目标。
4. UI、Shell、Win+E 和升级器改动需要补充相应的 Windows 实机验收说明。
5. 更新 `docs/TASKS.md`、`docs/CHANGELOG.md`、`docs/HANDOVER.md` 和对应的会话记录。

## Pull Request

- PR 标题说明行为变化，不要只写“更新代码”。
- 描述用户可见变化、兼容性影响、测试命令和未覆盖的 Windows 环境。
- 不要提交密钥、访问令牌、真实客户资料或未经授权的第三方内容。
- openTC 与 Total Commander 没有隶属关系；涉及该名称时请使用描述性、非官方表述。
