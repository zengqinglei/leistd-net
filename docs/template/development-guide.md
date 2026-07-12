# 模板开发规范

## 1. 定位

`template/` 是可发布的 `dotnet new` 项目模板，也是 Leistd.* NuGet 包的参考消费端。模板维护需要同时保证源模板正确、条件裁剪正确以及生成结果可构建运行。

## 2. 依赖方向

- `Domain` 保存业务模型和内层抽象，不依赖 Application、Infrastructure 或 Api。
- `Application` 编排用例并依赖 Domain，不依赖 Infrastructure。
- `Infrastructure` 实现持久化和外部适配器并依赖 Domain；需要实现 Application 抽象时可依赖单独的 Contracts 项目，当前模板未拆该项目。
- `Api` 是组合根，可同时引用 Application 和 Infrastructure，并负责服务注册与端点映射。
- 模板只通过 `PackageReference` 消费框架包，本地联调也使用 `.tmp/local-feed`。

## 3. 条件生成

修改条件化功能前先读 `template/.template.config/template.json`。条件代码、文件排除、项目引用、DI、前端路由、Mock 和文档必须作为一个场景整体调整，生成后不得残留 `<!--#if`、`<!--#endif` 或模板占位符。

标准场景矩阵：

| 场景 | 参数重点 | 目的 |
| --- | --- | --- |
| `default` | 默认参数 | 主路径 |
| `minimal` | `IncludeIdentity=false` | 最小裁剪 |
| `no-roles` | `IncludeRoles=false` | 认证但无角色权限 |
| `notifications` | `IncludeNotifications=true` | 通知与实时组合 |
| `no-openiddict` | `IncludeOpenIddict=false` | 无开放授权服务 |
| `external-login` | `IncludeExternalLogin=true` | 外部登录适配 |

## 4. Skill 与规范

- `template/.agents/skills/leistd-project-workflow/` 是跨工具项目协作入口，不依赖 `CLAUDE.md`、`AGENTS.md` 或其他工具专属文件。
- 单一 `SKILL.md` 根据用户最终意图路由规划、实现、审查、测试、协调和部署，并持有对应完成责任；场景细节按需从一层 `references/` 加载。
- `template/docs/README.md` 是生成项目唯一文档索引，`docs/standards/` 只保存工程事实，不重复 Skill 流程。
- Skill 安装后即使项目没有文档，也必须从源码、配置、测试和 CI 继续低风险任务；只有产生长期可复用信息时才按需创建最小权威文档。
- 不携带固定需求、规范或报告模板，不预建按需目录。
- 修改任何 Skill 时使用官方 `skill-creator` 并运行 `scripts/validate-skills.ps1`。

## 5. 本地框架联调

先在仓库根目录打包，再由模板矩阵通过一次性 NuGet 配置消费：

```powershell
dotnet pack framework/Leistd.Framework.slnx -c Release -o .tmp/local-feed
pwsh scripts/test-template-matrix.ps1 -SkipPack
```

不在仓库 `NuGet.Config` 或生成项目中固化本地源。

## 6. 数据库初始化

- 模板不预置迁移；所有环境启动时有迁移则执行 `MigrateAsync`，无迁移则执行 `EnsureCreatedAsync` 自动创建数据库。
- `EnsureCreatedAsync` 不建立迁移历史。计划持续演进结构的数据库必须在首次启动前包含初始迁移，或在后续启用迁移时明确重建/基线方案。
- 修改初始化策略时同步 `Program.cs`、生成项目 README、部署说明和模板场景断言，避免 AI 重复应用迁移，并确保生产部署把应用启动视为数据库变更操作。

## 7. 验证

```powershell
pwsh scripts/validate-skills.ps1
pwsh scripts/test-template-matrix.ps1
```

模板场景统一生成到 `.tmp/generated-template/`，本地 NuGet 包统一输出到 `.tmp/local-feed`，还原缓存隔离在 `.tmp/nuget-packages`。脚本会在 `.tmp` 生成一次性 NuGet 配置，不修改仓库或用户配置，从而避免同版本全局缓存掩盖当前源码包。CI 发布目录仍使用 `framework/artifacts`。

重复调试同一份本地包时可组合使用 `-SkipPack -ReusePackages`；框架包内容变化后不得复用缓存，必须重新 pack 和 restore。
