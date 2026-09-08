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

无返回对象的操作（删除、启用/禁用、登出等）使用无泛型 `Task`，成功时返回 HTTP 200 且无响应体。普通业务 Controller 不使用 `IActionResult` 表达成功结果；只有同一端点需要返回 `Redirect`、`Forbid`、`SignIn` 等多种协议结果时才使用它。

```text
HTTP/1.1 200 OK
```

### 2.3 分页响应

分页查询返回 `PagedResultDto<T>`，HTTP 200。固定字段 `totalCount` + `items`。

```json
{
  "totalCount": 0,
  "items": []
}
```

### 2.4 错误响应

失败统一返回 RFC 9457 `ProblemDetails`，`Content-Type: application/problem+json`，由 `Leistd.ExceptionHandling.AspNetCore` 的全局异常处理（`BusinessExceptionHandler`）产出。除 RFC 标准字段外，固定附带 `code`、`message`、`traceId` 三个扩展字段。

```json
{
  "type": "urn:leistd:problem:business-error",
  "title": "Bad Request",
  "status": 400,
  "detail": "用户名 'admin' 已存在",
  "instance": "/api/v1/users",
  "code": "User:UsernameTaken",
  "message": "用户名 'admin' 已存在",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| type | string | 稳定问题类型 URI；校验错误为 `urn:leistd:problem:validation-error`，其他业务错误为 `urn:leistd:problem:business-error` |
| title | string | 错误标题（英文短语，与 status 对应，如 `Bad Request`、`Not Found`） |
| status | number | HTTP 状态码 |
| detail | string | 面向用户的错误说明；与 `message` 相同，按错误码本地化或回落为安全文案 |
| instance | string | 出错的请求路径 |
| code | string | **业务错误码扩展字段**：`BusinessException.Code`，形如 `User:UsernameTaken`。既是稳定机器契约（前端可据此分支），也是本地化词条键。未显式 `WithCode` 时为状态码通用码（`Error:BadRequest` 等），因此**恒有值** |
| message | string | 错误消息扩展字段，与 `detail` 同值，便于前端统一读取 |
| traceId | string | 链路追踪 ID；优先取 `Activity.Current?.TraceId` 的 32 位小写十六进制值，无 `Activity` 时回落 `HttpContext.TraceIdentifier` |

> `GlobalExceptionOptions.IncludeExceptionDetails` 默认为 `false`；关闭时不输出 `details` 或 `stackTrace`。开启后，`WithDetails` 设置的业务诊断信息以 `details` 返回，实际捕获异常的堆栈以 `stackTrace` 返回；不得在业务诊断信息中放入密钥、Token 或连接串。

### 2.5 验证错误响应

业务或应用层抛出 `UnprocessableEntityException` 时返回 HTTP **422**（默认 `code` = `Error:UnprocessableEntity`）；`System.ComponentModel.DataAnnotations.ValidationException` 也会被转换为该 422 响应。`[ApiController]` 自动模型校验返回 HTTP **400**。两条路径都使用 `urn:leistd:problem:validation-error` 问题类型，并将字段错误写入 Leistd 自定义的 `errors` 对象数组，而不是 `ValidationProblemDetails` 字典。

```json
{
  "type": "urn:leistd:problem:validation-error",
  "title": "Unprocessable Entity",
  "status": 422,
  "detail": "输入的信息有误",
  "instance": "/api/v1/products",
  "code": "Error:UnprocessableEntity",
  "message": "输入的信息有误",
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
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

## 3. HTTP 状态码

| 状态码 | 场景 |
| --- | --- |
| 200 | 查询或操作成功 |
| 201 | 创建成功 |
| 400 | 参数格式错误或业务规则校验失败 |
| 401 | 未认证 |
| 403 | 无权限 |
| 404 | 资源不存在 |
| 409 | 状态冲突或幂等冲突 |
| 415 | 请求媒体类型不受支持 |
| 422 | 实体字段校验失败（按字段聚合 `errors`） |
| 500 | 服务端异常 |
| 503 | 上游服务超时 / 连接失败 |

> **业务规则校验**（如「用户名已存在」）用 400（`BadRequestException`）；**结构化字段校验**用 422（`UnprocessableEntityException`）和 `errors` 数组。

## 4. 异常类型映射

后端使用 `Leistd.ExceptionHandling.Core` 提供的异常类型（均继承 `BusinessException`），由 `Leistd.ExceptionHandling.AspNetCore` 的全局异常处理转换为对应 HTTP 状态码的 `ProblemDetails`。每个异常声明对外 HTTP 状态码，并自带一个默认错误码。

| 异常类型 | HTTP | 默认 `code` | 使用场景 |
| --- | --- | --- | --- |
| `BadRequestException` | 400 | `Error:BadRequest` | 参数/请求结构错误、业务规则验证失败 |
| `UnauthorizedException` | 401 | `Error:Unauthorized` | 未登录、Token 无效 |
| `ForbiddenException` | 403 | `Error:Forbidden` | 已登录但权限不足 |
| `NotFoundException` | 404 | `Error:NotFound` | 资源不存在 |
| `ConflictException` | 409 | `Error:Conflict` | 状态冲突、重复提交 |
| `UnsupportedMediaTypeException` | 415 | `Error:UnsupportedMediaType` | 请求媒体类型不受支持 |
| `UnprocessableEntityException` | 422 | `Error:UnprocessableEntity` | 实体字段校验失败（按字段聚合 `errors`） |
| `InternalServerException` | 500 | `Error:InternalServer` | 未预期异常，`detail` 不暴露内部细节 |
| `ServiceUnavailableException` | 503 | `Error:ServiceUnavailable` | 上游服务超时 / 连接失败 |

框架还会**自动转换**下列常见异常，无需手动 catch：`System.ComponentModel.DataAnnotations.ValidationException` → 422；`OperationCanceledException` / `TimeoutException` / `HttpRequestException` → 400 或 503；其余未捕获异常 → 500。

`BusinessException` 支持链式细化：

- `WithCode("User:UsernameTaken")`：指定错误码，它同时是本地化词条键。不调用则为状态码通用码。
- `WithDetails("...")`：补充可对外返回的业务详情；该内容不受开发环境限制，不得包含敏感或内部诊断信息。
- `WithData("Name", value)`：为本地化文案的具名占位符 `{Name}` 提供值（仅启用多语言时用）。

<!--#if (IncludeLocalization)-->
### 4.1 异常本地化

本项目已启用多语言（`--include-localization true`），异常按请求 `Accept-Language` 本地化。`Message` 与 `Code` 分工如下：

| 职责 | 载体 | 说明 |
| --- | --- | --- |
| 日志/诊断 | `Message`（构造参数） | 永远是可读**英文**，进日志便于跨语言检索，**默认不给终端用户看** |
| 身份 + 展示 | `Code`（`WithCode`） | 既是稳定机器契约（前端可据此分支），也是本地化词条键。未设置时为状态码通用码 |

- **throw 处写法**：
  ```csharp
  throw new BadRequestException("Username already exists")          // Message：英文诊断，只进日志
      .WithCode("User:UsernameTaken")                              // Code：对外契约 + 词条键
      .WithData("Username", username);                             // 具名占位 {Username}
  ```
  资源 `Resources/{en,zh-CN}.json` 的 `texts` 段按错误码给出各语言文案（`en` 为默认/回落）：`"User:UsernameTaken": "Username '{Username}' already exists."`。
- **`Code` 一经对外即为契约**，重命名它是破坏性变更。这是"一个标识符同时承担机器身份与词条键"的代价，换来的是不必为每个错误维护两个必须同步的字符串。
- 全局处理器解析顺序：**`Code` → 状态码通用码（`Error:NotFound` 等）→ 兜底**，绝不把裸键漏给用户；查询全程 `try/catch` 隔离，本地化失败不覆盖原始 `code`。**响应结构不变**，仅 `message`/`detail`/`title` 随语言变，`code`/`traceId` 不变。
- **未启用多语言（off-mode）时**：没有 `IStringLocalizer`，业务错误对外只有状态码英文短语。`Message` 仍只进日志——这是刻意的安全默认。需要某条消息落地给用户，用 `AsUserFacing()` 显式声明。
- 错误码命名 `模块:语义`（`User:*`、`Auth:*`、`OpenApp:*`、`Security:*` 等）。前端**直接显示后端 `message`**，不重复翻译业务错误（见 [`coding-frontend.md`](./coding-frontend.md) §8.2）。
- **DataAnnotations 校验消息**也随 culture 本地化：DTO 的 `ErrorMessage`/`Display` 用英文句子作键（`"{0} is required."`），`zh-CN.json` 按同一句子映射中文；`Program.cs` 已接线 `AddDataAnnotationsLocalization(...DataAnnotationLocalizerProvider...)`。这样参数校验与业务异常在同一请求下**同语言**。

**示例（`Message` 恒为英文诊断，`Code` 给出身份）**：

```csharp
// 业务规则验证失败（400，未设 Code 时为 Error:BadRequest）
if (await userRepository.AnyAsync(u => u.Username == username, cancellationToken))
    throw new BadRequestException($"Username '{username}' already exists.");

// 资源不存在（404，未设 Code 时为 Error:NotFound）
var user = await userRepository.GetAsync(id, cancellationToken);
if (user is null)
    throw new NotFoundException($"User {id} not found.");

// 实体字段校验（422，按字段聚合）
throw new UnprocessableEntityException("email", "Invalid email format.");

// 前端需按具体错误分支时，给出错误码；它同时是词条键
throw new BadRequestException("Insufficient balance.").WithCode("Wallet:InsufficientBalance");
```
<!--#endif-->

## 5. 认证与授权

### 5.1 用户认证

- 默认使用 Bearer Token（OpenIddict 校验）或 Cookie Session。
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

分页查询使用 `offset/limit` 偏移分页（与 `PagedRequestDto` 一致），不使用 `page/pageSize`。

| 参数 | 类型 | 必填 | 默认值 | 说明 |
| --- | --- | --- | --- | --- |
| offset | number | 否 | 0 | 起始偏移量，从 0 开始；**小于 0 返回 400** |
| limit | number | 否 | 10 | 每页数量；**取值范围 1 ~ 1000，越界返回 400** |
| keyword | string | 否 | 空 | 搜索关键字 |
| sorting | string | 否 | 空 | 排序字段，如 `name asc`、`creationTime desc`；**字段取自各接口自己的白名单，不在白名单内返回 400**，方向词只认 `asc` / `desc` |

### 6.2 后端 DTO 命名

- 分页查询输入：`Get{Entity}PagedInputDto`，继承 `PagedRequestDto`。
- 分页返回：`PagedResultDto<{Entity}OutputDto>`，字段为 `totalCount` + `items`。

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
