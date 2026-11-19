# 租户感知的 HybridCache：键自带租户、回源恢复租户

状态：已完成 · 风险档：高 · 关联：下游反馈（HybridCache 回源回调中丢失当前租户）

## 1. 需求

### 问题

Microsoft.Extensions.Caching.Hybrid 10.11.0 在缓存未命中、调用方令牌 `CanBeCanceled` 时，用 `ThreadPool.UnsafeQueueUserWorkItem` 执行回源，不流转调用方的 `ExecutionContext`。源码见 `DefaultHybridCache.cs` 第 178–191 行和 `DefaultHybridCache.StampedeStateT.cs` 第 47–61 行。

Leistd 的当前租户、主体、链路标识、工作单元、数据过滤开关都存在 `AsyncLocal` 里，回调里全部读不到。下游 CRM 门户取品牌图因此在冷缓存时全部返回 500。

更危险的情形：按租户分库时，回调里的连接解析拿不到租户，可能静默连到宿主库。

### 已确认的事实（2026-11-19 查阅）

- **官方声明：** `AsyncLocal<T>` 随 `ExecutionContext` 流转；`UnsafeQueueUserWorkItem` 不把调用方的上下文带到工作线程。
- **实现细节，文档没写：** 令牌可以取消时，回源才排进线程池；不可取消时，回源在首个调用方的执行流里直接运行。`main` 分支写法相同。
- **上游立场：**
  - issue #5648 仍未关闭，微软 2026-10-07 回复：.NET 11 来不及修，考虑 .NET 12。
  - 作者在 #5568 的建议：在回调外取值；结果依赖它时，把它放进键。原因是同键请求会合并，没法回答"该用哪个请求的上下文"。
- **框架现状：**
  - `ScopeKey` 已规定租户外部标识的格式：租户为 `{租户 Id:N}:{key}`，宿主为 `host:{key}`。
  - `IAmbientContext.Capture/Restore` 用于后台队列，会恢复主体。
  - `SharedSettingCache` 在回调外取好租户，经路径参数显式传入，写法正确。
- **下游 Leistd 调用点：** 只有 CRM 的 `FoundationBrandVisualAssetStore` 需要改。完整核对结果见下游反馈文件 §6.7。

### 决定

- 新增 `TenantHybridCache`：它是 `HybridCache` 的子类，单独注册，不替换全局 `HybridCache`。
  - 进入时取好当前租户；
  - 键和标签按 `ScopeKey` 格式加租户段；
  - 回源时在回调外层执行 `ICurrentTenant.Change(租户 Id, 名称)`。
- **只恢复租户**，因为租户已经在键里：合并进来的调用方必然同租户，恢复对每个调用方都正确。
  - 主体、工作区、语言、链路标识、工作单元、数据过滤、`HttpContext` 都不恢复。恢复主体会把首个调用方的身份用在所有合并进来的调用方身上。
  - 结果依赖这些维度时，在外面取值、写进键、经 state 传入。
- 否决的方案及理由见下游反馈文件 §6.3：恢复完整上下文、强制传 `CancellationToken.None`、只靠约定或分析器、换 FusionCache。

### 验收标准

| 编号 | Given / When / Then |
| --- | --- |
| AC1 冷缓存回源 | Given 真实 `AddHybridCache()`、租户 A 作用域、可取消令牌；When 未命中时 `TenantHybridCache.GetOrCreateAsync`；Then 回调内 `ICurrentTenant.Id` 为 A。对照用例：直接用 `HybridCache` 走同一路径，回调内租户为空（证明走到了排队分支）。 |
| AC2 不串缓存 | Given 租户 A、B 和宿主；When 用同一逻辑键各自读取；Then 各回源一次，值互不相同。宿主落在 `host:` 分区，回调内租户为空。 |
| AC3 写与失效 | `SetAsync`、`RemoveAsync`（单键与多键）、`RemoveByTagAsync` 都只作用于当前租户。`RemoveByTagAsync("*")` 抛出 `NotSupportedException`，避免一次清掉全部租户。 |
| AC4 跟随上游 | 反射测试断言 `HybridCache` 的全部 abstract 与 virtual 成员都已重写。以后 .NET 新增重载时测试变红。 |
| AC5 规范 | `docs/framework/development-guide.md` §6.7、`framework/docs/components/multi-tenancy.md`、模板后端编码规范写明下文"规范"一项的规则。 |
| AC6 模板 | `IdentitySystemDirectory` 改用带 state 的重载，语言文化经 state 传入，回调不读环境上下文。 |

## 2. 方案

- **位置：** `Leistd.MultiTenancy.Core`。`HybridCache` 抽象类在 `Microsoft.Extensions.Caching.Abstractions` 里，该项目已经通过 `Microsoft.Extensions.Caching.Memory` 间接引用。是否显式加包引用，按仓库惯例决定。
- **注册：** `AddMultiTenancy()` 以 `TryAddSingleton` 注册 `TenantHybridCache`，构造时注入 `HybridCache` 和 `ICurrentTenant`。宿主仍需自己调用 `AddHybridCache()`，模板已经调用。
- **键与标签：** 复用 `ScopeKey` 的实现，不另写一套格式。
- **不做的事：**
  - 不恢复其他维度；
  - 不为"调用方取消后回源仍在用请求作用域里的服务"这类少见竞态加保护；
  - 不加分析器。
- **规范**（各写一段，不另开长篇）：
  - `development-guide.md` §6.7：回源回调可能在不带调用方上下文的线程里执行；租户数据用 `TenantHybridCache`；其他维度在外面取值、写进键、经 state 传入；回调里不发需要用户令牌的调用（Token Exchange）。
  - `multi-tenancy.md` 的"隔离数据库之外的标识"：`TenantHybridCache` 的用法、宿主分区、标签失效范围。`ScopeKey` 继续用于锁键、幂等键。
  - 模板后端编码规范：一条规则加一段示例。注意读取成本闸门，先看余量。
- **测试补充：** 给 `SharedSettingCache` 补一条可取消令牌下的冷缓存用例，证明它不依赖环境上下文。
- **发版：** 框架新增公共类型，合并后发 beta。下游通知由协调方负责。

## 3. 计划

| 任务 | 产物 | 验证 |
| --- | --- | --- |
| 封装 | `TenantHybridCache`、注册、XML 注释 | AC1–AC4 的单元测试，以及多租户组件的全部测试 |
| 规范与模板 | 三处文档、`IdentitySystemDirectory` | 文档检查、读取成本闸门、模板代表场景的后端测试 |
| 回归 | — | 框架全部测试、包内容与隔离消费检查、`check-docs-api-drift` |

## 4. 结果

与依赖升级在同一个 PR 交付。新增 `TenantHybridCache`，现有 `HybridCache` DI 注册及现有公开 API 的签名与调用契约保留。

- AC1 真实冷缓存加可取消令牌。对照用例中的原生 `HybridCache` 回调租户为空，租户缓存回调在挂起后仍读到调用时的租户 Id 与名称。
- AC2–AC3 同名键分别落在两租户和宿主分区，回源各一次。单键、多键、单标签和多标签失效按当前租户隔离，通配符会抛出 `NotSupportedException`。
- AC4 反射核对了 `HybridCache` 声明的全部可重写成员。新增缓存测试 6 例，多租户组件 447 例通过。共享设置的可取消冷缓存回源用例通过。
- AC5–AC6 已同步三处规范，文档 API 漂移和阅读成本闸门通过。模板 `IdentitySystemDirectory` 从 `state` 读取语言和 API，生成后的 Resource 与 Webhooks 形态完成还原、构建和后端测试。

框架 32 个测试项目、2,933 个用例通过，零失败、零跳过。82 个候选包通过包内容与隔离消费检查。生成的 `resource-api-webhooks` 完成候选源还原、Release 构建和 361 个后端用例，无 NU1605。
