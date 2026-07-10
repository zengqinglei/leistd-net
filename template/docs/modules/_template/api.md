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
GET /api/{module}?offset=0&limit=10
```

**请求参数**（分页约定以 `api-standard.md` §6 为准：`offset/limit` 偏移分页）

| 参数名 | Query/Body/Path | 类型 | 必填 | 默认值 | 说明 |
| --- | --- | --- | --- | --- | --- |
| offset | query | number | 否 | 0 | 起始偏移量，从 0 开始 |
| limit | query | number | 否 | 10 | 每页数量 |

**响应**（`PagedResultDto`，字段以 `api-standard.md` §2/§6 为准）

```json
{
  "totalCount": 0,
  "items": []
}
```

## 4. 错误响应

失败统一返回 RFC 7807 `ProblemDetails`（`application/problem+json`），格式与状态码映射**以 [api-standard.md](../../standards/api-standard.md) §2.4/§3/§4 为单一权威**（不自定义 `code` 字段体系）。本模块可能返回的状态：

| HTTP | 场景 | 抛出的异常 |
| --- | --- | --- |
| 400 | 参数格式错误或**业务校验失败**（本项目统一 400，不用 422） | `BadRequestException` |
| 404 | 资源不存在 | `NotFoundException` |
| 409 | 状态冲突 | `ConflictException` |

## 5. 安全与权限

- 认证方式：{authentication}
- 授权规则：{authorization}
- 幂等策略：{idempotency}
- 审计要求：{audit}

## 6. 兼容性

- 新增字段：默认向后兼容。
- 删除/重命名字段：必须记录迁移方案。
- 错误码变化：必须同步前端处理逻辑。
