# API 规范

## 1. 基本原则

- API 应表达资源和业务动作，避免暴露内部实现细节。
- 请求、响应、错误格式保持一致。
- 所有破坏性操作必须具备认证、授权、审计和幂等策略；认证与权限规则见 [认证与授权](auth.md)。

## 2. 响应格式

本项目响应风格：**成功直接返回业务对象（裸对象，不包裹），失败统一返回 RFC 9457 `ProblemDetails`**。
该风格由 Leistd 框架的全局异常处理（`Leistd.ExceptionHandling.AspNetCore`）落地，无需也不应在 Controller 里手动包裹 `Ok(...)` 或自定义 `{success,data}` 信封。

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
| 200 | 查询或操作成功（含创建：本项目业务接口有响应体时直接返回 DTO） |
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

框架不根据 BCL 异常类型猜测为 400/503；认证和授权拒绝交给 ASP.NET Core 管道，不用业务异常模拟。

`WithData("Name", value)` 为本地化文案的 `{Name}` 占位符传值，无论是否启用多语言都可保留。不提供 `WithCode` 或 `WithDetails`：错误码必须在构造时完整，技术详情只进入 `InnerException` 和日志。

错误码不随是否启用多语言而变化，命名与放置：

- 采用 `模块:语义`（`User:*`、`Auth:*`、`OpenApp:*`、`Security:*` 等），前缀由一个模块独占，常量成员名与语义后缀一致。
- 按所属模块和最低实际使用层放置在该模块的 `Errors/`：Domain 规则码归 Domain 模块，纯用例码归 Application 模块，`Domain/Shared` 只放真正跨模块的契约；组件错误码引用组件常量。
- 错误码对外即为契约，改名是破坏性变更，前端分支与状态映射都要有针对性测试。
- 前端直接显示后端 `detail`，不重复翻译业务错误。

公开文案只写用户能采取的下一步。非敏感且确有帮助的输入可以保留，例如已登录管理员操作中的订单号；密码、令牌、连接串以及登录/找回密码等匿名场景中可用于枚举账号的用户名、邮箱不回显。未预期 5xx 只展示通用文案与 `traceId`。

<!--#if (IncludeLocalization)-->
### 4.1 异常本地化

本项目已启用多语言（`--include-localization true`），异常按请求 `Accept-Language` 本地化。`Message` 与 `Code` 分工如下：

| 职责 | 载体 | 说明 |
| --- | --- | --- |
| 安全回落 | `Message`（构造参数） | 可读英文，资源未命中或未启用多语言时会直接给用户，不得含内部细节 |
| 身份 + 展示 | `Code`（构造参数） | 必填的稳定机器契约，也是本地化词条键 |

- 资源 `Resources/{en,zh-CN}.json` 的 `texts` 段按错误码给出各语言文案（`en` 为默认/回落），占位符与 `WithData` 的键同名：`"User:UsernameTaken": "Username '{Username}' already exists."`。抛出写法见[后端开发规范 §3.4](./coding-backend.md#34-领域服务)。
- `Code` 同时承担机器身份与词条键，因此不必为每个错误维护两个必须同步的字符串。
- 全局处理器按 **`Code` 词条 → 安全 `Message`** 解析，本地化失败不改写 `code` 或 HTTP 语义。
- **未启用多语言时**会直接返回 `Message`，因此必须从抛出点就是安全、可展示的文案；原始技术异常放在 `InnerException` 中。
- **所有 `BusinessException` 都必须带码**，并由构造函数强制；不需要根据是否启用多语言加条件编译。
- DataAnnotations 校验消息同样随 culture 本地化（写法见[后端开发规范 §5](./coding-backend.md#5-命名与-dto)），参数校验与业务异常在同一请求下同语言。
- `scripts/check-i18n.py` 检查：两种语言资源的 `culture` 与文件名一致、键集合与 `{Name}` 占位符一致；Domain、Application 的 `*ErrorCodes.cs` 常量形如 `<所有者>:<成员名>`（所有者取文件名去掉 `ErrorCodes`）、不重复且在资源里有句子，`BusinessException` 不写字面量码；`Display` 名与 `ErrorMessage` 原文都是资源键，`Dtos/` 下的校验特性都显式写 `ErrorMessage`。

**示例（`Message` 是安全英文回落，`Code` 给出稳定身份）**：

```csharp
// 资源不存在；该码在 API 组合根映射为 404
var user = await userRepository.GetByIdAsync(id, cancellationToken);
if (user is null)
    throw new BusinessException("User:NotFound", $"User {id} not found.")
        .WithData("Id", id);

// 请求字段校验由 DTO DataAnnotations 与 [ApiController] 统一产生 400 + errors
```
<!--#endif-->

## 5. 分页规范

### 5.1 请求参数

分页查询使用 `offset/limit` 偏移分页（与 `PageRequest` 一致），不使用 `page/pageSize`。

| 参数 | 类型 | 必填 | 默认值 | 说明 |
| --- | --- | --- | --- | --- |
| offset | number | 否 | 0 | 起始偏移量，从 0 开始；**小于 0 返回 400** |
| limit | number | 否 | 10 | 每页数量；**取值范围 1 ~ 1000，越界返回 400** |
| keyword | string | 否 | 空 | 搜索关键字 |
| sorting | string | 否 | 空 | 排序字段，如 `name asc`、`creationTime desc`；**字段取自各接口自己的白名单，不在白名单内返回 400**，方向词只认 `asc` / `desc` |

### 5.2 后端 DTO 命名

- 分页查询输入：`Get{Entity}PagedInputDto`，继承 `PageRequest`。
- 分页返回：`PagedResult<{Entity}OutputDto>`，字段为 `totalCount` + `items`。

## 6. HTTP 方法与路由规范

对外接口统一以 `/api/v1/` 开头。

| 操作 | 方法 | 路由 | 后端方法名建议 |
| --- | --- | --- | --- |
| 分页查询 | GET | `/api/v1/{resource}` | `GetPagedListAsync` |
| 单个查询 | GET | `/api/v1/{resource}/{id}` | `GetAsync` |
| 创建 | POST | `/api/v1/{resource}` | `CreateAsync` |
| 更新 | PUT | `/api/v1/{resource}/{id}` | `UpdateAsync` |
| 局部更新 | PATCH | `/api/v1/{resource}/{id}` | `PatchAsync` |
| 删除 | DELETE | `/api/v1/{resource}/{id}` | `DeleteAsync` |

## 7. API 文档

仅在 API 需要供其他团队、客户端或外部使用者长期查阅时建立文档。先参考项目中最新的同类 API 文档，根据真实契约说明必要的路由、权限、请求、响应和错误语义；不复制固定章节模板。

## 8. 兼容性

- 新增字段默认向后兼容。
- 删除字段、修改字段含义、修改错误码属于破坏性变更。
- 破坏性变更必须明确迁移方案并获得相关使用者确认。
