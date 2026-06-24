# API 规范

## 1. 基本原则

- API 应表达资源和业务动作，避免暴露内部实现细节。
- 请求、响应、错误格式保持一致。
- 所有破坏性操作必须具备认证、授权、审计和幂等策略。
- 对外接口变更应记录兼容性影响和迁移方式。

## 2. 响应格式

### 2.1 成功响应

```json
{
  "success": true,
  "data": {},
  "traceId": "{trace-id}"
}
```

### 2.2 空响应

无内容响应推荐使用 HTTP 204。若项目要求统一包裹，可返回：

```json
{
  "success": true,
  "data": null,
  "traceId": "{trace-id}"
}
```

### 2.3 分页响应

```json
{
  "success": true,
  "data": {
    "items": [],
    "total": 0,
    "page": 1,
    "pageSize": 20
  },
  "traceId": "{trace-id}"
}
```

### 2.4 错误响应

```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "请求参数不合法",
    "details": []
  },
  "traceId": "{trace-id}"
}
```

### 2.5 验证错误响应

```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "请求参数不合法",
    "details": [
      {
        "field": "name",
        "message": "名称不能为空"
      }
    ]
  },
  "traceId": "{trace-id}"
}
```

## 3. HTTP 状态码

| 状态码 | 场景 |
| --- | --- |
| 200 | 查询或操作成功 |
| 201 | 创建成功 |
| 204 | 成功且无需返回内容 |
| 400 | 参数格式错误 |
| 401 | 未认证 |
| 403 | 无权限 |
| 404 | 资源不存在 |
| 409 | 状态冲突或幂等冲突 |
| 422 | 业务校验失败 |
| 500 | 服务端异常 |

## 4. 异常类型映射

| 异常类型 | HTTP | 错误码建议 | 使用场景 |
| --- | --- | --- | --- |
| BadRequest | 400 | `BAD_REQUEST` | 参数格式、请求结构错误 |
| Unauthorized | 401 | `UNAUTHORIZED` | 未登录、Token 无效 |
| Forbidden | 403 | `FORBIDDEN` | 已登录但权限不足 |
| NotFound | 404 | `{RESOURCE}_NOT_FOUND` | 资源不存在 |
| Conflict | 409 | `{RESOURCE}_CONFLICT` | 状态冲突、重复提交 |
| Validation | 422 | `VALIDATION_ERROR` | 业务字段校验失败 |
| Internal | 500 | `INTERNAL_ERROR` | 未预期异常 |

## 5. 认证与授权

### 5.1 用户认证

- 默认使用 Bearer Token、Cookie Session 或项目声明的认证方式。
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

| 参数 | 类型 | 必填 | 默认值 | 说明 |
| --- | --- | --- | --- | --- |
| page | number | 否 | 1 | 页码，从 1 开始 |
| pageSize | number | 否 | 20 | 每页数量 |
| keyword | string | 否 | 空 | 搜索关键字 |
| sort | string | 否 | 空 | 排序字段 |

### 6.2 后端 DTO 命名

- 分页查询输入：`Get{Entity}PagedInputDto`
- 分页返回：`PagedResultDto<{Entity}OutputDto>` 或项目等价类型。

## 7. HTTP 方法与路由规范

| 操作 | 方法 | 路由 | 后端方法名建议 |
| --- | --- | --- | --- |
| 分页查询 | GET | `/api/{resource}` | `GetPageAsync` |
| 单个查询 | GET | `/api/{resource}/{id}` | `GetAsync` |
| 创建 | POST | `/api/{resource}` | `CreateAsync` |
| 更新 | PUT | `/api/{resource}/{id}` | `UpdateAsync` |
| 局部更新 | PATCH | `/api/{resource}/{id}` | `PatchAsync` |
| 删除 | DELETE | `/api/{resource}/{id}` | `DeleteAsync` |

## 8. API 文档模板

### 8.1 接口说明

```markdown
### {接口名称}

{接口用途说明。}

```text
GET /api/{resource}
```

**权限**：`{Permission}`

**参数**
| 参数名 | Query/Body/Path | 类型 | 必填 | 默认值 | 说明 | 示例值 |
| --- | --- | --- | --- | --- | --- | --- |
| page | query | number | 否 | 1 | 页码 | 1 |

**示例**
```bash
curl --location --request GET '{service-url}/api/{resource}?page=1&pageSize=20' \
  --header 'Authorization: Bearer {token}'
```

**成功响应：HTTP 200**
```json
{
  "success": true,
  "data": {
    "items": [],
    "total": 0,
    "page": 1,
    "pageSize": 20
  },
  "traceId": "{trace-id}"
}
```

**响应字段**
| 字段名 | 类型 | 说明 |
| --- | --- | --- |
| data.items | array | 数据列表 |
| data.total | number | 总数 |

**失败响应：HTTP 422**
```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "请求参数不合法"
  },
  "traceId": "{trace-id}"
}
```
```

## 9. 兼容性

- 新增字段默认向后兼容。
- 删除字段、修改字段含义、修改错误码属于破坏性变更。
- 破坏性变更必须在需求 Plan 中说明迁移方案。
