# 模板：路径含机密的端点不让原始路径进入日志与追踪

状态：实施中 · 风险档：中（改宿主日志默认配置、遥测富化与三处框架日志字段） · 基线：develop `ccb2c464`（PR #263 之后） · 实施与端到端验收：Claude · 代码审查：Codex

## 1. 需求

下游服务有一类匿名回调，路径段本身就是认证材料：邮件服务商的事件回调地址是 `/api/v1/mail-receipts/{provider}/{routeKey}`，其中 `routeKey` 是随机机密。用 Serilog 时，业务靠富化器改写日志属性来脱敏；去掉 Serilog（PR #263）后，内置日志和 OpenTelemetry 都没有这种挂点。下游要求按 .NET / OpenTelemetry 官方做法给出模板级最佳实践：脱敏规则只写一处，同时覆盖控制台和 OTLP 两个通道，不过度设计。反馈来自下游开户线负责会话（2026-11-10）。

已核对的源码事实（ASP.NET Core 10）：

- 每条日志都带的 `RequestPath`/`RequestId` 作用域，只在 `Microsoft.AspNetCore.Hosting.Diagnostics` 类别启用时才创建，见 `HostingApplicationDiagnostics.BeginRequest`：先判断 `_logger.IsEnabled(LogLevel.Critical)`，为真才调用 `RequestScope`。同一类别下还有请求开始/结束日志，以及 `GenericWebHostService` 的启动异常日志。
- 模板的 `UseHttpLogging()` 排在 `UseRouting()` 之前，内置 HTTP 日志读取端点元数据（`HttpLoggingAttribute`）时端点还没选出。所以 `WithHttpLogging` 对模板的请求完成日志不生效；要按端点处理，只能放在拦截器的响应阶段，那时端点已经确定。
- ASP.NET Core 追踪的服务端 span 有 `url.path` 属性，取的是原始路径；span 名和 `http.route` 用的是路由模板。

适用边界：保护只在请求选中带声明的端点时生效。方法不符（例如对只收 POST 的敏感地址发 GET）时，路由可能选中框架的 405 拒绝端点，它不带声明，完成日志与追踪照常记录原始路径；路由不匹配、在路由之前短路的请求同样不在保护内。建议网关对这类路径限制请求方法，并自行处理访问日志。本方案不为此改设计。

非目标：

- 不引入通用脱敏框架或 `Microsoft.Extensions.Compliance.Redaction`，那一套针对的是带数据分类的业务日志字段，覆盖不到框架自己写的路径。
- 不改网关、ingress 的访问日志，那属于部署职责。
- 不替业务决定认证方式。

验收：

- **默认配置**：模板 `appsettings.json` 把 `Microsoft.AspNetCore.Hosting.Diagnostics` 设为 `None`。Given 任意请求中写出的业务日志，Then 控制台（Simple 与 JSON）和 OTLP 日志里都没有 `RequestPath`/`RequestId` 作用域；关联标识、租户与 TraceId 照旧。
- **端点声明**：Given 端点调用了 `.WithSensitiveRoute()`（或使用等价的特性），When 请求该端点，Then：
  - 请求完成日志里的路径记为该端点的路由模板（如 `/api/v1/mail-receipts/{provider}/{routeKey}`），不出现原始值；
  - 服务端 span 的 `url.path` 同样记为路由模板；
  - 同一请求中的业务日志、请求完成日志、OTLP 日志与追踪，全部不出现机密原值。
- **未声明的端点**：行为不变，请求完成日志和 `url.path` 照常记录原始路径。
- **调试级日志**：`Microsoft.AspNetCore` 显式按类别覆盖为 Warning，系统设置的最小级别降不下去；规范写明，生产环境不要开启 Routing/MVC 的 Debug。
- **测试**：集成测试映射一个带声明的测试端点，用 FakeLogCollector 与真实 OTLP 接收端证明原始值没有进入任何日志或追踪；未声明端点照常记录原始路径（对照）。变异：去掉其中任一处改写，测试失败。

## 2. 方案

1. **一处声明。** `Hosting/SensitiveRouteMetadata.cs`：端点元数据标记，同时提供扩展 `WithSensitiveRoute()`，用于 `IEndpointConventionBuilder`，控制器上用同名特性。业务只在端点上声明一次。
2. **两处使用这份声明**，都是模板现有的代码：
   - `RequestLoggingInterceptor.OnResponseAsync`：端点带该标记时，把已收集的 `Path` 参数换成路由模板：取端点的 `IRouteDiagnosticsMetadata.Route`（与追踪 `http.route` 同源），补齐开头的 `/`；全局异常处理清掉端点后改取 `IExceptionHandlerFeature.Endpoint`。
   - `TelemetryExtensions` 的 ASP.NET Core 追踪：在 `EnrichWithHttpResponse` 中，端点带该标记时，把 `url.path` 设为路由模板。
3. **默认配置。** `Microsoft.AspNetCore.Hosting.Diagnostics: None`，并在注释中写明：Hosting 作用域与请求完成日志重复，还会把原始路径带进每一条日志；关掉它的代价是该类别的日志全部关闭，包括 `GenericWebHostService` 的启动异常日志；默认通用宿主仍经 `ILogger<Host>` 以 Error 记录 `Hosting failed to start`，未捕获的异常照常抛出并由运行时输出，而且启动完成前 OTLP 本来就不发送。
4. **框架日志不记原始路径。** 下面三处框架日志原来写 `Path={Path}`，取的是原始请求路径：
   - `Leistd.ExceptionHandling` 的 `BusinessExceptionHandler`；
   - `Leistd.MultiTenancy` 的 `TenantSessionRecoveryMiddleware`；
   - `Leistd.Security` 的 `BrowserOriginMiddleware`。

   框架看不到模板里的标记，所以统一改为 `Route={Route}`，取匹配到的端点路由模板。异常处理器在端点已被清除时，改取 `IExceptionHandlerFeature.Endpoint`；没有匹配到端点时记 null。完整路径只由请求完成日志记录，并受模板标记约束。日志字段从 `Path` 改名为 `Route`，按破坏性变更提交。
5. **规范。**
   - `template/docs/standards/coding-common.md` §1 与 `coding-backend.md` §7：路径里有机密的端点用 `WithSensitiveRoute()`；首选把机密移出 URL，例如验证服务商的签名。
   - `docs/deploy/configuration.md`「日志」一节：网关访问日志由部署侧另行脱敏，生产环境不开 Routing/MVC 的 Debug。

## 3. 计划

1. 按 §2 实施后端与文档，补集成测试与单元测试。
2. 两种代表场景（Identity 开本地化、Resource 关本地化）做后端构建、单测与集成测试，高负载任务放 worker-01；`-GenerateOnly` 跑选择器给出的场景；相关闸门。
3. 端到端：真实 Kestrel 加 Collector，请求带机密的测试端点，核对 stdout（Simple 与 JSON）、Collector 的 logs/traces 中没有原始值。
4. 交 Codex 代码审查，通过后推送、开 PR、合并，回执 WP2。

## 4. 结果

（实施后填写。）
