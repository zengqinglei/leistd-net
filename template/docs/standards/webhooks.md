# Webhook 规范

把业务事件推给第三方系统（出站 webhook），同时遵循 [后端开发规范](./coding-backend.md)。组件的注册、签名与投递细节以 `webhooks` 组件随包文档为准（随包文档的定位见[后端说明](../../backend/README.md#leistd-框架-api)）；本篇写发布方的约定和交给接收方的接入说明。

## 1. 发布方

- **声明事件类型**：在 Application 的 `Webhooks` 目录里由 `IWebhookDefinitionProvider` 的实现登记名称、分组与说明词条。名称用 `资源.过去式`（如 `user.created`），一经对外就是契约；发布未声明的类型会抛出。`webhook.test` 由组件内置，不重复声明。
- **在用例的同一工作单元里发布**：注入 `IWebhookPublisher`，在写业务数据的用例里调用 `PublishAsync(eventType, data, ct)`。事件与投递记录随业务一起提交、一起回滚，提交后才发出请求。没有活动工作单元时抛 `InvalidOperationException`，不要为它另开事务。
- **事件处理器里发布**：处理器必须声明 `[UnitOfWorkEventHandler(UnitOfWorkPhase.BeforeCommit)]`；提交后阶段已没有可写的事务，发布会抛出。只为发 webhook 不新建领域事件，直接在用例里调用。
<!--#if (LocalIdentity)-->
- 示例：`UserAppService` 的创建用例发布 `user.created`，载荷只带用户 id。
<!--#endif-->

### 1.1 载荷与版本

- 请求体是信封 `{type, timestamp, data}`，`data` 在发布时冻结为 JSON，之后的重试与手动重发都发同一份。
- `data` 只放接收方需要的稳定字段（实体 id、状态、业务时间），不放密钥、口令、令牌与个人联系方式；接收方需要更多信息时凭 id 调本系统的 API 回查。
- 字段只增不改、不删。语义或结构要变时发布新的类型名（如 `order.paid.v2`），旧类型保留到订阅方迁走后再停发。

### 1.2 默认值与配置键

配置节 `Leistd:Webhooks`，都是部署配置，不进设置页；只写与默认值不同的项。

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `RetrySchedule` | `00:00:05, 00:05:00, 00:30:00, 02:00:00, 05:00:00, 10:00:00, 10:00:00` | 首发失败后的重试间隔（约 27 小时），实际时刻带 ±10% 抖动 |
| `RequestTimeout` | `00:00:15` | 单次请求超时 |
| `DispatchInterval` | `00:00:10` | 兜底扫描间隔；正常情况下提交后立即发出 |
| `AutoDisableAfter` | `5.00:00:00` | 连续失败多久后自动停用订阅 |
| `AllowedNetworks` | 空 | 放行的私网 CIDR；默认拒绝私网、回环与链路本地地址，开发环境配置放行回环 |
| `SecretRotationOverlap` | `1.00:00:00` | 轮换后旧密钥继续签名的时长 |
| `Retention:Enabled` / `Retention:Days` | `true` / `62` | 事件、投递与尝试记录的保留期 |

订阅停用（手动、端点返回 410 或连续失败）时，待投递的记录直接记为失败；停用期间发生的事件不会补发。重新启用时可选择把现存的失败投递按手动重发处理。

## 2. 接收方接入

交给订阅方的说明。请求按 [Standard Webhooks](https://www.standardwebhooks.com/) 签名，各语言的官方库可直接验签。

- **验签**：请求头 `webhook-id`、`webhook-timestamp`、`webhook-signature`；签名是用密钥（`whsec_` 前缀后的 Base64 解码值）对 `{webhook-id}.{webhook-timestamp}.{原始请求体}` 做 HMAC-SHA256，格式 `v1,<Base64>`。密钥轮换期间头里有多个签名（空格分隔），任一匹配即通过。按原始字节验签，不要先反序列化再序列化。
- **校验时间戳**：与本地时间相差超过 5 分钟就拒绝，防重放。
- **按 `webhook-id` 去重**：投递是至少一次，同一事件的自动重试与手动重发都沿用同一个 id。时间戳与签名每次尝试都重新生成，不能用来去重。
- **15 秒内返回 2xx**：先落库或入队再异步处理；超时、3xx（不跟随跳转）、4xx、5xx 都按失败重试。429 与 503 可带 `Retry-After`。
- **不依赖顺序**：同一资源的事件可能乱序到达，按载荷里的业务时间或凭 id 回查最新状态。
- **退订返回 410**：端点返回 `410 Gone` 时订阅立即停用，不再投递，需要管理员在后台重新启用。
