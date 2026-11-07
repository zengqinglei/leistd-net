# ServiceClient 面向全部出站 HTTP：异常消息隐私与关联标识按客户端关闭

状态：进行中 · 风险档：高 · 关联：待开 PR

## 1. 需求

**背景**：下游项目要接入两个第三方（身份核验服务、筛查服务），咨询是否基于 ServiceClient 开发。框架侧答复（2026-11-08）：可以，ServiceClient 的传输故障分类、统一异常、配置绑定与返回的 `IHttpClientBuilder` 与对方是谁无关；但现状有两处缺口，组件文档也仍写着"服务间调用"。用户裁决：基于最佳实践终局决策，不留过渡态或兼容层。

缺口（develop `57d4555f`）：

- **异常消息带远端文本**：`HttpResponseMessageExtensions.CreateRemoteErrorAsync` 把远端 JSON 的 `message`/`detail`/`title`，或没有这些字段时的响应原文片段（≤4096 字符）拼进 `RemoteServiceException.Message`；信封失败拼入信封 `message`；读取与反序列化失败拼入内层异常消息；请求地址含完整查询串。未处理异常由全局处理器连同异常对象记 Error，`Message` 随之进日志。第三方响应可能回显姓名、证件号，内部服务的 detail 也可能回显输入，查询串常带被查询人的标识。
- **关联标识无法按客户端关闭**：`AddServiceClientPipeline` 在宿主注册了关联标识时对所有命名客户端转发，第三方客户端无法单独关闭。

**目标**：

1. 异常 `Message` 只含 HTTP 方法、不含查询串的请求地址、远端状态码、错误码（有则带）与远端 traceId（有则带）；结构化属性不变。
2. 命名客户端可关闭关联标识转发。
3. 组件文档面向全部出站 HTTP，补"第三方接入"写法。

**非目标**：不提供 API Key / Basic 等认证入口（各家差异只剩一行赋值，宿主自写 `DelegatingHandler`）；不在框架里关闭 W3C `traceparent`（官方 `DistributedContextPropagator` 负责）；不另出 Meter；路径段里的个人信息不处理（地址路径由调用方设计，查询串才是常见载体）。

| 要求 | 验收（Given / When / Then） | 覆盖 |
| --- | --- | --- |
| 非 JSON 正文不进消息 | Given 远端返回含哨兵值的 HTML，When 还原远端错误，Then `Message` 与 `ToString()` 不含哨兵，`ResponseBody` 含 | `RemoteErrorMessagePrivacyTests.A_non_json_error_body_stays_out_of_the_message_but_in_ResponseBody` |
| 远端说明不进消息 | Given JSON 的 `message`/`detail`/`title` 含哨兵（带或不带业务码），When 还原，Then `Message`、`ToString()` 不含哨兵；带业务码时 `Detail` 仍还原，traceId 与错误码在消息里 | `Remote_json_explanations_stay_out_of_the_message`（5 组） |
| 查询串与用户信息不进消息 | Given 请求地址查询串、片段或用户信息含哨兵，When 还原或传输失败，Then 消息不含哨兵、含方法与路径 | `The_query_string_stays_out_of_the_message`、`User_info_in_the_address_stays_out_of_the_message`、`A_transport_failure_message_leaves_out_the_query_string` |
| 信封说明不进消息 | Given 2xx 信封 `code != 0` 且 `message` 含哨兵，When `ReadResultAsync`，Then 消息含码不含哨兵，`Detail` 含哨兵 | `An_envelope_failure_message_stays_out_of_the_message` |
| 解析器消息不复制 | Given 正文不是合法 JSON，When 反序列化失败，Then 消息不含 `JsonException` 的消息，内层异常保留 | `A_deserialization_failure_does_not_copy_the_parser_message` |
| 默认转发 | Given 注册了关联标识，When 两个客户端发请求，Then 都带 `X-Correlation-Id` | `CorrelationIdForwardingOptionTests.Clients_forward_the_correlation_id_by_default` |
| 按客户端关闭 | Given 一个客户端经配置节或委托设 `ForwardCorrelationId=false`，When 发请求，Then 它不带关联标识头，另一个照常带 | `Disabling_forwarding_in_configuration_affects_only_that_client`、`Disabling_forwarding_in_the_delegate_affects_only_that_client` |
| 文档 | 组件文档标题、导语、何时使用、第三方接入、配置项与 tracing 同步 | 人工验收；`check-all.ps1` 的文档骨架、API 漂移、引用闸门 |

**已确认的决策**：三项方案由协调者按咨询答复定下，用户 2026-11-08 授权"终局自行决策"。

## 2. 方案

**现状证据**：`framework/components/service-client/Leistd.ServiceClient.Core/Http/HttpResponseMessageExtensions.cs`（`CreateRemoteErrorAsync`、`DeserializeAsync`、`EnsureEnvelopeSuccess`、`Describe`）；`Handlers/TransportFailureHandler.cs`；`DependencyInjection.cs` 中 `AddServiceClientPipeline` 的关联标识一环；全局处理器记录异常对象见 `framework/components/exception-handling/`。

**设计**：

- 新增内部 `RequestDescription.Of(request)`：方法 + `GetComponents(SchemeAndServer | Path)`（去掉查询串、片段与用户信息；相对地址截到 `?`/`#`）。响应侧消息统一为 `<方法> <地址> returned HTTP <状态>`，再按需追加 `; error code: …`、`; remote traceId: …` 或失败类别短语。传输失败沿用本地传输异常消息（不含载荷），只换地址写法。
- 读取、反序列化失败不再复制内层异常消息：`JsonException` 的消息含出错位置的载荷字符，解码异常会带出字节；内层异常照常作为 `InnerException`。
- `ServiceClientOptions.ForwardCorrelationId`（默认 `true`）：与 `BaseAddress` 一样经 `IOptions<TOptions>` 读取，首次读取后固定、配置重载不生效，为 `false` 时那一环换成已有的直通处理器，管道结构不变；经配置节或 `configure` 委托设置，同一命名客户端在进程内行为一致。
- 文档：`framework/docs/components/service-client.md` 改为"出站 HTTP 客户端"，新增「第三方接入」节（认证处理器、按租户凭据、非幂等重试、边界转换异常、日志脱敏、业务失败判断、指标）；`tracing.md` 第三方转发说明改指 `ForwardCorrelationId`。

**同类做法**：.NET 9 起 `IHttpClientFactory` 默认日志与 HttpClient 事件已脱敏查询串（[官方破坏性变更](https://learn.microsoft.com/dotnet/core/compatibility/networking/9.0/query-redaction-logs)），异常消息去掉查询串与运行时默认行为一致。OpenIddict 7.8 起默认不再记录远端响应原文（openiddict-core#2558），本组件的 OAuth 响应守卫已只记状态、类型与长度；本次把同一原则推广到业务调用的异常消息。

**备选与取舍**：

| 备选 | 取舍 |
| --- | --- |
| 加开关保留旧消息（如 `IncludeResponseInMessage`） | 不采用：用户要求不留兼容层；诊断原文已有 `ResponseBody`，开关只会让默认值之外的组合多一种泄露路径 |
| 消息里保留截断的远端说明 | 不采用：截断不改变数据性质，短片段同样可能是姓名或证件号 |
| `ForwardCorrelationId` 在发送时读 `IOptionsMonitor` | 不采用：客户端选项现以 `IOptions` 读取、首次后固定（基址同此），混用会让同一客户端的基址与转发在配置重载后不一致 |
| 按客户端关闭由宿主"不挂处理器"实现 | 不可行：处理器由 `AddServiceClientPipeline` 统一挂载，宿主无法摘除 |

**兼容与回滚**：`Message` 文本是诊断内容，不是契约，但依赖其文本的宿主断言需改用结构化属性（`Detail`、`ResponseBody`、`ErrorCode`）。新选项默认值保持原行为。回滚为还原三个提交。

## 3. 计划

| 任务 | 产物 | 验证 |
| --- | --- | --- |
| 异常消息隐私 | `HttpResponseMessageExtensions.cs`、`RequestDescription.cs`、`TransportFailureHandler.cs`、异常类型 XML；同步断言消息文本的既有测试 | 回归用例先失败后通过；`Leistd.ServiceClient.Tests` 全量 |
| 关联标识按客户端关闭 | `ServiceClientOptions.ForwardCorrelationId`、管道装配 | `CorrelationIdForwardingOptionTests` 先失败后通过；`Leistd.Tracing.Tests`、`Leistd.MultiTenancy.Tests` |
| 文档 | `service-client.md`、`tracing.md`、组件索引、`security.md` 链接文字；API 漂移白名单 | `check-all.ps1` 全过 |
| 包 | 打包到 `.tmp/local-feed` | `test-package-consumption.ps1 -PackageIds Leistd.ServiceClient.Core` |

**不做的事**：不改模板（模板不依赖这些异常消息文本，新选项默认值保持行为）；不提供认证入口、Meter 或 `traceparent` 开关。

## 4. 结果

本地分支 `zengqinglei/serviceclient-outbound-privacy`，三个实施提交：`fix(service-client)` 异常消息隐私、`feat(service-client)` `ForwardCorrelationId`、`docs(service-client)` 文档。尚未推送与开 PR。

- 回归用例在修复前 11 条全部失败（消息中可见哨兵值），修复后通过；`ForwardCorrelationId` 的两条关闭用例在接线前失败。
- 与计划的偏差：传输失败消息同样去掉查询串（同一泄露路径，判据一致）；用户信息一并从地址中去掉。
- 遗留：读取或反序列化失败时，内层异常（`JsonException` 等）的消息仍随异常对象的 `ToString()` 输出，可能带出个别载荷字符；按方案保留内层异常供诊断。
- 常驻文档已同步：`framework/docs/components/service-client.md`、`tracing.md`、`README.md`、`security.md`。
