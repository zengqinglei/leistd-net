# 通知列表分页对齐规范与候选上限写明边界

状态：已完成 · 风险档：中 · 关联：#316

## 1. 需求

### 问题与背景

- **通知列表不符合分页规范（下游反馈）。** `GET /api/v1/notifications` 用 `maxCount` 参数，越界时静默收敛到 1–100，只返回条目数组。分页规范（`template/docs/standards/api.md` §5、§2.3）要求统一用 `offset/limit`，越界返回 400，响应为 `PagedResult`。模板前端照抄 `maxCount`，下游跟着照抄。
- **全仓盘点（应用户要求）。** 框架其余组件端点（租户管理、操作记录、分布式事件、Webhook）和模板后端的分页接口都已是 `offset/limit` 加 400；前端发给后端的分页参数也都走 `PagedRequestDto`。前端地址栏的 `page/pageSize` 与表格的 `pageIndex/pageSize` 属于界面状态，经 `table-query-state.ts` 统一换算成 `offset/limit`，不在规范约束范围内。盘点中唯一另类的是租户人员候选搜索：它不分页，内部 `Take(200)`，但接口没有写明这个边界，前后端各写了一个 200。

### 目标与非目标

- 目标：通知列表与分页规范完全一致；候选上限只在前后端各定义一次，并写进接口说明。
- 非目标：
  - 候选搜索不改成分页：它是"输入关键字找人"，满额时提示继续输入，已经够用。
  - 通知列表不加排序参数，沿用固定排序。

### 功能要求与验收

| # | Given | When | Then | 覆盖 |
| --- | --- | --- | --- | --- |
| R1 | 已登录用户 | `GET /notifications?offset=&limit=&unreadOnly=` | 按参数分页，返回 `totalCount` 与 `items` | `NotificationEndpointTests`、模板 `NotificationTests` |
| R2 | 任意调用方 | `limit` 不在 1–100，或 `offset<0` | 400 校验错误，`errors[].field` 为对应参数，不查存储 | `NotificationEndpointTests` |
| R3 | 非用户调用方 | 请求列表 | 空的 `PagedResult` | 同上 |
| R4 | 模板前端与 Mock | 加载通知 | 请求带 `limit=50`、读取 `items`；Mock 同形，越界同样返回 400 | `notification-service.spec.ts`、`_mock/api/notification.spec.ts` |
| R5 | 候选搜索返回满额 | 打开授权弹窗 | 界面提示继续输入；上限由后端 `TenantStaffAppService.CandidateLimit` 与前端 DTO 常量 `TENANT_STAFF_CANDIDATE_LIMIT` 各定义一处 | `tenant-staff-edit-dialog.spec.ts` |

方案：

- 通知列表新增输入类型 `GetNotificationsInputDto : PageRequest`，覆写 `Limit` 的默认值（50）和范围（1–100）。
- 端点改用带默认值的 `offset`/`limit` 参数，构造输入后用 `Validator.ValidateObject` 校验，与其他组件服务的写法一致。
- 删除 `NotificationEndpoints.MaximumListCount`。
- 这是破坏性变更，下游随下一个 beta 同步，不保留过渡兼容。

## 4. 结果

- **交付：** #316（合并提交 `0c06629e`）。通知分页是框架的破坏性变更，提交脚注写明了：
  - 改传 `limit`、改读 `items`；
  - 用 `GetNotificationsInputDto.MaximumLimit` 替代删掉的常量；
  - 宿主必须接入全局异常处理，越界才会返回 400，未接入时是 500。
- **偏差：**
  - 上限常量放在输入类型 `GetNotificationsInputDto` 上（`DefaultLimit = 50`、`MaximumLimit = 100`），而不是端点类。原因是 `[Range]` 校验要用到它，而 Core 包不能引用 AspNetCore 包。
  - Mock 只接受 Int32 范围内的十进制整数。Codex 还建议对齐 ASP.NET 的其他绑定细节（首尾空格、重复参数、布尔大小写），没有采纳，理由是 Mock 只需覆盖界面实际发出的参数。
- **验收：**
  - `NotificationEndpointTests` 覆盖 R1–R3，修复前的收敛用例已改为断言越界返回 400；
  - 模板 `NotificationTests` 等集成测试改为读取 `items`；
  - 前端与 Mock 的 spec 覆盖 R4；
  - 全场景矩阵 CI 通过。
- **已上收的常驻文档：** `framework/docs/components/notifications.md`，包括接口参考与"须接入全局异常处理"的接入说明。
