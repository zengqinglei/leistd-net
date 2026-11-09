# 跨服务共享设置

状态：已完成 · 风险档：中 · 基线：develop `b21cf8b1` · 关联：[#257](https://github.com/zengqinglei/leistd-net/pull/257)，`2.0.0-beta.272`

## 1. 需求

下游项目有一类设置由一个服务管理、其他服务只读：例如身份服务管的二次验证开关与时长，资源服务在各自的端点上要按本租户的值校验。现状是设置组件只读本地存储，每多一项共享就要在提供方加一个内部端点、在使用方写一套缓存与回源（模板账户偏好缓存约 480 行）。

目标：提供方声明哪些设置共享并映射一个端点；使用方用一个独立的只读读取器按租户读取，缓存、条件请求、故障时用旧值、读不到时用使用方声明的兜底值都由组件完成。

已裁决（leistd 侧用户，2026-11-09）：
- 机器身份被拒（取令牌被拒、401、403）时清掉旧值、记 Error，返回使用方声明的兜底值，并带来源标记，不抛异常。
- 不做变更通知（反馈 S3）：读取的正确性不依赖推送，新鲜期加 304 已够；需要秒级生效的设置出现时再做。
- 模板不加演示设置（反馈 S4）：模板 Identity 的租户设置资源服务都用不上，硬造会被生成项目照抄；行为测试放在框架组件测试里，模板规范只写何时用共享设置。

非目标：
- 不改 `ISettingProvider` 的本地读取语义，读取器不并入它。
- 不迁移模板账户偏好缓存：那是按人、可写、前台要读到最新值的另一种语义。
- 不抽出与 `TenantRouteCache` 共用的缓存基元：两处在不同包里，规则已由「Identity 不可达时用旧值」回执统一。

验收：
- 定义：Given 设置标记 `IsShared` 且为机密、进程级或不允许租户层；When 首次访问定义；Then 抛 `InvalidOperationException` 并列出设置名与原因。
- 提供方端点：
  - Given 宿主映射 `MapSharedSettings` 并指定策略；When 以满足策略的主体 `GET /{租户}`；Then 只返回 `IsShared` 设置在该租户的生效值（租户值，没有时为代码默认值），带强 ETag；`If-None-Match` 命中时回 304。
  - Given 主体不满足策略；Then 401/403。Given 租户段不是 `host` 也不是有效租户（注册了 `ITenantStore` 时要求租户存在且启用）；Then 404。
  - Given 未指定策略；Then 映射时抛出。
- 使用方读取器：
  - Given 已登记提供方与每项设置的兜底值；When 新鲜期内重复读取；Then 只回源一次；同键并发回源合并为一次；过期后带 `If-None-Match` 重验证，304 时续用原值。
  - Given 提供方暂不可达（传输失败、超时、408/429/5xx）且有旧值；Then 返回旧值、来源为 `Stale`；有旧值时单次回源最多等 1 秒，超时先用旧值，同键节流 30 秒，每个节流周期一条 Warning。旧值超过保留上限后不再使用。
  - Given 暂不可达且没有旧值；Then 返回兜底值、来源为 `Fallback`。
  - Given 机器身份被拒或其他明确失败；Then 清掉旧值，记 Error，返回兜底值；之后再遇暂时故障也不恢复旧值。
  - Given 读取未登记的提供方或未声明的设置名；Then 抛 `InvalidOperationException`。
  - Given 提供方的响应里没有某项已声明的设置（对方没共享）；Then 该项返回兜底值。

## 2. 方案

现状：
- 设置定义在 `Leistd.Settings.Core/Definitions`，`IsVisibleToClients` 一类的标记是定义上的可写属性；`ISettingDefinition` 只有组件内部实现。
- 解析回落为用户 → 租户 → 代码默认值；匿名只到租户层。存储 `ISettingStore` 经当前工作单元读，连接归属由工作单元决定，所以切换租户后要开新工作单元（模板 `GetForTenantUserAsync` 同一写法）。
- 多租户的 `TenantRouteCache` 已按统一规则实现：HybridCache 只用进程内一级、负责单飞；有旧值时等待预算 1 秒、节流 30 秒；明确拒绝清旧值。
- 服务客户端要求每个客户端一个具体选项类型；认证与弹性管道由宿主在返回的构建器上组合，弹性管道先挂、在外层。

设计：

| 层 | 包 | 内容 |
| --- | --- | --- |
| 定义 | `Leistd.Settings.Core` | `ISettingDefinition.IsShared`；定义管理器在 `PostDefine` 之后校验：共享设置不能是机密、不能是进程级、必须允许租户层。响应形状 `SharedSettingsDto`（`Values`：设置名 → 值） |
| 提供方 | `Leistd.Settings.AspNetCore` | `MapSharedSettings(options => options.Policy = ...)` 映射 `GET /{tenant}`。租户段 `host` 或租户 Id；在该租户下开新工作单元，按存储里的租户层原始值、没有时代码默认值组装。ETag 取按名称排序后响应体的 SHA-256 前 16 字节 |
| 使用方 | 新包 `Leistd.Settings.ServiceClient` | `AddSharedSettingsClient<TOptions>(provider, declare, configure?, configSectionPath?)` 返回 `IHttpClientBuilder`；`ISharedSettingReader.GetAsync(provider, name)` 返回 `SharedSettingValue(Value, Source)`，`GetAsync<T>` 按不变文化转换 |

- 提供方只读存储与定义，不经 `ISettingProvider`：共享设置不可能有用户层、进程级或密文，"存储的租户值 ?? 代码默认值"就是完整的回落，也不受调用方主体是否被识别为用户的影响。
- 使用方选项 `SharedSettingsClientOptions : ServiceClientOptions`：`Path`（默认 `api/v1/internal/shared-settings`）、`FreshFor`（默认 1 分钟）、`MaxStaleness`（新鲜期结束后可用旧值的时长，默认 1 小时，0 表示不用旧值）。宿主为每个提供方派生一个具体类型，与服务客户端约定一致。
- 缓存按（提供方，租户）存整份；HybridCache 进程内一级、单飞；结构照 `TenantRouteCache`，增加两点：回源带上一次的 ETag；没有旧值或明确失败时不抛出，交回"无值"，由读取器按每项兜底值返回。
- 机器认证与弹性管道由宿主在返回的构建器上组合（先 `AddStandardResilienceHandler()`、再 `AddClientCredentials()`），组件不代为选择。
- 不另设授权约定：端点策略由宿主指定，组件不内置默认策略，与 `MapSettings` 一致。
- 读取器为 Scoped，当前租户取自调用方作用域（宿主可替换 `ICurrentTenantAccessor`）；缓存与回源是单例，用根容器解析客户端，回源可能在调用方请求结束后继续。
- 兼容：`ISettingDefinition` 是公开接口，新增 `IsShared` 后外部实现须补该属性并重新编译，按破坏性变更提交（`feat(settings)!:`，`BREAKING CHANGE:` 脚注），不保留兼容层；其余为新增 API 与新包。发框架 beta。

## 3. 计划

1. Core：`IsShared`、定义校验、`SharedSettingsDto`；单测覆盖三种非法组合。
2. AspNetCore：`MapSharedSettings` 与选项；TestServer 单测覆盖只返回名单、生效值、ETag/304、策略、租户 404。
3. 新包：注册入口、选项与校验、读取器、缓存；单测用假处理器覆盖新鲜期、单飞、304、旧值与节流、保留上限、明确失败清旧值、兜底值、未声明名称。
4. 随包文档 `settings.md`、模板规范何时用共享设置；slnx 与测试项目引用。
5. 构建（警告即错误）、设置测试项目全量、仓库静态检查、打包与隔离消费；送 Codex 审查；PR、合并、发 beta，回执。
