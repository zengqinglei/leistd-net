---
name: using-leistd-net-framework
description: |
  在框架仓（leistd-net）内消费或修改 Leistd.* 组件（通用组件 components + DDD 基座 ddd-struct）前使用，作为框架知识的发现入口。

  使用时机：
  (1) 需要用某个 Leistd.* 组件（锁/事件总线/工作单元/审计/权限/通知/实时/异常/追踪/安全…）或 DDD 基座类型（Entity/仓储/AppService/PagedResultDto）时
  (2) 不确定组件的公共 API、DI 注册方法、配置项或运行时语义时
  (3) 用户自然语言：「怎么用 XX 组件」「这个组件有哪些 API」「DDD 基座怎么用」

  不适用：项目治理工作流（用 using-leistd-workflow）；通用工程方法（TDD/头脑风暴/代码审查方法论等）。
metadata:
  openclaw:
    requires: []
    skillKey: "using-leistd-net-framework"
user-invocable: true
disable-model-invocation: false
---

# 框架知识入口 (using-leistd-net-framework)

> 本 skill 是 Leistd .NET 框架（通用组件 + DDD 基座）的知识发现入口。用/改任何 `Leistd.*` 组件前先读它，再按需读对应组件文档——**不要凭记忆臆造 API**。

## 唯一事实源

组件用法的权威文档在 `framework/docs/`，按源码撰写、逐一核对公共 API。**文档缺失或存疑时，先读源码 `framework/components/<家族>/**/*.cs`（含 XML 注释与公共类型），再按 `framework/docs/_doc-template.md` 骨架补文档，不要凭空回答。** 新增/修改组件的规范见 `framework/docs/development-guide.md`。

## 修改组件前置原则

在 `framework/components` 或 `framework/ddd-struct` 内动手前，先按以下原则自检：

- **依赖方向**：`components` 不依赖 `ddd-struct`；`*.Core` / `Leistd.Ddd.Domain` 不依赖 Web、EF、Castle 等具体实现；DDD 内部保持 `Domain ← Application(.Contracts) ← Infrastructure`。
- **组件闭环**：每个家族只做自己的职责，不替其它组件注册基础设施、映射端点或隐式挂载拦截器；跨组件组合由宿主显式调用各自的 `Add*` / `Map*`。
- **实现分层**：抽象放 `.Core`；ASP.NET Core 放 `.AspNetCore*`；EF Core 放 `.EntityFrameworkCore` / `.EfCore`；SignalR 放 `.AspNetCore.SignalR`。
- **目录与命名**：服务注册文件统一 `DependencyInjection.cs`；实体放 `Entities`；Options 放 `Options`；DTO 使用 `XxxInputDto` / `XxxOutputDto`；EF 持久化实现优先使用 `XxxStore` / `XxxManager`。
- **身份读取**：业务/组件优先使用 `ICurrentUser` / `ICurrentPrincipalAccessor` 等统一抽象，避免散落 `ClaimsPrincipal user`、`Context.User!` 或重复用户抽象。
- **文档示例自包含**：组件文档示例只能使用该组件真实引用的类型 + 原生 .NET/EF Core；DDD 分层示例归 `framework/docs/ddd-struct/ddd-struct.md`。

框架组件改动后，必须同步审视 `template/backend`、`template/frontend`、`template/docs` 是否需要适配；模板是组件组合的端到端示范，不应与框架 API 漂移。

## 通用组件（components）

| 组件 | 定位 | 文档 |
| --- | --- | --- |
| aop | 动态代理拦截器基类 | `framework/docs/components/aop.md` |
| auditing | 审计（创建/修改/软删自动填充） | `framework/docs/components/auditing.md` |
| authorization | 权限授权（定义/检查/策略/存储） | `framework/docs/components/authorization.md` |
| core | 核心原语（时钟、通用异常） | `framework/docs/components/core.md` |
| dependency-injection | 服务注册回调 + 动态代理织入 | `framework/docs/components/dependency-injection.md` |
| event-bus | 事件总线（进程内本地实现） | `framework/docs/components/event-bus.md` |
| exception | 业务异常与全局异常处理 | `framework/docs/components/exception.md` |
| lock | 分布式锁与本地锁 | `framework/docs/components/lock.md` |
| notifications | 站内通知：持久化的用户可读通知（推给指定用户/分组）。**非**通用事件推送——那用 realtime | `framework/docs/components/notifications.md` |
| object-mapping | 对象映射（Mapster/AutoMapper） | `framework/docs/components/object-mapping.md` |
| realtime | 实时通信：业务资源事件订阅 + 在线状态。**非**用户可读通知——那用 notifications | `framework/docs/components/realtime.md` |
| response | 统一 API 响应 | `framework/docs/components/response.md` |
| security | 当前用户与身份信息 | `framework/docs/components/security.md` |
| tracing | 链路追踪 | `framework/docs/components/tracing.md` |
| unit-of-work | 工作单元与事务 | `framework/docs/components/unit-of-work.md` |

组件依赖关系图见 `framework/docs/components/README.md`（仅标注真实 `ProjectReference` 边）。

## DDD 基座（ddd-struct）

| 内容 | 文档 |
| --- | --- |
| Domain / Application / Application.Contracts / Infrastructure 四层基座（Entity、仓储、AppService、PagedResultDto、事件拦截器、全局过滤器）。**注意**：审计/事件拦截器需在配置 DbContext 时 `AddInterceptors` 显式挂载，非自动生效 | `framework/docs/ddd-struct/ddd-struct.md` |

## 能力跨多个家族时的分工

某些能力横跨多个包，按下表选择落点，**不要只看单个组件名**：

| 能力 | 用哪个 | 说明 |
| --- | --- | --- |
| 领域事件（聚合根状态变更） | **ddd-struct** | 实体内 `AddLocalEvent(...)`，保存时由拦截器发布。⚠️ 拦截器需在配置 DbContext 时 `AddInterceptors` **显式挂载**，否则静默不发布 |
| 应用层事件（流程主动派发） | **event-bus** | 直接 `IEventBus.PublishAsync(...)` |
| 软删除字段填充 | **auditing** | `ISoftDelete` + 审计拦截器写入删除字段 |
| 软删除后自动过滤查询 | **ddd-struct** | 由全局查询过滤器实现 |
| 事务边界/一致性 | **unit-of-work** + **ddd-struct** | `[UnitOfWork]` AOP 定界，ddd Infrastructure 协同提交 |

## 本框架未覆盖的能力（勿臆造 Leistd API）

以下常见能力**本框架不提供实现**，不要误挂到相近组件、也不要臆造 `Leistd.*` API——请另选方案：

- **幂等 / 防重复提交**：无此组件（`lock` 是分布式锁，非幂等键去重）。
- **限流 / 熔断**：无此组件（不在 `authorization` 内）。
- **跨进程 / 分布式事件、消息队列**：`event-bus` 仅进程内本地实现。
- **缓存抽象**：无此组件。

> 找不到对应能力时先确认它是否在此清单；宁可显式说明"框架未覆盖、建议用 X"，也不要臆造不存在的 Leistd 组件或 API。

## 与其它 skill 的边界

- **项目治理工作流**（需求→开发→审查→测试→部署→验收）：用 `using-leistd-workflow` 及其阶段 skill，不在本 skill 范围。
- **框架仓开发流程**：先用 `using-leistd-workflow` 确认需求/开发/审查/测试闭环；一旦涉及 `framework/components`、`framework/ddd-struct` 或 `Leistd.*` API 边界，再回到本 skill 查组件事实源。
- **通用工程方法**（TDD/头脑风暴/代码审查方法论）：不在本 skill 范围，按你现有的通用工程实践处理。
- 本 skill 只负责"框架组件/基座怎么用、去哪查"，是知识发现入口，不执行开发流程。

## 最小验证

组件代码或文档改完后，优先运行与改动匹配的最小验证：

```bash
dotnet build framework/Leistd.Framework.slnx -c Release
dotnet pack framework/Leistd.Framework.slnx -c Release -o framework/artifacts
pwsh framework/build/check-docs-sync.ps1
pwsh framework/build/check-docs-api-drift.ps1
```

未执行的验证必须说明原因；不要把未运行写成已通过。
