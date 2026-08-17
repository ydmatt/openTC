# START_HERE — openTC

任何 AI 或开发者继续 openTC（原 MYTC）前，按以下顺序读取：

1. [README.md](README.md)
2. [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md)
3. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
4. [docs/DATA_MODEL.md](docs/DATA_MODEL.md)
5. [docs/TASKS.md](docs/TASKS.md)
6. [PROJECT_MEMORY.md](PROJECT_MEMORY.md)
7. [docs/HANDOVER.md](docs/HANDOVER.md)

## 当前状态

- 品牌迁移版 `v1.0.25` 正在进行公开仓库预检，可发布为 win-x64 自包含绿色目录。
- Release 自动化测试 40/40 通过，STA WPF 冒烟测试及真实回收站删除/撤销还原测试通过。
- 可交互界面原型位于 `prototype/index.html`。
- 正式 WPF 解决方案位于 `MYTC.slnx`（保留旧文件名以兼容既有脚本），源码位于 `src/`，测试位于 `tests/`。
- 项目本地 .NET 10 SDK 位于被忽略的 `.tools/dotnet/`。
- 绿色包生成到被忽略的 `artifacts/release/`，不要把用户 `data/` 提交到仓库。

## 下一步

1. 用户日常试用 `v0.1.7` 并记录实际工作流反馈。
2. 修复日用问题后决定是否进入 `v0.2`。
3. 后续重点候选：文件系统自动刷新、Shell 图标与属性、更丰富布局、配置导入导出。
