# AI 协作说明 — 本项目使用 Leistd 框架

> 本文件是 AI 编码助手的入口。本项目基于 **Leistd .NET DDD 框架**（`Leistd.*` NuGet 包）构建。用到任何框架组件前，先读本文件定位到对应用法文档，**不要凭记忆臆造框架 API**。

## 框架版本

框架包版本由 `backend/Directory.Build.props` 的 `LeistdFrameworkVersion` 单点声明。查具体 API 时以该版本对应的文档/源码为准。

## 本项目引用的 Leistd.* 组件

下表列出本项目各层引用的框架包及其能力定位。**用法/公共 API/DI 注册以下方"用法文档"为准。**

### Domain 层
| 包 | 能力 |
| --- | --- |
| `Leistd.Ddd.Domain` | DDD 领域基类型：Entity、聚合、仓储接口、本地事件、数据过滤 |
| `Leistd.Exception.Core` | 业务异常基类型（`BusinessException` 等） |
| `Leistd.Lock.Core` | 锁抽象 |
| `Leistd.Security.Core` | 当前用户/身份抽象（`ICurrentUser`） |

### Application 层
| 包 | 能力 |
| --- | --- |
| `Leistd.Ddd.Application` / `Leistd.Ddd.Application.Contracts` | AppService 基类、`PagedResultDto`、DTO 约定 |
| `Leistd.ObjectMapping.Mapster` | 对象映射（Mapster） |
| `Leistd.Security.Core` / `Leistd.Authorization.Core` | 当前用户、权限检查/定义 |

### Infrastructure 层
| 包 | 能力 |
| --- | --- |
| `Leistd.Ddd.Infrastructure` | EF Core 仓储、工作单元事件拦截器、全局过滤器 |
| `Leistd.Auditing.EntityFrameworkCore` | 审计字段/软删自动填充（EF 拦截器） |
| `Leistd.Authorization.EntityFrameworkCore` | 权限授予的 EF 存储 |
| `Leistd.EventBus.Local` | 进程内事件总线 |
| `Leistd.Lock.Redis` / `Leistd.Lock.Memory` | 分布式锁 / 本地锁实现 |
<!--#if (IncludeNotifications)-->
| `Leistd.Notifications.EntityFrameworkCore` | 通知持久化存储 |
<!--#endif-->

### Api 层
| 包 | 能力 |
| --- | --- |
| `Leistd.Exception.AspNetCore` | 全局异常处理中间件 |
| `Leistd.DependencyInjection.DynamicProxy` | AOP 动态代理织入 |
| `Leistd.Authorization.AspNetCore` | 权限策略集成 |
| `Leistd.Security.AspNetCore` | HttpContext 当前用户 |
| `Leistd.Tracing.AspNetCore` / `Leistd.Tracing.HttpClient` | 链路追踪 |
<!--#if (IncludeNotifications)-->
| `Leistd.Notifications.Core` / `Leistd.Notifications.AspNetCore.SignalR` | 通知发布 + SignalR 推送 |
| `Leistd.RealTime.AspNetCore.SignalR` | 实时业务事件 / 在线状态 |
<!--#endif-->

## 用法文档去哪查

> **推荐先装框架用法索引 Skill**：`npx skills add zengqinglei/leistd-net`（跨 Claude Code / Cursor / Codex）。装后它会帮你按「能力→组件→随包文档」定位，并带一份「框架未覆盖能力」防臆造清单。未装也可按下面手动定位。

框架组件文档随 NuGet 包分发。查某个 `Leistd.Xxx` 包的用法：

1. **NuGet 缓存中的随包文档**（离线可用，与已安装版本一致）：
   `<全局包缓存根>/<小写包名>/<版本>/docs/` 下的 Markdown（如 `.../leistd.unitofwork.core/<版本>/docs/unit-of-work.md`）。缓存根用 `dotnet nuget locals global-packages --list` 解析（默认 `~/.nuget/packages/`，可能被环境变量改写）。
2. **包内 XML 文档**（精确 API 签名）：与 dll 同目录的 `.xml`（IDE/编译器可读；AI 需按需查阅对应 `.xml`）。
3. **在线文档**：框架仓库 `framework/docs/components/` 与 `framework/docs/ddd-struct/`（`PackageProjectUrl` 指向的仓库）。

**优先读随包文档（手段 1），它与你项目实际安装的版本对应，不会版本漂移。**

## 约束

- 不臆造框架 API：拿不准 `Leistd.*` 的方法名/签名/DI 注册时，先查上面的用法文档或包内 `.xml`，再写代码。
- 遵循本项目 `docs/standards/`（若存在）声明的工程规范与工作流。
- 高风险动作（生产部署、数据迁移、密钥变更）需人工确认。
