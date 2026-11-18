# 缓存接口按数据性质选择

状态：已完成 · 风险档：中 · 关联：PR #293

## 1. 需求

现状：框架与模板共 6 处用 `HybridCache`，全部经 `DisableDistributedCache` 关闭二级缓存，只取进程内缓存与同键并发合并；令牌与含口令连接串能否留在本进程，取决于每个调用点都写对标志。微软对该标志的定位是按场景选择性关闭某层（[API 提案](https://github.com/dotnet/aspnetcore/issues/54647)），不是数据边界；`HybridCache` 是抽象类，替换实现是否遵守标志由实现决定；只用一级时仍按序列化复制（[dotnet/extensions#6063](https://github.com/dotnet/extensions/issues/6063)）。

用户于 2026-11-18 确认按数据性质选接口：

| 数据 | 接口 |
| --- | --- |
| 数据库或远端结果的缓存，丢失可重算，共享只是优化 | `HybridCache`；是否启用 Redis 由部署注册决定 |
| 必须留在本进程：凭据、令牌、含口令的连接串、不可序列化对象 | `IMemoryCache` |
| 必须多节点共享的短期状态：会话票据、挑战、验证码 | `IDistributedCache`；`HybridCache` 一级不跨节点失效，一次性状态会被重放 |

关闭层的标志只用于第一类条目的性能或场景取舍，并注释原因。第二类需要同键合并时在本类按键共享进行中的任务，可接受重复回源的不做合并。

验收：

- AC1 Given 部署注册了共享的 `IDistributedCache`，When 两个节点先后读取系统目录，Then 后一个节点不回源 Identity。
- AC2 Given 部署注册了 Redis，When 服务客户端缓存令牌或租户路由缓存连接串，Then 两者都不写入 `IDistributedCache`，且不依赖任何关闭标志。
- AC3 Given 第二类缓存同键并发未命中，When 回源，Then 只回源一次；续取、旧值回落与节流行为与改动前一致。
- AC4 框架开发指南与模板后端规范写明上述规则；模板规范读取成本不超过上限。

## 2. 方案

| 位置 | 归类 | 调整 |
| --- | --- | --- |
| 模板 `IdentitySystemDirectory` | 第一类，无保留在本机的理由 | 显式引用并注册 `HybridCache`，由部署决定是否启用二级缓存 |
| 模板 `ResourceWorkspaceTicketProvider` | 第二类 | 已用 `IMemoryCache`，不改 |
| 框架 `ServiceClient.OAuth` `TokenCache` | 第二类 | 改用 `IMemoryCache` 与本类同键合并 |
| 框架 `MultiTenancy.Core` `TenantRouteCache`、`RemoteConnectionStringResolver` | 第二类（连接串含口令） | 同上 |
| 框架 `MultiTenancy.ServiceClient` `CachedTenantDatabaseDirectory` | 第一类（只含指纹与租户 Id） | 与连接串共用 `TenantRouteCache`，用 `IMemoryCache` 减少逐库作业的秒级轮询开销 |
| 框架 `Settings.ServiceClient` `SharedSettingCache` | 第一类（机密设置不下发值） | 保留 `HybridCache` 并关闭二级，各节点按本地 ETag 重验证 |
| 模板 `IDistributedCache` 13 处 | 第三类 | 不改 |

保留公共 API。迁移令牌与租户路由缓存的组件使用 `AddMemoryCache`。同步框架随包文档（`service-client.md` 缓存一节、`multi-tenancy.md` 远端连接解析）与模板 `service-invocation.md` 交换令牌缓存的表述。

## 3. 计划

1. 更新开发指南和模板后端规范。系统目录启用可选的共享二级缓存，验证两个节点能通过同一分布式缓存共享结果。
2. 迁移令牌和租户路由缓存，验证同键合并、分布式缓存隔离、取消与真实到期。同步共享设置注释、组件文档和模板服务调用规范。
3. 构建、打包、验证候选包及两个模板场景。框架和模板一起交付到 PR #293，合入后随 develop 发框架 beta。

## 4. 结果

开发指南 §6.5 与 §6.7、模板后端规范 §3.11 已加规则，模板规范读取成本最高读数 33139（上限 34700）。系统目录去掉关闭标志；Resource 场景经 runner 在 worker-01 生成验证：系统目录单测 2/2 通过，恢复关闭标志时多节点共享用例失败（AC1 可证伪），还原后单元测试 175/175 通过。

令牌、连接串和物理库清单通过 `IMemoryCache` 保存在本进程。各调用方用自己的取消令牌结束等待。即使所有调用方都已取消，共享回源仍在客户端与弹性管道时限内继续，完成后移除进行中的任务。

框架构建、三个相关测试项目、候选包内容和隔离消费检查通过。分布式计数反证确认，令牌和连接串改走默认 `HybridCache` 写入时各触发一次分布式写入。Resource 和 Identity 工作区均以候选包通过后端构建及定向单测、集成测试。[PR #293 验证记录](https://github.com/zengqinglei/leistd-net/pull/293)
