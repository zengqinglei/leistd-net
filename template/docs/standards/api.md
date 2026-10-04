# API 规范

## 1. 基本原则

- API 应表达资源和业务动作，避免暴露内部实现细节。
- 请求、响应、错误格式保持一致。
- 所有破坏性操作必须具备认证、授权、审计和幂等策略。
- 对外接口变更应记录兼容性影响和迁移方式。

## 2. 响应格式

本项目响应风格：**成功直接返回业务对象（裸对象，不包裹），失败统一返回 RFC 9457 `ProblemDetails`**。
该风格由 Leistd 框架的全局异常处理（`Leistd.ExceptionHandling.AspNetCore`）落地，无需也不应在 Controller 里手动包裹 `Ok(...)` 或自定义 `{success,data}` 信封。

> 设计取舍：前端按 HTTP 状态码判断成功失败；失败用 ProblemDetails（`application/problem+json`）携带机器可读的错误信息。
> JSON 序列化：属性 camelCase、忽略 null 值、枚举序列化为字符串。

### 2.1 成功响应

直接返回业务对象，HTTP 200。

```json
{
  "id": "0198f2a1-...",
  "name": "示例",
  "isActive": true,
  "creationTime": "2026-06-25T08:00:00Z"
}
```

### 2.2 空响应

无返回对象的操作（删除、启用/禁用、登出等）使用无泛型 `Task`，成功时返回 HTTP 200 且无响应体。普通业务 Controller 不使用 `IActionResult` 表达成功结果；只有下面两种情形才使用它：

1. 同一端点需要返回 `Redirect`、`Forbid`、`SignIn` 等**多种协议结果**；
2. 端点返回的是**文件下载**（如 CSV 导出）——带 content-type 与文件名的响应没有"裸对象"的表达方式，`File(...)` 必然产出 action result。此时应用服务返回**字节与元数据**（内容、媒体类型、建议文件名），由 Controller 包成 `File(...)`：应用层依赖 MVC 返回类型就只能从 Controller 调用了。

```text
HTTP/1.1 200 OK
```

框架组件自带的端点（设置、权限、操作记录、通知、租户与租户连接，映射在 `Api/Hosting/ComponentEndpoints.cs`）按 Minimal API 惯例，无返回对象的写操作返回 **HTTP 204**。客户端把 200 空响应与 204 一样当作成功处理，不按状态码分支。

### 2.3 分页响应

分页查询返回 `PagedResult<T>`，HTTP 200。固定字段 `totalCount` + `items`。

```json
{
  "totalCount": 0,
  "items": []
}
```

### 2.4 错误响应

失败统一返回 RFC 9457 `ProblemDetails`，`Content-Type: application/problem+json`，由 `Leistd.ExceptionHandling.AspNetCore` 的全局异常处理（`BusinessExceptionHandler`）产出。除 RFC 标准字段外，所有失败都带 `traceId` 扩展字段；业务错误另带稳定错误码 `code`，公开文案在标准字段 `detail`。输入校验、未预期异常、上游故障等协议层失败的契约就是 HTTP 状态码（RFC 9457 §4），只带本地化 `title` 与 `traceId`（校验另带 `errors`），不带 `code` 与 `detail`。

```json
{
  "type": "urn:leistd:problem:business-error",
  "title": "Bad Request",
  "status": 400,
  "detail": "用户名 'admin' 已存在",
  "instance": "/api/v1/users",
  "code": "User:UsernameTaken",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| type | string | 稳定问题类型 URI；校验与业务错误分别为 `validation-error`、`business-error`，其余按状态码使用 ASP.NET Core 默认值 |
| title | string | 与 status 对应的错误标题，按当前语言本地化（如 `请求无效`、`Not Found`） |
| status | number | HTTP 状态码 |
| detail | string | 业务错误面向用户的说明，按错误码本地化或回落为安全文案；协议层失败不带 |
| instance | string | 出错的请求路径 |
| code | string | **稳定错误码**：只出现在业务错误上，`BusinessException` 在构造时必填，形如 `User:UsernameTaken`；也是本地化词条键 |
| traceId | string | ASP.NET Core 写出的链路标识（当前 `Activity.Id`），W3C 格式 `00-<TraceId>-<SpanId>-<flags>`，第二段是 TraceId，用它检索日志与链路；没有 Activity 时为请求标识。业务关联标识另在响应头 `X-Correlation-Id` |

> `GlobalExceptionOptions.IncludeExceptionDetails` 默认为 `false`；开启后仅额外输出 `stackTrace`，只用于本地调试，生产环境不开启。

### 2.5 验证错误响应

`[ApiController]` 自动模型校验和内部调用抛出的 `System.ComponentModel.DataAnnotations.ValidationException` 都返回 HTTP **400**，使用 `urn:leistd:problem:validation-error` 与 Leistd `errors` 对象数组。跨字段或用例规则失败抛 `BusinessException`，未配置状态时同样默认为 400。

```json
{
  "type": "urn:leistd:problem:validation-error",
  "title": "Bad Request",
  "status": 400,
  "instance": "/api/v1/products",
  "errors": [
    {
      "detail": "名称不能为空",
      "field": "name",
      "code": "Product:NameRequired"
    },
    {
      "detail": "价格必须大于 0",
      "field": "price"
    }
  ],
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

## 3. HTTP 状态码

| 状态码 | 场景 |
| --- | --- |
| 200 | 查询或操作成功 |
| 201 | 创建成功 |
| 400 | 服务端认为是客户端导致的请求错误；包括结构、字段、协议参数和默认业务拒绝 |
| 401 | 未认证 |
| 403 | 无权限 |
| 404 | 资源不存在 |
| 409 | 状态冲突或幂等冲突 |
| 415 | 请求媒体类型不受支持 |
| 422 | 可选：内容语法成立，但无法按其指令处理；只在客户端需区分时按错误码显式映射 |
| 429 | 请求触发明确的频率限制 |
| 500 | 服务端异常 |
| 502 | 未处理的上游拒绝、无效或提前中断的响应；不透传远端状态 |
| 503 | 本服务连接上游失败，或明确知道所依赖能力暂时不可用 |
| 504 | 本服务等待上游响应超时 |

> **请求结构/字段校验**用 400 + `errors`；**业务规则失败**用 `BusinessException`，默认 400。仅对特殊状态在 API 组合根按错误码显式映射，不重复登记 400。

## 4. 异常与 HTTP 映射

后端只保留一个业务异常 `BusinessException(code, safeMessage, innerException?)`。错误码是必填且不可变的机器契约；各 API 业务模块只登记自己的非默认 HTTP 状态，由组合根汇总；框架组件的默认状态由组件在自己的 `AddXxx` 里登记，需要改时在组合根用 `MapCode` / `MapException` 覆盖：

| 来源 | 默认 HTTP | 说明 |
| --- | --- | --- |
| `BusinessException` 命中错误码映射 | 400 / 401 / 403 / 404 / 409 等 | 宿主按稳定业务语义精确决定 |
| 未命中的 `BusinessException` | 400 | 广义的客户端请求错误；防止新错误码意外变成稀有状态 |
| DataAnnotations 自动校验 / `ValidationException` | 400 | 请求字段或结构不合法，返回 `errors` |
| 未捕获的 BCL/技术异常 | 500 | 只返回通用安全文案，细节记日志 |
| 框架判定的请求错误（请求体无法解析、请求体过大、内容类型不符、路由不存在、未认证、限流） | 400 / 413 / 415 / 404 / 401 / 429 | `/api` 下统一返回 Problem Details，只有状态码、本地化标题与 `traceId`，不带业务错误码；开发与生产环境一致。前端按状态码处理这类失败 |
| `ServiceClientException` | 按本地观测的失败来源返回 500/502/503/504（注册客户端时自动登记） | 本地配置或未分类故障默认 500，远端明确失败、响应无效或提前中断默认 502；不从远端状态推断本地状态。原始 URL、响应片段只留服务端诊断，已知上游契约可由宿主覆盖 |

`ArgumentException`、`InvalidOperationException`、`HttpRequestException` 等优先按 .NET 语义抛出，框架不根据类型猜测为 400/503。认证和授权拒绝优先交给 ASP.NET Core 管道，不用业务异常模拟。

`WithData("Name", value)` 为本地化文案的 `{Name}` 占位符传值，无论是否启用多语言都可保留。不提供 `WithCode` 或 `WithDetails`：错误码必须在构造时完整，技术详情只进入 `InnerException` 和日志。

公开文案只写用户能采取的下一步。非敏感且确有帮助的输入可以保留，例如已登录管理员操作中的订单号；密码、令牌、连接串以及登录/找回密码等匿名场景中可用于枚举账号的用户名、邮箱不回显。未预期 5xx 只展示通用文案与 `traceId`。

<!--#if (IncludeLocalization)-->
### 4.1 异常本地化

本项目已启用多语言（`--include-localization true`），异常按请求 `Accept-Language` 本地化。`Message` 与 `Code` 分工如下：

| 职责 | 载体 | 说明 |
| --- | --- | --- |
| 安全回落 | `Message`（构造参数） | 可读英文，资源未命中或未启用多语言时会直接给用户，不得含内部细节 |
| 身份 + 展示 | `Code`（构造参数） | 必填的稳定机器契约，也是本地化词条键 |

- **throw 处写法**：
  ```csharp
  throw new BusinessException(
          "User:UsernameTaken",
          $"Username '{username}' already exists.")
      .WithData("Username", username);
  ```
  资源 `Resources/{en,zh-CN}.json` 的 `texts` 段按错误码给出各语言文案（`en` 为默认/回落）：`"User:UsernameTaken": "Username '{Username}' already exists."`。
- **`Code` 一经对外即为契约**，重命名它是破坏性变更。这是"一个标识符同时承担机器身份与词条键"的代价，换来的是不必为每个错误维护两个必须同步的字符串。
- 全局处理器按 **`Code` 词条 → 安全 `Message`** 解析，本地化失败不改写 `code` 或 HTTP 语义。
- **未启用多语言时**会直接返回 `Message`，因此必须从抛出点就是安全、可展示的文案；原始技术异常放在 `InnerException` 中。
- 错误码命名 `模块:语义`（`User:*`、`Auth:*`、`OpenApp:*`、`Security:*` 等），前缀由一个模块独占，常量成员名与语义后缀一致；码定义在所属模块的 `Errors/`，而非集中到 `Domain/Shared/Errors`。前端**直接显示后端 `detail`**，不重复翻译业务错误。
- **所有 `BusinessException` 都必须带码**，并由构造函数强制；不需要根据是否启用多语言加条件编译。
- **DataAnnotations 校验消息**也随 culture 本地化：DTO 的 `ErrorMessage`/`Display` 用英文句子作键（`"{0} is required."`），`zh-CN.json` 按同一句子映射中文；`Program.cs` 已接线 `AddDataAnnotationsLocalization(...DataAnnotationLocalizerProvider...)`。这样参数校验与业务异常在同一请求下**同语言**。

**示例（`Message` 是安全英文回落，`Code` 给出稳定身份）**：

```csharp
// 业务规则验证失败；该码在 API 组合根映射为 409
if (await userRepository.AnyAsync(u => u.Username == username, cancellationToken))
    throw new BusinessException("User:UsernameTaken", $"Username '{username}' already exists.")
        .WithData("Username", username);

// 资源不存在；该码在 API 组合根映射为 404
var user = await userRepository.GetAsync(id, cancellationToken);
if (user is null)
    throw new BusinessException("User:NotFound", $"User {id} not found.")
        .WithData("Id", id);

// 请求字段校验由 DTO DataAnnotations 与 [ApiController] 统一产生 400 + errors
```
<!--#endif-->

## 5. 认证与授权

### 5.1 用户认证

<!--#if (OpenIddictServer)-->
支持本地 Cookie Session 与 OpenIddict Bearer 校验。
<!--#elseif (ResourceBrowserSession)-->
支持远端 Bearer 校验与服务端 Cookie Session；有 Authorization 头时只选 Bearer，失败不回退 Cookie。
<!--#elseif (RemoteTokenAuth)-->
仅使用 OpenIddict 校验远端 Bearer，不提供 Cookie Session。
<!--#else-->
使用本地 Cookie Session，不提供 OIDC 授权服务器或 Bearer 验证入口。
<!--#endif-->
<!--#if (LocalIdentity)-->
- Cookie 会话在服务端登记（`UserSessions`，即个人设置里的「登录设备」）：每个请求都确认会话仍然有效，所以撤销——退出某台设备、退出其他所有设备、修改密码、管理员重置密码、退出登录——对已发出的 Cookie 立即生效。确认结果缓存 1 分钟；多实例部署未配 Redis 时，其他实例上的撤销至多滞后这么久。
<!--#if (OpenIddictServer)-->
- 自签发的访问令牌不在会话撤销的范围内，按有效期自然失效。
- 访问令牌是只签名、不加密的 JWT：资源服务经 discovery/JWKS 本地验签，不需要分发解密密钥。令牌中的 claim 对每个持有者可读（包括获准的 public/native 客户端），不要放入业务机密；授权码与 refresh token 不受此影响，仍然加密。要改为加密令牌，须删除 `DisableAccessTokenEncryption()` 并为每个资源服务配置解密凭据；要改用 introspection，须启用签发端点，并让资源服务以客户端身份调用。两者都要同时改动签发端与资源端。
<!--#endif-->
<!--#endif-->
- 所有需要用户身份的接口必须校验认证状态。
- 认证失败统一返回 401，不暴露内部认证细节。

### 5.2 授权

- 写操作必须校验资源归属或角色权限。
- 批量操作必须逐项校验权限或明确全局权限。
- 管理接口必须与普通用户接口隔离权限。

### 5.3 敏感信息

- 不在响应、日志、错误消息中返回密钥、Token、连接串。
- 邮箱、手机号、证件号等敏感字段按项目规则脱敏。

## 6. 分页规范

### 6.1 请求参数

分页查询使用 `offset/limit` 偏移分页（与 `PageRequest` 一致），不使用 `page/pageSize`。

| 参数 | 类型 | 必填 | 默认值 | 说明 |
| --- | --- | --- | --- | --- |
| offset | number | 否 | 0 | 起始偏移量，从 0 开始；**小于 0 返回 400** |
| limit | number | 否 | 10 | 每页数量；**取值范围 1 ~ 1000，越界返回 400** |
| keyword | string | 否 | 空 | 搜索关键字 |
| sorting | string | 否 | 空 | 排序字段，如 `name asc`、`creationTime desc`；**字段取自各接口自己的白名单，不在白名单内返回 400**，方向词只认 `asc` / `desc` |

### 6.2 后端 DTO 命名

- 分页查询输入：`Get{Entity}PagedInputDto`，继承 `PageRequest`。
- 分页返回：`PagedResult<{Entity}OutputDto>`，字段为 `totalCount` + `items`。

## 7. HTTP 方法与路由规范

对外接口统一以 `/api/v1/` 开头。

| 操作 | 方法 | 路由 | 后端方法名建议 |
| --- | --- | --- | --- |
| 分页查询 | GET | `/api/v1/{resource}` | `GetPagedListAsync` |
| 单个查询 | GET | `/api/v1/{resource}/{id}` | `GetAsync` |
| 创建 | POST | `/api/v1/{resource}` | `CreateAsync` |
| 更新 | PUT | `/api/v1/{resource}/{id}` | `UpdateAsync` |
| 局部更新 | PATCH | `/api/v1/{resource}/{id}` | `PatchAsync` |
| 删除 | DELETE | `/api/v1/{resource}/{id}` | `DeleteAsync` |

## 8. API 文档

仅在 API 需要供其他团队、客户端或外部使用者长期查阅时建立文档。先参考项目中最新的同类 API 文档，根据真实契约说明必要的路由、权限、请求、响应和错误语义；不复制固定章节模板。

## 9. 兼容性

- 新增字段默认向后兼容。
- 删除字段、修改字段含义、修改错误码属于破坏性变更。
- 破坏性变更必须明确迁移方案并获得相关使用者确认。

<!--#if (SpaFrontend)-->
## 浏览器认证

浏览器与所属 API 必须同源；开发期 Angular 代理转发 `/api/**`。页面由前端渲染，认证协议由后端处理。浏览器只持有 HttpOnly 会话引用，OAuth access/refresh/id token 留在服务端 `ITicketStore`，不写 URL、前端存储、JSON 响应或 SignalR 参数。唯一的例外是退出：依赖方以自动提交的表单把 id_token 作为 `id_token_hint` 发往 Identity，它只出现在那张表单的正文里，不进地址栏、历史记录与 Referer。SignalR 浏览器连接使用同源 Cookie；机器令牌仅走 Authorization 头，Hub 仅接受请求头中的 Bearer。

### 会话与部署

`DistributedTicketStore` 使用已有分布式缓存与 Data Protection 保护完整票据。多实例必须共享缓存与 Data Protection 密钥；没有 Redis 的本地开发使用内存缓存，重启后需重新登录。缓存票据删除后，复制的旧 Cookie 失效。显式登录更换引用版本，滑动续期保留版本；旧请求不能把已撤销票据重新写回，也不能删除再次登录的新版本。

会话 Cookie 在部署环境名为 `__Host-Http-CompanyName.ProjectName.Auth`，Secure、HttpOnly、`Path=/`、不带 Domain；浏览器只接受经 HTTPS 写入的这个名字，因此对外源必须是 HTTPS，站点不能挂在子路径下。Development 环境为了支持 HTTP 同源调试，名字不带前缀，Secure 跟随请求协议。

`SessionCookie:SameSite` 只控制应用会话 Cookie；默认 Lax。OAuth correlation 与 OIDC nonce Cookie 保持官方 SameSite=None、Secure=Always，HTTPS 回调不可省略。开发回调在 `/api/**` 下，由开发代理转发。

浏览器写 API 请求与实时 Hub（`/hubs/**`，含 WebSocket 握手）检查 Origin，接受本源及显式 `Cors:AllowedOrigins`。CORS 不约束 WebSocket，Hub 的来源检查不因带 Authorization 头而跳过。没有 Origin 时看 `Sec-Fetch-Site`：值为 `cross-site` 或 `same-site` 的拒绝，`same-origin`、`none` 放行；两个头都没有的非浏览器调用保持支持。API 写请求不使用 ASP.NET Core antiforgery，也不把 Angular 默认 XSRF 拦截器当成完整防护；唯一用到官方 antiforgery 的是 Identity 的退出确认表单（见下文）。浏览器认证不支持独立跨源 API 地址；进程分离须由部署代理将页面、认证导航、协议回调与 API 暴露在同一个外部源。`environment.api.gateway` 保持空值，以相对路径访问同源 API；其他服务通过同源微服务路由前缀访问。整页认证导航不经过 HTTP 拦截器；不要将任意源加入允许列表。OIDC form_post 回调由官方处理器消费，依靠 state、correlation 与 nonce 校验。

<!--#if (LocalIdentity)-->
### 本地账号

账号密码与第二步验证走 `/api/v1/auth/session-login` 等现有会话接口。`GET /api/v1/auth/me` 读取已验证用户，退出撤销服务端会话。第二步完成前不签发最终会话，第二步凭据由 JSON 与前端导航状态传递。
<!--#endif-->
<!--#if (OpenIddictServer)-->
### 依赖方的登录与退出

授权（`/connect/authorize`）与退出（`/connect/logout`）启用了 OpenIddict 请求缓存：首个请求校验后存为 request token（控制库的令牌表），再重定向回同一端点、只带 `client_id` 与 `request_uri`，此后才进入控制器。依赖方因此可以跨站 POST 发起，重入是顶层 GET，Lax 会话 Cookie 随之送达；附加在重入地址上的参数不能改写缓存的请求。request token 一次性：授权完成或退出完成时即被标记为已兑现，同一 `request_uri` 再次进入返回 400；过期记录由已有的令牌清理任务删除。

请求要求重新认证（`prompt=login`，或 `max_age` 已超过）时，跳转登录页的回跳地址带一份受保护的证明：绑定这一个 `request_uri`，只有证明签发之后才开始的会话（即一次新的登录）能兑现它，签发前就已存在的会话都不行。证明只抵消 `prompt=login` 与 `max_age=0` 的"每次都要认证"；正数 `max_age` 在回跳时仍按当前认证年龄判断。登录、第二步验证与外部登录都回到同一地址；没有新认证就回跳会再次要求登录，证明挪给别的请求无效。

退出时，`id_token_hint` 中的会话标识（`sid`）与当前 Identity 会话一致才直接退出并回到登记的退出回调；没有 hint、hint 属于别的会话时转到确认页 `/auth/logout-confirm`（RP-Initiated Logout 1.0 §2）。确认页的地址只有 `request_uri` 与受保护的确认凭据，后者绑定该退出请求与当前会话的用户、租户、会话标识，有效 10 分钟。页面经 `GET /api/v1/auth/logout-confirmation` 核对凭据并取得官方 antiforgery 令牌，用户确认时整页 POST 回 `/connect/logout`；防伪令牌或凭据不符时再次显示确认页，会话保持。重新登录后，旧确认页不能结束新会话；取消不退出，发起退出的应用已清掉的本地会话不会因此恢复。当前没有会话时直接回到退出回调。

开放应用的"会话绑定"（`sessionBound`，创建与更新都必须显式给值，缺失或 null 返回 400）决定授权是否跟随签发时的 Identity 会话：

- 开启时，授权码、刷新令牌与 id_token 带上会话标识（访问令牌不带）；换取或刷新令牌时会话已退出、被撤销或空闲到期即返回 `invalid_grant`。判定不记活跃，后台续期不会延长 Identity 会话。服务端会话类客户端（BFF，如 Resource 的浏览器登录）应开启：用户在 Identity 退出后，它在访问令牌到期时随之收敛。
- 关闭时授权与会话无关，适合需要持续离线续期的客户端（桌面端、原生应用）。管理界面新建 Web 应用默认开启，桌面端与服务模板默认关闭。
- 授权按签发时的事实处理：开启前签发的刷新令牌在开启后被拒，客户端须重新授权；签发时已绑定的授权在关闭后仍受约束。早于该设置的登记读作未设置（按关闭处理），编辑时须明确选择。
- 修改登记只在当前实例立即生效：OpenIddict 应用缓存只在本进程失效、没有时间过期，多实例修改后滚动重启 Identity。

依赖方须登记 `ept:end_session` 与退出回调；退出确认、重新认证证明与 Cookie 都依赖 Data Protection，多实例除共享缓存外还要共享控制库、令牌证书、Data Protection 密钥环与应用名，以及分布式锁（见部署文档）。
<!--#endif-->
<!--#if (ExternalLogin)-->
### 外部账号

Google 使用微软官方 AddGoogle（UserInfo v3）；GitHub 使用 aspnet-contrib 的 AddGitHub（`AspNet.Security.OAuth.GitHub`），显式启用 S256 PKCE。处理器自带的邮箱补取已关闭，因为它只给地址、不给 verified，且失败即中断登录；主邮箱及其 verified 由 `OnCreatingTicket` 查询 `/user/emails` 取得，查询失败时只是不按邮箱关联。定制 github scheme 时用 `Configure<GitHubAuthenticationOptions>`，不是 `OAuthOptions`。添加提供商时在组合根注册官方远程处理器：scheme 名为 `AuthenticationSchemeNames.ExternalProviderPrefix + provider`（provider 使用小写），`SignInScheme` 指向 `ExternalCookie`，`CallbackPath` 在 `/api/**` 下，并在 `OnCreatingTicket` 把规范化 `ExternalUserInfo` 序列化到 `context.Properties.Items[ExternalAuthenticationExtensions.UserInfoKey]`。目录从该专用前缀的远程 scheme 得出，Cookie/Bearer/策略 scheme 均不开放。注册时明确协议失败响应、所需 PKCE 与资料验证；账号关联、锁定、用户名生成与第二步验证仍由领域/应用层决定。

0. 登录页匿名读取 `GET /api/v1/external-auth/providers`，只为已登记的提供商显示入口；读取失败时单独提示并可重试（5xx 附追踪 ID），不当作"未配置"。登录页只内置 GitHub、Google 两个入口，新增提供商时要同时补前端入口和 `getExternalLoginUrl` 的提供商类型。
1. 浏览器导航至 `GET /api/v1/external-auth/{provider}/challenge`，可带站内 `returnUrl`（外站地址返回 400）。绑定使用 `GET /api/v1/external-auth/{provider}/link/challenge`，要求通过自然人策略的非受限会话。
2. 提供商回调至 `/api/v1/external-auth/{provider}/signin`，官方处理器完成 code/state/correlation/PKCE 与 UserInfo，签发五分钟外部票据引用，然后重定向前端 `/auth/external-callback/{provider}?intent=...`。用户在提供商处取消或协议校验失败（state、correlation 等）时，不签发外部票据，重定向前端 `/auth/external-callback/{provider}?intent=...&error=cancelled|failed`：登录意图显示原因并提供返回登录入口（会话仍有效时直接回到应用，例如后退键重放旧回调），绑定意图回到安全设置页并提示。业务提示中的提供商名使用官方 scheme 的显示名（`ExternalUserInfo.ProviderDisplayName`）。
3. 前端 `POST /api/v1/external-auth/{provider}/complete` 或受保护的 `POST /api/v1/external-auth/{provider}/link/complete`，请求体为空对象。后端匹配受保护的提供商、意图、绑定发起者与租户，先一次消费外部票据，再执行账号政策；登录返回最终会话结果或第二步凭据及受保护的 `returnUrl`，前端在登录或第二步成功后接续该地址；绑定返回 `{ linked: true }`。

完成端点失败也不能重用票据，须重新 challenge；查询参数不能改变保护过的登录/绑定意图。提供商后台需分别登记上述完整 HTTPS signin 地址。Google v3 使用 `sub/email_verified`。邮箱接口失败或未验证邮箱不允许按邮箱关联账号。
<!--#endif-->
<!--#if (RemoteTokenAuth)-->
### Resource 依赖方

后端是 OIDC 机密客户端，使用 code、PKCE、SaveTokens 与服务端票据。配置 `Authentication:Issuer`、`Audience`、`ClientId`、`ClientSecret`，缺键启动失败；`Scope` 可省略，默认与 Audience 同名。密钥只放后端机密配置。

Identity 登记 web/confidential 客户端，开启会话绑定，允许 authorization code、refresh token、退出端点、PKCE、openid/profile/email/roles/offline_access 和本 API scope。登录回调登记完整 `/api/v1/auth/signin`，退出回调登记完整 `/api/v1/auth/signout`。会话绑定使 Identity 退出后本服务的会话在访问令牌到期时收敛，而不是靠刷新令牌继续存活。

前端导航至 `GET /api/v1/auth/login?returnUrl=...`，仅接受站内 returnUrl；回调后 `GET /api/v1/auth/me` 还原用户、角色与租户。授权与退出请求都以官方 FormPost（`AuthenticationMethod = FormPost`）发往 Identity：响应是一张自动提交的表单，参数不进地址栏。`POST /api/v1/auth/logout` 先删除本服务端票据（旧 Cookie 立即失效），再以表单携带 `id_token_hint` 发起退出，Identity 据其中的会话标识免确认退出。表单依赖一段内联脚本自动提交（禁用脚本时显示提交按钮）；宿主若加内容安全策略，要放行这段脚本或接受手动提交。

有 Authorization 头的请求只选官方 Bearer 验证，失败不回退到 Cookie；无头时选 Cookie。角色与租户取已验证访问令牌的声明，不能假定 ID token 具有资源声明：会话主体在登录回调里换成访问令牌的主体，id_token 的声明（含 `auth_time`）不进会话。需要重新认证时，用官方 `OpenIdConnectChallengeProperties` 的 `MaxAge` 与 `Prompt` 发起挑战；它们只要求 Identity 重新验证身份，不是多因素或升级认证——那需要 Identity 把 `amr`/`acr` 签发进访问令牌，模板没有内置。`OnValidatePrincipal` 在过期前一分钟于服务端刷新，同一会话由分布式锁串行化，采用最新 refresh token；失败注销会话。访问令牌保存在服务器，模板自己的 `IUserAccessTokenAccessor` 为下游 Token Exchange 提供经过验证的请求令牌。

访问令牌按只签名的 JWT 本地验签（issuer、audience、签名、有效期），本服务不持有解密凭据；签发方若改为加密令牌或 introspection，这里要同步配置。

签发方轮换签名证书后，遇到不认识的 kid 时先向配置的签发方刷新一次公钥再验（`Auth/SigningKeyRefresh.cs`，覆盖 Bearer、登录回调与服务端续期；id_token 由 OIDC 处理器自身刷新重试）。只处理可读的 JWS，公钥只来自配置的发现文档，验签规则不放宽。同一时刻的刷新合并成一次抓取，抓取超时 10 秒，请求刷新每分钟至多转交一次（签发方不可用时，伪造 kid 的请求不会逐个触发抓取）；抓取失败时沿用已有公钥；抓取成功则本次只用返回的公钥集，签发方撤掉的公钥不再参与验签。逐请求结果只记 Debug，真实的刷新请求每次记一条 Information。这依赖进程级开关 `Switch.Microsoft.IdentityModel.UpdateConfigAsBlocking`（Api 与集成测试项目以 `RuntimeHostConfigurationOption` 设置）：它也让定期自动刷新改为由到点的请求等待完成。签发方刚刷新过（IdentityModel 的 5 分钟间隔、本服务的 1 分钟限频）或不可达时，新 kid 的请求仍会失败，所以轮换仍按签发方部署文档的顺序先发布、后切换。

退出 Resource 会话不会撤销签发方所有既有令牌；注销 Identity Cookie 与撤销 OAuth 授权/令牌也是不同边界。账号或租户停用后的本地验签窗口由访问令牌有效期（Identity 的 `OAuth:AccessTokenLifetime`，默认 10 分钟）决定，后续刷新失败收敛会话。
<!--#endif-->
<!--#endif-->
