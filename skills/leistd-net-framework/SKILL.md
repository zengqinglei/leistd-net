---
name: leistd-net-framework
description: |
  Use when writing or maintaining code that consumes the Leistd .NET DDD framework NuGet packages (Leistd.* — general-purpose components + the ddd-struct DDD base layer), to apply each component's real public API, DI registration, and runtime semantics correctly instead of guessing.

  Triggers:
  (1) Using a Leistd.* component (lock / event-bus / unit-of-work / auditing / authorization / notifications / realtime / exception / tracing / security / object-mapping …) or a DDD base type (Entity / repository / AppService / PagedResultDto).
  (2) Unsure of a component's public API, DI extension method, options, or behavior.
  (3) User says things like "how do I use Leistd's X", "what API does this component expose", "how does the DDD base layer work".

  Not for: generic engineering methodology; frameworks other than Leistd.
metadata:
  openclaw:
    requires: []
    skillKey: "leistd-net-framework"
user-invocable: true
disable-model-invocation: false
---

# Leistd .NET Framework — 组件用法索引

> 面向**消费 `Leistd.*` NuGet 包**的下游项目（人 + AI）。用/改任何 Leistd 组件前先读本索引定位到权威文档，**不要凭记忆臆造框架 API**。

## 文档在哪（按优先级）

组件用法文档**随 NuGet 包分发**，与你实际安装的版本一致。**注意：本 skill 自身目录下没有文档**——下表的 `docs/<家族>.md` 指的是 **NuGet 全局包缓存中、对应包目录内的 `docs/` 文件**，不是 skill 目录的相对路径。

**定位配方（家族 → 文件）**：
1. 找到你项目实际安装的、属于该家族的任一 `Leistd.<家族>.*` 包（该家族每个包都内置了同一份家族文档）。
2. **先解析 global-packages 缓存的真实根目录**（不要假设为 `~/.nuget/packages`——它可能被环境变量/配置改到别处）：
   ```bash
   dotnet nuget locals global-packages --list
   ```
   该命令输出**当前生效**的 global-packages 路径（形如 `global-packages: <根目录>`），已计入 `NUGET_PACKAGES` 环境变量、`NuGet.Config` 的 `globalPackagesFolder`、MSBuild `RestorePackagesPath` 等所有覆盖，优先级：`NUGET_PACKAGES` > `globalPackagesFolder` > 默认。
3. 用解析到的 `<缓存根>` 拼出家族文档路径并读取：
   `<缓存根>/<小写包名>/<版本>/docs/<家族>.md`
   - 例（lock 家族，装了 `Leistd.Lock.Memory`）：`<缓存根>/leistd.lock.memory/<版本>/docs/lock.md`
   - 例（DDD 基座，装了 `Leistd.Ddd.Domain`）：`<缓存根>/leistd.ddd.domain/<版本>/docs/ddd-struct.md`
   - `<版本>` 见项目的 `Directory.Build.props`（`LeistdFrameworkVersion`）或 `dotnet list package`。
   - **默认根（仅当未改缓存位置时）**：Windows `%USERPROFILE%\.nuget\packages\`、Mac/Linux `~/.nuget/packages/`——但请以步骤 2 的实际输出为准。

其它来源：
- **包内 XML 文档（精确签名）**：与 dll 同目录的 `<包名>.xml`（精确到方法签名）。
- **在线仓库文档**：`framework/docs/components/` 与 `framework/docs/ddd-struct/`（框架仓库 GitHub，`PackageProjectUrl` 指向）。

> 版本纪律：优先读**随包文档/包内 XML**，它们与你安装的版本严格对应；在线文档跟踪最新版，可能与旧版本包漂移。

## 通用组件（components）

下表列出家族及其文档文件名。文档位于**该家族任一已安装包**的缓存 `docs/<下表文件名>` 处（按上方配方定位）。

| 组件 | 定位 | 家族文档文件 | 家族包（任一即可）示例 |
| --- | --- | --- | --- |
| aop | 动态代理拦截器基类 | `docs/aop.md` | `Leistd.DynamicProxy` |
| auditing | 审计（创建/修改/软删自动填充） | `docs/auditing.md` | `Leistd.Auditing.Core` / `.EntityFrameworkCore` |
| authorization | 权限授权（定义/检查/策略/存储） | `docs/authorization.md` | `Leistd.Authorization.Core` / `.AspNetCore` / `.EntityFrameworkCore` |
| core | 核心原语（时钟、通用异常） | `docs/core.md` | `Leistd.Core` |
| dependency-injection | 服务注册回调 + 动态代理织入 | `docs/dependency-injection.md` | `Leistd.DependencyInjection` / `.DynamicProxy` |
| event-bus | 事件总线（进程内本地实现） | `docs/event-bus.md` | `Leistd.EventBus.Core` / `.Local` |
| exception | 业务异常与全局异常处理 | `docs/exception.md` | `Leistd.Exception.Core` / `.AspNetCore` |
| lock | 分布式锁与本地锁 | `docs/lock.md` | `Leistd.Lock.Core` / `.Memory` / `.Redis` |
| notifications | 站内通知：持久化的用户可读通知（推给指定用户/分组）。**非**通用事件推送——那用 realtime | `docs/notifications.md` | `Leistd.Notifications.Core` / `.EntityFrameworkCore` / `.AspNetCore.SignalR` |
| object-mapping | 对象映射（Mapster / AutoMapper） | `docs/object-mapping.md` | `Leistd.ObjectMapping.Core` / `.Mapster` / `.AutoMapper` |
| realtime | 实时通信：业务资源事件订阅 + 在线状态。**非**用户可读通知——那用 notifications | `docs/realtime.md` | `Leistd.RealTime.Core` / `.AspNetCore.SignalR` |
| response | 统一 API 响应 | `docs/response.md` | `Leistd.Response.Core` / `.AspNetCore` |
| security | 当前用户与身份信息 | `docs/security.md` | `Leistd.Security.Core` / `.AspNetCore` |
| tracing | 链路追踪 | `docs/tracing.md` | `Leistd.Tracing.Core` / `.AspNetCore` / `.HttpClient` |
| unit-of-work | 工作单元与事务 | `docs/unit-of-work.md` | `Leistd.UnitOfWork.Core` / `.EfCore` |

## DDD 基座（ddd-struct）

| 内容 | 家族文档文件 | 家族包（任一即可）示例 |
| --- | --- | --- |
| Domain / Application / Application.Contracts / Infrastructure 四层基座（Entity、仓储、AppService、PagedResultDto、事件拦截器、全局过滤器）。**注意**：审计/事件拦截器需在配置 DbContext 时 `AddInterceptors` 显式挂载，非自动生效 | `docs/ddd-struct.md` | `Leistd.Ddd.Domain` / `.Application` / `.Application.Contracts` / `.Infrastructure` |

## 能力跨多个家族时的分工

某些能力横跨多个包，按下表选择落点，**不要只看单个组件名**：

| 能力 | 用哪个 | 说明 |
| --- | --- | --- |
| 领域事件（聚合根状态变更） | **ddd-struct** | 实体内 `AddLocalEvent(...)`，保存时由拦截器发布。⚠️ 拦截器需在配置 DbContext 时 `AddInterceptors` **显式挂载**，否则静默不发布；见 `ddd-struct.md`「挂载拦截器」 |
| 应用层事件（流程主动派发） | **event-bus** | 直接 `IEventBus.PublishAsync(...)`；见 `event-bus.md` |
| 软删除字段填充 | **auditing** | `ISoftDelete` + 审计拦截器写入删除字段 |
| 软删除后自动过滤查询 | **ddd-struct** | 由全局查询过滤器实现；见 `ddd-struct.md` |
| 事务边界/一致性 | **unit-of-work** + **ddd-struct** | `[UnitOfWork]` AOP 定界，ddd Infrastructure 协同提交 |

## 本框架未覆盖的能力（勿臆造 Leistd API）

以下常见能力**本框架不提供实现**，不要误挂到相近组件、也不要臆造 `Leistd.*` API——请另选方案（如 ASP.NET Core 内建、专用库）：

- **幂等 / 防重复提交**：无此组件（`lock` 是分布式锁，非幂等键去重）。
- **限流 / 熔断**：无此组件（不在 `authorization` 内）。
- **跨进程 / 分布式事件、消息队列**：`event-bus` 仅进程内本地实现（其文档已明确标注）。
- **缓存抽象**：无此组件。

> 找不到对应能力时，先确认它是否在上表；宁可显式说明"框架未覆盖、建议用 X"，也不要臆造不存在的 Leistd 组件或 API。

## 使用约束

- 拿不准 `Leistd.*` 的方法名/签名/DI 注册时，先查上面的随包文档或包内 `.xml`，再写代码。
- 本 skill 是**知识索引**，不复述文档正文——权威内容以随包文档为准（单一来源）。
- 本 skill 只覆盖 Leistd 组件用法；通用工程方法（TDD、代码审查方法论等）不在本 skill 范围。
