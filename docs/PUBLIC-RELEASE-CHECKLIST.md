# openTC 公开发布清单

本清单用于把 openTC 整理为可持续维护的公开仓库。GitHub 仓库已完成改名并切换为 Public；许可证和历史内容治理仍需继续完成。

## 已完成

- [x] README、窗口标题、菜单、关于对话框、维护工具和发布说明统一使用 openTC 品牌。
- [x] README 明确说明 openTC 与 Total Commander 的独立关系，避免造成官方隶属或授权误解。
- [x] 保留旧版 `MYTC.exe`、`MYTC.Maintenance.exe`、`MYTC.update.json`、`data/` 和内部注册标识，确保现有绿色升级、工作区和 Win+E 配置可迁移。
- [x] 增加贡献指南、安全漏洞报告规则和项目商标说明。
- [x] `.gitignore` 排除 `data/`、日志、备份、工具链和发布输出。

## 公开后仍需处理

- [ ] 选择并确认开源许可证。当前仓库没有擅自添加许可证；没有许可证时，公开代码并不自动授予他人再分发或修改权。
- [ ] 审阅历史项目记录、会话记录和示例路径，确认其中的本机路径、内部工作流和测试样例可以公开；不需要公开的内容应移出仓库或匿名化。
- [x] GitHub 仓库已从 `MyTC` 改名为 `openTC`。
- [x] GitHub 仓库已从 Private 切换为 Public。

## 公开维护检查命令

```powershell
git grep -n -i -E 'password|token|secret|BEGIN .*PRIVATE|webhook|feishu:|oc_[A-Za-z0-9]+'
git status --short
dotnet test MYTC.slnx -c Release
```

公开仓库的后续提交仍应确认扫描结果只包含测试占位词、说明文字或已脱敏示例；任何真实凭据、私密链接、个人数据或客户资料都必须先移除。
