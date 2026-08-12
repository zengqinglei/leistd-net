# 服务调用 SDK 引入 Refit 的评估

> 关联方案：[服务调用 SDK 技术方案](../plans/2026-08-11-service-invocation-sdk.md)
> 现状基线：`feat/service-invocation-sdk`（`Leistd.ServiceClient.Core/OAuth/AspNetCore` 已交付）

## 1. 问题

现状下业务服务编写自己的 Client 包时，每个方法都要手写「`httpClient.GetAsync(url)` → `ReadContentAsync<T>()`」样板：URL 拼接、query 参数编码、multipart 构造都靠手工完成，方法数一多重复度高、易写错。诉求：让业务 SDK 作者**只声明接口 + 特性**，HTTP 细节由库生成。

**边界澄清**：候选库只替代「接口实现层」（手写 HttpClient 调用代码）。现有 `Leistd.ServiceClient` 的核心价值——DelegatingHandler 管道（日志/TraceId/用户头）、client credentials 认证、被调方用户上下文恢复、`RemoteServiceException` 错误契约——与此正交，**全部保留复用**，不存在"替换 SDK 基准库"。

## 2. 候选对比

| 候选 | 形态 | 优势 | 劣势 | 结论 |
| --- | --- | --- | --- | --- |
| 现状（手写 typed client） | `HttpClient` + 扩展方法 | 零依赖、完全可控 | 每方法样板代码；multipart/query 编码手工易错 | 保留为基线路径 |
| **Refit**（reactiveui/refit） | 接口 + 特性，源生成实现 | 社区事实标准、活跃维护（MIT）；`AddRefitClient` 返回 `IHttpClientBuilder`，与现有 handler 管道**无缝组合**；`RefitSettings.ExceptionFactory` 可整体替换错误语义；STJ 默认序列化；multipart/form/流均有一等支持 | 新第三方依赖；非 2xx 默认抛 `ApiException` 需适配为我们的契约；接口需对源生成器可见 | **推荐** |
| RestEase | 同为接口式，Refit 启发 | 轻量 | 社区与维护活跃度显著低于 Refit，生态收敛到 Refit | 排除 |
| Flurl | 链式 fluent 调用 | 语法简洁 | 非接口契约式，client 包无法表达为稳定接口，与"Client 包 = 强类型契约"的定位冲突 | 排除 |
| OpenAPI 代码生成（Kiota / NSwag / Refitter） | 从 swagger 生成客户端 | 契约驱动、零手写 | 生成代码量大、风格不可控；要求各服务维护高质量 OpenAPI 描述；与"Client 包自带精选 DTO"的 D6 决策冲突 | 不作为基线；**Refitter（生成 Refit 接口）留作后续演进**，与本推荐同向兼容 |

> 2026 年生态现状：接口式运行时客户端以 Refit 为主流，OpenAPI 生成器阵营（Kiota/NSwag/Refitter）中 Refitter 生成的目标恰是 Refit 接口——先落 Refit，未来引入契约生成不推翻本次选型。

## 3. 推荐方案：新增 `Leistd.ServiceClient.Refit` 可选包

```
framework/components/service-client/Leistd.ServiceClient.Refit/
├── DependencyInjection.cs      # AddRefitServiceClient<TApi, TOptions>(serviceName, configuration)
├── LeistdRefitSettings.cs      # 统一 RefitSettings 工厂：STJ(Web) 序列化 + 错误契约适配
└── ...
```

- `AddRefitServiceClient<TApi, TOptions>`：内部 `AddRefitClient<TApi>(settings, httpClientName: serviceName)` + 复用现有标准管道装配（把 `AddServiceClient` 的管道部分提为共享入口，如 `IHttpClientBuilder.AddServiceClientPipeline<TOptions>(serviceName)`），返回 `IHttpClientBuilder` 供继续叠加 `AddClientCredentials`。
- **错误契约统一**：`RefitSettings.ExceptionFactory = async response => 非 2xx ? 解析 ProblemDetails 构造 RemoteServiceException : null`——复用 Core 现有解析逻辑（提为可复用方法）。业务代码无论走手写路径还是 Refit 路径，捕获的都是同一个 `RemoteServiceException`，不感知 `ApiException`。
- **序列化统一**：`SystemTextJsonContentSerializer` + Web 默认（camelCase、大小写不敏感），与模板服务端一致。
- 手写路径（Core 扩展方法）继续支持，文档标注两条路径的适用场景；模板 Client 示例改写为 Refit 接口作为推荐形态。

业务 SDK 作者的最终体验：

```csharp
public interface IOrderApi
{
    [Get("/api/v1/orders/{id}")]
    Task<OrderDto> GetAsync(Guid id, CancellationToken ct = default);

    [Multipart]
    [Post("/api/v1/orders/{id}/attachments")]
    Task UploadAsync(Guid id, StreamPart file, CancellationToken ct = default);
}
// 注册：services.AddRefitServiceClient<IOrderApi, OrderServiceClientOptions>("OrderService", config)
//           .AddClientCredentials(config);
```

## 4. 数据格式传输规范（官方文档核对）

| 格式 | Refit 写法 | 对接要点 |
| --- | --- | --- |
| JSON 请求体 | `[Body] T dto`（默认 STJ 序列化） | 与服务端 camelCase 契约一致，由统一 `RefitSettings` 保证 |
| 表单 `application/x-www-form-urlencoded` | `[Body(BodySerializationMethod.UrlEncoded)]`，接受 `IDictionary` 或普通对象（公共可读属性→字段，`[AliasAs]` 重命名） | 覆盖 OAuth 式表单端点；token 获取仍走我们 OAuth 包，不经业务接口 |
| 文件上传 `multipart/form-data` | 方法标 `[Multipart]`，参数用 `StreamPart(stream, fileName, contentType, name)` / `ByteArrayPart` / `FileInfoPart`，`[AliasAs]` 定字段名 | 旧 `AttachmentName` 特性已废弃，规范中禁用 |
| 二进制/文件下载 | 返回 `Task<HttpResponseMessage>`（原始响应，自行读流）或 `Task<string>`（文本） | 下载方法须先 `await response.EnsureRemoteSuccessAsync()`（Core 现有扩展）再读流；`ExceptionFactory` 对返回 `HttpResponseMessage` 的方法不介入错误转换，此点必须写入文档 |
| 响应元数据 | `Task<ApiResponse<T>>`（状态码/头/`HasResponseError`） | 仅诊断场景使用；常规业务方法直接 `Task<T>`，错误统一走 `RemoteServiceException` |
| Query 参数 | 方法参数自动拼接，`[AliasAs]`/`[Query]` 控制命名与格式 | 替代手写 URL 拼接，消除编码错误 |

**是否需要落文档：需要。** 判据：这些是「使用者必须知道的调用契约与限制」（尤其：文件下载不经 ExceptionFactory、multipart 命名规则、表单序列化范围），属于 `framework/docs/components/service-client.md` 的职责范围——在 Refit 包落地时于该文档新增「Refit 接入」与「数据格式规范」章节（随包分发）；`template/docs/standards/service-invocation.md` 增加链接与模板侧示例。本评估文档不随包分发，仅存决策依据。

## 5. 风险与前置确认

| 风险 | 说明与缓解 |
| --- | --- |
| Refit 版本与破坏性变更 | 实施时最新稳定版 **15.0.0**（CPM 锁版本，升级走版本评审）。v15 拆分源生成与反射构建器：multipart/原始响应等无法内联生成的方法（RF006）需要 `Refit.Reflection` 包——由 `Leistd.ServiceClient.Refit` 统一携带。另 v15 要求 `Microsoft.Extensions.Http >= 10.0.10`，框架 CPM 已同步抬升 |
| 源生成器可见性 | Refit 接口须对生成器可见（public，或 internal + `InternalsVisibleTo`）；写入接入文档 |
| 错误语义双轨 | 未配置我们 `RefitSettings` 的裸 `AddRefitClient` 会抛 `ApiException`——接入规范要求一律经 `AddRefitServiceClient` 注册 |
| 依赖面扩大 | 仅新增可选包引入 Refit；Core/OAuth/AspNetCore 不依赖 Refit，不用 Refit 的消费者零影响 |

## 6. 实施工作量估算

1. Core：提取管道装配为 `AddServiceClientPipeline`、错误解析逻辑公共化（无行为变化，内部重构）——小。
2. 新包 `Leistd.ServiceClient.Refit` + 单元/端到端测试（复用现有双宿主基建，补 multipart/表单/下载场景）——中。
3. 文档：组件文档新增 Refit 接入与数据格式规范章节；模板 Client 示例与 `service-invocation.md` 改写——中。
4. CPM 登记 Refit、包消费与矩阵验证——小。
