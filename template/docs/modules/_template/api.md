# {ModuleName} - API 文档

## 1. 接口总览

| 方法 | 路径 | 说明 | 权限 |
| --- | --- | --- | --- |
| GET | `/api/{module}` | 查询列表 | `{Permission}` |
| GET | `/api/{module}/{id}` | 查询详情 | `{Permission}` |
| POST | `/api/{module}` | 创建资源 | `{Permission}` |
| PUT | `/api/{module}/{id}` | 更新资源 | `{Permission}` |
| DELETE | `/api/{module}/{id}` | 删除资源 | `{Permission}` |

## 2. DTO / 数据结构

### 2.1 `{Entity}OutputDto`

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| id | string | 是 | 唯一标识 |

## 3. 接口详情

### 3.1 查询列表

```http
GET /api/{module}?page=1&pageSize=20
```

**请求参数**

| 参数名 | Query/Body/Path | 类型 | 必填 | 默认值 | 说明 |
| --- | --- | --- | --- | --- | --- |
| page | query | number | 否 | 1 | 页码 |
| pageSize | query | number | 否 | 20 | 每页数量 |

**响应**

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

## 4. 错误码

| code | HTTP | 说明 | 处理建议 |
| --- | --- | --- | --- |
| `{MODULE}_NOT_FOUND` | 404 | 资源不存在 | 检查资源 ID |
| `{MODULE}_VALIDATION_FAILED` | 422 | 业务校验失败 | 根据 details 修正输入 |
| `{MODULE}_CONFLICT` | 409 | 状态冲突 | 刷新后重试 |

## 5. 安全与权限

- 认证方式：{authentication}
- 授权规则：{authorization}
- 幂等策略：{idempotency}
- 审计要求：{audit}

## 6. 兼容性

- 新增字段：默认向后兼容。
- 删除/重命名字段：必须记录迁移方案。
- 错误码变化：必须同步前端处理逻辑。
