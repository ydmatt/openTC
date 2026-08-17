# openTC v1.0.25

## 品牌迁移与公开仓库预检

- 产品界面、关于对话框、Win+E 维护工具、升级提示和用户文档统一使用 openTC。
- 产品定位改为“受 Total Commander 启发的独立开源四窗格文件管理器”，并加入独立项目及商标说明。
- 为保护现有用户的绿色升级和配置，保留 `MYTC.exe`、`MYTC.Maintenance.exe`、`MYTC.update.json`、`data/`、注册表备份和内部互斥标识。
- 发布包目录名改为 `openTC-v1.0.25-win-x64`；包内兼容文件名暂不变。
- 新增贡献指南、安全策略、项目说明和公开发布清单。
- GitHub 仓库已改名为 `ydmatt/openTC` 并切换为 Public。

## 兼容性说明

这是品牌迁移，不是一次强制的物理文件迁移。已有快捷方式、Win+E 注册、任务栏固定项和用户数据仍按原兼容标识工作；后续如要把可执行文件也改成 `openTC.exe`，需要单独设计一次迁移和回滚方案。

## 验证

- `dotnet test MYTC.slnx -c Release`
- 发布前扫描 Git 历史和工作树中的凭据、私密链接及真实个人数据。
