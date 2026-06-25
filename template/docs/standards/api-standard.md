# API 规范

## 1. 基本原则

- API 应表达资源和业务动作，避免暴露内部实现细节。
- 请求、响应、错误格式保持一致。
- 所有破坏性操作必须具备认证、授权、审计和幂等策略。
- 对外接口变更应记录兼容性影响和迁移方式。

## 2. 响应格式

本项目响应风格：**成功直接返回业务对象（裸对象，不包裹），失败统一返回 RFC 7807 `ProblemDetails`**。
该风格由 Leistd 框架的全局异常处理（`Leistd.Exception.AspNetCore`）落地，无需也不应在 Controller 里手动包裹 `Ok(...)` 或自定义 `{success,data}` 信封。

> 设计取舍：成功响应裸对象与 Leistd / volo.abp 约定一致，前端按 HTTP 状态码判断成功失败；失败用 ProblemDetails（`application/problem+json`）携带机器可读的错误信息。
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

无内容的操作（删除、启用/禁用、登出等）返回 HTTP 204，无响应体。

```text
HTTP/1.1 204 No Content
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

失败统一返回 RFC 7807 `ProblemDetails`，`Content-Type: application/problem+json`。

```json
{
  "type": "about:blank",
  "title": "请求处理失败",
  "status": 400,
  "detail": "用户名 'admin' 已存在",
  "instance": "/api/v1/users",
  "traceId": "00-abc...-def...-01"
}
```

字段说明：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| type | string | 错误类型 URI；未细分时为 `about:blank` |
| title | string | 错误标题（人类可读、与 status 对应） |
| status | number | HTTP 状态码 |
| detail | string | 本次错误的具体说明 |
| instance | string | 出错的请求路径（可选） |
| traceId | string | 链路追踪 ID（扩展字段，贯穿调用链） |

### 2.5 验证错误响应

模型校验（Data Annotations）失败返回 HTTP 400，`ProblemDetails` 的 `errors` 扩展字段按字段聚合错误信息（ASP.NET Core `ValidationProblemDetails` 标准格式）。

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "name": ["名称不能为空"],
    "price": ["价格必须大于 0"]
  },
  "traceId": "00-abc...-def...-01"
}
```

## 3. HTTP 状态码

| 状态码 | 场景 |
| --- | --- |
| 200 | 查询或操作成功 |
| 201 | 创建成功 |
| 204 | 成功且无需返回内容 |
| 400 | 参数格式错误或业务校验失败 |
| 401 | 未认证 |
| 403 | 无权限 |
| 404 | 资源不存在 |
| 409 | 状态冲突或幂等冲突 |
| 500 | 服务端异常 |

> 业务校验失败本项目统一用 400（由 `BadRequestException` 抛出），不区分到 422。

## 4. 异常类型映射

后端使用 `Leistd.Exception.Core` 提供的异常类型，由全局异常处理转换为对应 HTTP 状态码的 `ProblemDetails`。

| 异常类型 | HTTP | 使用场景 |
| --- | --- | --- |
| `BadRequestException` | 400 | 参数/请求结构错误、业务规则验证失败 |
| `UnauthorizedException` | 401 | 未登录、Token 无效 |
| `ForbiddenException` | 403 | 已登录但权限不足 |
| `NotFoundException` | 404 | 资源不存在 |
| `ConflictException` | 409 | 状态冲突、重复提交 |
| （未捕获异常） | 500 | 未预期异常，detail 不暴露内部细节 |

**示例**：

```csharp
// 业务规则验证失败
if (await userRepository.AnyAsync(u => u.Username == username, cancellationToken))
    throw new BadRequestException($"用户名 '{username}' 已存在");

// 资源不存在
var user = await userRepository.GetAsync(id, cancellationToken);
if (user is null)
    throw new NotFoundException($"用户 {id} 不存在");
```

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
| offset | number | 否 | 0 | 起始偏移量，从 0 开始 |
| limit | number | 否 | 10 | 每页数量 |
| keyword | string | 否 | 空 | 搜索关键字 |
| sorting | string | 否 | 空 | 排序字段，如 `name asc`、`creationTime desc` |

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

## 8. API 文档模板

### 8.1 接口说明

```markdown
### {接口名称}

{接口用途说明。}

```text
GET /api/v1/{resource}
```

**权限**：`{Permission}`

**参数**
| 参数名 | Query/Body/Path | 类型 | 必填 | 默认值 | 说明 | 示例值 |
| --- | --- | --- | --- | --- | --- | --- |
| offset | query | number | 否 | 0 | 起始偏移量 | 0 |
| limit | query | number | 否 | 10 | 每页数量 | 10 |

**示例**
```bash
curl --location --request GET '{service-url}/api/v1/{resource}?offset=0&limit=10' \
  --header 'Authorization: Bearer {token}'
```

**成功响应：HTTP 200**
```json
{
  "totalCount": 0,
  "items": []
}
```

**响应字段**
| 字段名 | 类型 | 说明 |
| --- | --- | --- |
| totalCount | number | 总数 |
| items | array | 数据列表 |

**失败响应：HTTP 400**（application/problem+json）
```json
{
  "type": "about:blank",
  "title": "请求处理失败",
  "status": 400,
  "detail": "请求参数不合法",
  "traceId": "{trace-id}"
}
```
```

## 9. 兼容性

- 新增字段默认向后兼容。
- 删除字段、修改字段含义、修改错误码属于破坏性变更。
- 破坏性变更必须在需求 Plan 中说明迁移方案。
