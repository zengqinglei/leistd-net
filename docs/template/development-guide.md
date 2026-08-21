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

**前端条件块的空行放在块内部**：紧跟 `//#if` 写内容，空行留在 `//#endif` 之前。空行放在标记外侧时，开启与关闭两种形态里必有一种产生双空行或块首空行，prettier 会判 lint 失败——而只跑其中一种场景发现不了。因此条件块的格式必须按开、关两个场景分别验证。

标准场景矩阵：

| 场景 | 参数重点 | 目的 |
| --- | --- | --- |
| `identity` | `ServiceRole=Identity` | OIDC Server、租户控制面、Identity 业务库与完整前端 |
| `resource` | `ServiceRole=Resource` | 远程 OIDC 验证、本服务授权、动态租户连接与完整前端 |
| `identity-notifications` | Identity + `IncludeNotifications=true` | Identity 可选通知切片 |
| `resource-notifications` | Resource + `IncludeNotifications=true` | Resource 可选通知切片 |
| `identity-external-login` | Identity + `IncludeExternalLogin=true` | Identity 外部登录适配；Resource 不提供该参数 |
| `identity-localization` | Identity + `IncludeLocalization=true` | Identity 本地化切片 |
| `resource-localization` | Resource + `IncludeLocalization=true` | Resource 本地化切片 |

`ServiceRole`只有 `Identity|Resource`。旧的 `IncludeIdentity`、`IncludeRoles`、`IncludeOpenIddict`、`IncludeTenancy`和 `TenancyEnabled` 不是兼容入口，不得重新引入。

## 4. Skill 与规范

- `template/.agents/skills/leistd-project-workflow/` 是跨工具项目协作入口，不依赖 `CLAUDE.md`、`AGENTS.md` 或其他工具专属文件。
- 单一 `SKILL.md` 根据用户最终意图路由规划、实现、审查、测试、协调和部署，并持有对应完成责任；场景细节按需从一层 `references/` 加载。
- `template/docs/README.md` 是生成项目唯一文档索引，`docs/standards/` 只保存工程事实，不重复 Skill 流程。
- Skill 安装后即使项目没有文档，也必须从源码、配置、测试和 CI 继续低风险任务；只有产生长期可复用信息时才按需创建最小权威文档。
- 不携带固定需求、规范或报告模板，不预建按需目录。
- 修改任何 Skill 时使用官方 `skill-creator` 并运行 `scripts/validate-skills.ps1`。
- 前端 UI 走 Spartan UI：选型依据与主题/能力取舍见 [`docs/architecture/frontend-ui-library.md`](../architecture/frontend-ui-library.md)，组件用法规范见 [`template/docs/standards/coding-frontend.md`](../../template/docs/standards/coding-frontend.md)；改前端时按「`spartan` skill（`.agents/skills/spartan/`，含 `rules/`）→ 本地 `libs/ui` 源码 → 官方文档」确认组件 API，不臆造 Helm/Brain API。`@spartan-ng/mcp` 是仓库维护者的可选工具（根 `.mcp.json`），模板不内置。

## 5. 本地框架联调

先在仓库根目录打包，再由模板矩阵通过一次性 NuGet 配置消费：

```powershell
dotnet pack framework/Leistd.Framework.slnx -c Release -o .tmp/local-feed
pwsh scripts/test-template-matrix.ps1 -SkipPack
```

需要人工观看前端测试运行时，改用有头 Chrome（CI 仍默认 `ChromeHeadless`）：

```powershell
pwsh scripts/test-template-matrix.ps1 -SkipPack -FrontendBrowser Chrome
```

不在仓库 `NuGet.Config` 或生成项目中固化本地源。

## 6. 数据库初始化

- 模板携带可审查的 EF Core 基线迁移；API 启动不执行 `MigrateAsync` 或 `EnsureCreatedAsync`。
- 每个服务的 `DbMigrator` 是一次性部署进程，先于 API 运行，使用 DDL 身份；API 只使用 DML 身份。
- 每个服务在所有物理数据库中使用自己的固定 schema 和迁移历史表。Identity 的租户/OIDC Control DbContext 固定连宿主 Control DB，不跟随租户路由。
- `ConnectionStrings:MigrationTarget` 只用于首次预迁移一个尚未登记的 Dedicated 物理目标；常规发布仍从 Identity 枚举已登记目标并去重迁移。
- 修改迁移策略时必须同步 DbMigrator、基线 migration、生成项目 README、部署说明与真实 PostgreSQL 闭环断言。

## 7. 验证

```powershell
pwsh scripts/validate-skills.ps1
pwsh scripts/test-template-matrix.ps1
pwsh scripts/test-template-postgresql-e2e.ps1 -SkipPack
```

第三条在真实 PostgreSQL 上验证本地 Framework NuGet 包→Identity/Resource 生成→DbMigrator→API→Shared/Dedicated 隔离的整条链路；它要求本机已安装 Docker、`psql` 和 PowerShell。

每次运行使用独立的 run 目录 `.tmp/runs/<run-id>/`（`<run-id>` = PID+时间戳），其下含 `generated-template/`、`local-feed/`、`template-hive/`、`nuget-cache/` 与一次性 NuGet 配置——生成物、包源和 `globalPackagesFolder` 都不跨 run 写入，因此**多个 AI/终端可并行执行**。不得共享解包目录后再“定点清理 Leistd.*”：本地包会在版本号不变时重新 pack，清理会在另一个并发 build 期间抽走 DLL。NuGet 自身的 HTTP 缓存仍会避免重复下载。启动时只清理超过 2 小时未活动且非当前 run 的旧目录（据 `.run.lock` 判活），绝不删正在运行的 run。CI 发布目录仍使用 `framework/artifacts`。

重复调试同一份已 pack 的本地包时可用 `-SkipPack`（读取共享的 `.tmp/local-feed`）；Framework 包内容变化后必须重新 pack，不得让旧的同版本包掩盖源码改动。
