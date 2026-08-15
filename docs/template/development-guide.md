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
| `default` | 默认参数 | 主路径 |
| `minimal` | `IncludeIdentity=false` | 最小裁剪 |
| `no-roles` | `IncludeRoles=false` | 认证但无角色权限 |
| `tenancy` | `IncludeTenancy=true` | 多租户主路径 |
| `tenancy-illegal` | `IncludeTenancy=true` + `IncludeRoles=false` | 非法参数组合整体不生成租户能力 |
| `tenancy-external-login` | `IncludeTenancy=true` + `IncludeExternalLogin=true` | 外部身份按租户分区 |
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
- 前端 UI 走 Spartan UI：选型依据与主题/能力取舍见 [`docs/architecture/frontend-ui-library.md`](../architecture/frontend-ui-library.md)，组件用法规范见 [`template/docs/standards/coding-frontend.md`](../../template/docs/standards/coding-frontend.md)；改前端时按「`spartan` skill（`.agents/skills/spartan/`，含 `rules/`）→ 本地 `libs/ui` 源码 → 官方文档」确认组件 API，不臆造 Helm/Brain API。`@spartan-ng/mcp` 是仓库维护者的可选工具（根 `.mcp.json`），模板不内置。

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

每次运行使用独立的 run 目录 `.tmp/runs/<run-id>/`（`<run-id>` = PID+时间戳），其下含 `generated-template/`（模板场景生成）、`local-feed/`（本地 Leistd 包，每 run 独立 pack）、`template-hive/` 与一次性 NuGet 配置——各 run 自包含、互不写对方目录，因此**多个 AI/终端可并行执行**。第三方 NuGet 包缓存跨 run 共享、只读复用于 `.tmp/nuget-cache`（按 (id,version) 内容不可变，并发安全，避免每轮重下近 1GB 依赖闭包）；restore 前脚本会定点清除该缓存里的 `Leistd.*`，强制重新解包当前源码包，避免同版本全局缓存掩盖改动。启动时只清理超过 2 小时未活动且非当前 run 的旧目录（据 `.run.lock` 判活），绝不删正在运行的 run。CI 发布目录仍使用 `framework/artifacts`。

重复调试同一份已 pack 的本地包时可用 `-SkipPack`（复用当前 run 目录已有的 `local-feed`）；框架包内容变化后必须重新 pack（去掉 `-SkipPack`）。
