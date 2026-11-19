# 分布式事件、Webhook 与租户授予访问的列表体验

状态：已完成 · 风险档：高 · 关联：方案对比页 [design.html](2026-11-19-platform-ops-list-ux/design.html)

## 1. 需求

### 问题与背景

用户给出员工端前端优化规范（列表骨架、间距梯度、状态徽章、工具栏、Sheet 与对话框写法），要求按它优化平台管理里的分布式事件、Webhook 两页，并补充三点：

- 两个列表加时间范围筛选；刷新按钮统一放在列表工具栏右侧，组件与布局参照操作记录页。
- 分布式事件"发件箱"的列表行缺少复选框。
- 租户页的"授予访问"对话框按场景流程优化，并审视"平台人员"抽屉列表。

走查现状（develop `c42d3685`）发现的问题见 design.html 的"现状"页签，要点：

- 分布式事件的刷新放在页头，样式为 outline，加载时禁用；传输状态一行挤了一枚徽章和三段完整时刻。
- 两个列表都不能按时间筛选。Webhook 的统计区间只作用于指标卡，切换区间不重新查询列表，所以点"失败 7"后，列表里是全部时间的失败投递。
- 复选框只在可恢复的行渲染。发件箱只有死信可恢复，大多数行没有复选框，看起来像整列缺失。收件箱、拒收共用同一张表格，也有这个问题。
- "按当前筛选重投"常驻工具栏，大多数时候处于禁用态。
- 授予对话框把最多 200 个候选人直接摊开，选中后只在行尾显示一个"✓"；关联账号只写静态的 `platform.<用户名>`；修改角色时不显示改的是谁；按钮叫"保存"，没选全时一直禁用。
- 平台人员抽屉不展示关联账号；"状态"列名看不出指的是关联账号的状态；角色用 outline 徽章；≥sm 时 … 菜单只剩一项，上面还多一条分隔线；空态没有操作入口。

### 目标

- 两页按规范的员工列表骨架排列：指标卡 → line Tab → 工具栏 → 表格 → 分页，详情用右侧 Sheet（design.html 方案 A）。
- 记录类列表可以按时间范围筛选，刷新统一在工具栏右组。
- 可恢复列表每行都有复选框，不可恢复的行禁用并说明原因。
- 授予访问一次只授予一人：用选择框选人，选中后预览将创建的关联账号；修改角色时写明对象（design.html D1）。

### 非目标

- 不新建"更多筛选"或已选条件 chip 组件。现有分面在 1440 宽、侧栏展开时一行放得下，分面触发器自身已显示选中值；为一处使用另建组件属于过度设计。
- 事件目录、Webhook 订阅是配置清单，不加时间范围。
- 不做批量授予（D2）和抽屉内联授予（D3）。
- 平台人员抽屉不加刷新：授予、修改、移除后已自动重新加载。
- 不改候选人范围：候选人仍是全部宿主用户，包含已停用的。
- 不改 Webhook 统计的计算口径，也不把统计区间与列表时间范围合并为一个控件。

### 功能要求与验收标准

**时间范围（框架与两页）**

- AC1 Given 分布式事件的发件箱、收件箱或拒收列表；When 选择时间范围；Then 只列出时间落在范围内的记录，两端都包含。发件箱按创建时间筛选，收件箱、拒收按接收时间筛选。加载截止 `before` 同时生效，实际上限取两者中较早的一个；整个范围都晚于截止时，结果为空。只给一端时按单边筛选。（框架关系型 EF 测试：边界、单边、缺省、与 `before` 叠加；前端 spec）
- AC2 Given Webhook 投递日志；When 选择时间范围；Then 只列出发生时间（投递创建时间）落在范围内的投递，两端都包含，筛选在计数和分页之前生效。（框架关系型 EF 测试；前端 spec）
- AC3 Given 起始晚于结束，或时间写法不符合下述规则；When 经真实端点调用三个列表 GET（查询参数）或四条按条件恢复 POST（JSON `filter`，即重投、重放、重新解码、丢弃），或 Webhook 投递列表 GET；Then 返回 400 验证错误。时间写法沿用官方绑定：GET 查询参数接受带 `Z` 或显式偏移的 ISO 时刻，偏移会被换算成 UTC；POST 的 JSON 只接受带 `Z` 的时刻，带非零偏移的会得到 `Kind.Local`，返回 400；两边不带时区的都返回 400。时间顺序由管理用例统一校验。现有 `Before` 的行为不变。（框架端点测试，用现有测试宿主并参数化覆盖以上写法）
- AC4 Given 发件箱筛选了"死信"和时间范围；When 按当前筛选重投；Then 只恢复范围内的死信，确认框的"筛选"一栏写出时间范围，"加载截止"一栏照旧单独显示。收件箱、拒收同理。（框架关系型 EF 测试；前端 spec）
- AC5 Given 时间范围控件；When 选择日期；Then 按账户的展示时区取起始日 00:00 和结束日 23:59:59.999，换成 UTC 后写进 URL 查询参数 `startTime` / `endTime`，页码回到第一页。刷新、前进后退或分享链接后条件保持。切换 Tab，以及从指标卡、详情跳到其他视图时，时间范围清除。发送测试事件成功后，导航总是清除时间范围（即使已在发件箱），保证刚创建的事件能被列出。URL 中只有一端、格式错误或起止颠倒时，控件、列表请求和按条件恢复都当作未筛选，不把这样的参数发给后端。（前端 spec，含"已在发件箱且筛着历史日期，发送测试事件后能看到该事件"）
- AC6 Given Webhook 统计区间为"近 7 天"；When 点"失败"指标卡；Then 跳到投递日志，状态筛选为失败，订阅筛选与统计相同，时间范围直接取统计响应里的 `range.start` / `range.end`（服务端实际计数用的 UTC 边界；含今天时截到统计查询时刻）。统计正在重新加载、显示的是旧数据时，用旧数据对应的区间。统计口径是左闭右开，列表是闭区间，两者只差恰好落在结束时刻的投递，不另做处理。（前端 spec，覆盖含今天与按订阅两种）
- AC7 Given 事件目录或订阅 Tab；When 查看工具栏；Then 不显示时间范围。Webhook 列表请求的去重键包含时间参数。（前端 spec）

**工具栏、表格与 Sheet（分布式事件、Webhook）**

- AC8 Given 任一列表；When 查看工具栏；Then 右组从左到右依次是：时间范围、刷新、本 Tab 的主操作（新建订阅）。刷新是 secondary 样式，加载时图标换成 spinner，按钮不禁用。（前端 spec）
- AC9 Given 分布式事件页；When 查看传输状态；Then 第一行是传输名加一枚健康徽章，下一行 `text-xs` 写状态变化、最后上报（相对时间，Tooltip 显示完整时刻）和近 24 小时计数。"发送测试事件"放在这一行右侧，样式为 outline，可见条件不变。（前端 spec）
- AC10 Given 分布式事件页；When 内容区宽度 ≥54rem；Then 指标卡一行四张，断点与 Webhook、仪表盘相同，都按容器宽度计算。（人工验收）
- AC11 Given 发件箱、收件箱或拒收；When 查看状态筛选；Then 用单选 toggle-group 快捷切换，再点一次取消即为全部；选中项用中性色。错误码、消费者、原因码仍用分面。（前端 spec）
- AC12 Given 可管理用户查看发件箱；When 列表含待发送、已发送、死信；Then 每行都有复选框，非死信行禁用，外层 Tooltip 说明"仅死信可重投"，并用 `aria-describedby` 关联。收件箱按可重放条件处理。拒收的勾选资格是"可重新解码或可丢弃"：待处理两者都可以，待重新解码只能丢弃。表头全选只选本页有资格的记录。（前端 spec）
- AC13 Given 勾选了记录，或状态筛选为可按条件恢复的状态；When 查看表格上方；Then 出现选择条：已选条数、取消选择、"重投已选（n）"；状态筛选满足条件时，再出现"按当前筛选重投"。两项条件都不满足时不显示选择条。拒收在同一选择条上同时提供重新解码和丢弃：每个按钮只统计、只提交符合该动作的 ID，n 为 0 时按钮禁用；状态筛选为待处理或待重新解码时，显示"按当前筛选丢弃"。（前端 spec）
- AC14 Given 状态徽章；When 渲染；Then 带状态点（`appStatusDot`）。发件箱待处理统一叫"待发送"，用 secondary；重试中用 warning；死信、处理失败用 destructive；已发送、已处理用 success。拒收的待处理、待重新解码需要人工处理，用 warning。（前端 spec）
- AC15 Given 记录详情或投递详情 Sheet；When 打开；Then 标题 16px，状态徽章紧跟标题；下一行 `text-xs` 是元信息；操作贴在标题行右侧，破坏性的"丢弃"收进 … 菜单的分隔线之后；失败记录在正文顶部有一条 Alert，写原因和下一步；分区标题带下线（`border-b pb-3`）；dl 统一为 `grid-cols-[8rem_minmax(0,1fr)]`。投递详情里，调用记录排在请求之前。（前端 spec；人工验收）

**授予访问与平台人员抽屉**

- AC16 Given 打开"授予访问"；When 选人；Then 人员是选择框，弹出层里可搜索（沿用服务端搜索），搜索框内按 Enter 不会提交对话框；已授予的人置灰并标"已授予"；返回满 200 条时提示"最多显示 200 人，可继续输入缩小范围"。选中后，触发器显示首字头像、姓名、用户名，下方显示"新建时使用关联账号 platform.<用户名>"。（前端 spec）
- AC17 Given 授予或修改对话框；When 查看角色；Then 每个角色显示显示名和代码名，内置角色带 secondary 徽章，标题行右侧显示已选数量。（前端 spec）
- AC18 Given 授予对话框；When 未选人或未选角色就点"授予访问"；Then 在对应字段下报错，焦点移到第一个错误，不发请求。按钮只在请求进行中禁用。授予与修改都走 Signal Forms、`injectFormSubmission` 和 Spartan Field，角色组用现有的组合字段校验。（前端 spec）
- AC19 Given 点某人的"修改角色"；When 对话框打开；Then 标题是"修改角色"，顶部用只读行写明首字头像、姓名、关联账号（后端返回的真实用户名）和账号状态。保存成功的提示与授予的提示分开。（前端 spec）
- AC20 Given 平台人员抽屉；When 打开；Then 标题 16px，下一行 `text-xs` 写显示名和"已授予 N 人"（N 为列表全集条数）。表格依次是：人员、关联账号（后端返回的真实用户名）、角色（secondary 徽章，最多两枚加"+n"）、账号状态、授予时间、操作。宽度不够时按 priority 收起；被收起的列在行展开里用 dl 补出，表头与值复用 `columnLabelKeys`、`appColumnValue`、`hiddenDetailColumns(table)`。（前端 spec）
- AC21 Given 行操作只有"修改角色"和"移除访问"两项；When ≥sm；Then 两项都平铺成 ghost 图标按钮（带 Tooltip 和 aria-label），移除用 `text-destructive`，不再出现 … 菜单。<sm 时两项收进 … 菜单，移除放在分隔线之后。（前端 spec）
- AC22 Given 抽屉里没有人员；When 查看；Then 显示 Spartan Empty，带标题、说明和"授予访问"按钮。搜索无结果时只显示"无匹配的平台人员"，不给授予按钮。加载失败时显示 Empty 和重试按钮。重新加载时保留现有行，只标记忙碌。（前端 spec）
- AC23 Given 授予成功；When 回到抽屉；Then toast 提示"已授予 <所选人姓名> 进入 <租户>"，列表重新加载，不做新行高亮。（前端 spec）

**形态与多语言**

- AC24 Given 关闭本地化、关闭 TenantWorkspace，或不含 Webhook、分布式事件的生成形态；When 生成并构建；Then 构建与受影响 spec 全绿，新增文案在 zh-CN、en 和英文内置文本里都有，字段校验用到的全局词条也同步。（模板生成验证）
- AC25 Given 390 宽；When 查看两页和平台人员抽屉；Then 表格按 priority 收列，行操作收进菜单，右对齐的日历浮层不超出视口，标签不折行。（人工验收）

### 已确认的决策

- 2026-11-19 用户确认"按推荐的来"：分布式事件与 Webhook 采用方案 A；授予访问采用 D1；Webhook 统计区间与列表时间范围保持分开，从指标卡跳转时带入；不可恢复的行禁用勾选；订阅和事件目录不加时间范围。
- 候选人是否排除已停用的宿主用户，用户未表态，按现状保留。
- 2026-11-19 Codex 方案审查第 1 轮（不通过，11 条）、第 2 轮（不通过，2 条与勘误）全部采纳。第 3 轮复审通过。其中 AC6 不把列表改成半开区间，直接传统计响应的 UTC 边界；结束时刻的差别属于极少数边缘情况，不增加复杂度。
- design.html 方案 A 里画的"已选条件 / 来源" chip，以及 D1 里的新行高亮，以本规格为准，不实现。

## 2. 方案

### 现状事实

- 分布式事件管理接口在框架 `Leistd.EventBus`：输入 DTO 在 `framework/components/event-bus/Leistd.EventBus.Distributed/Dtos/DistributedEvent{Outbox,Inbox,Rejection}Dtos.cs`，列表输入是 `GetDistributedEventOutboxInputDto`、`GetDistributedEventInboxInputDto`、`GetDistributedEventRejectionsInputDto`，`Before` 可省略；三个恢复 `*FilterDto` 的 `Before` 必填。校验集中在 `Management/DistributedEventManagementService.cs` 的 `Validate` / `ValidateRecovery` / `ValidateBefore`。列表查询在 `Leistd.EventBus.EntityFrameworkCore/Stores/EfCoreDistributedEventManagementStore.cs`，按条件恢复的筛选在同目录的 `DistributedEventRecovery.cs`：发件箱比较 `CreationTime`，收件箱、拒收比较 `ReceivedAt`。端点在 `Leistd.EventBus.AspNetCore/Endpoints/DistributedEventEndpoints.cs`，列表 GET 逐个绑定查询参数，恢复 POST 从 JSON 读 `filter`。端点测试在 `framework/tests/components/event-bus/Leistd.EventBus.Tests/AspNetCore/DistributedEventEndpointTests.cs`。
- Webhook 投递列表在框架 `Leistd.Webhooks`：`Leistd.Webhooks.Core/Dtos/WebhookDeliveryDtos.cs` 的 `GetWebhookDeliveriesInputDto` 没有时间字段；校验在 `WebhookManagementService.GetDeliveriesAsync` 的 `Validator.ValidateObject`；查询在 `EfCoreWebhookManagementStore.GetDeliveriesAsync`，按 `Delivery.CreationTime` 倒序；端点在 `WebhookEndpoints.MapDeliveries`，测试在 `Leistd.Webhooks.Tests/AspNetCore/WebhookEndpointTests.cs`。
- 时间范围的现成写法：框架 `Leistd.OperationRecords` 的 `GetOperationRecordPagedInputDto.StartTime/EndTime`（UTC，两端闭区间，`IValidatableObject` 校验起止顺序）；前端在 `operation-records.ts` 中用 `hlm-date-range-picker` / `hlm-date-range-input`，通过 `shared/utils/zoned-time.ts` 在展示时区与 UTC 之间换算。
- 模板前端：分布式事件页 `features/platform/components/distributed-events/`，所有记录表共用 `distributed-event-table`，复选框由 `recoverable()` 决定是否渲染；Webhook 页 `features/platform/components/webhooks/`，统计区间放在 URL 的 `range` 参数里，`showFailedDeliveries()` 只带状态和订阅。统计响应的 `range.start` / `range.end` 是服务端实际计数用的 UTC 边界，见 `template/backend/src/CompanyName.ProjectName.Application/Statistics/StatisticsPeriod.cs`。
- 授予访问：`features/platform/components/tenants/widgets/tenant-staff-sheet/`；候选人接口 `GET /api/v1/tenants/{id}/staff/candidates` 最多返回 200 条（`TenantStaffAppService.GetCandidatesAsync`）。关联账号首次创建时用户名为 `"platform." + home.Username`；宿主改名或撤销后重新授予时，已有的关联账号保留原用户名。`TenantStaffOutputDto.Username` 是宿主账号的用户名，不返回关联账号的用户名。现有授予对话框用旧 command 列表，`template/frontend/e2e/workspaces.e2e.ts` 直接操作它并点"Save"。

### 设计

**框架（两个组件，公共 DTO 加字段，不破坏现有调用）**

- 三个 `GetDistributedEvent*InputDto`、三个 `DistributedEvent*FilterDto` 和 `GetWebhookDeliveriesInputDto` 都加 `StartTime`、`EndTime`：可空，UTC，两端闭区间，命名与操作记录一致。`Before` 保持原义，是加载截止，与 `EndTime` 同时生效。
- 校验各只在管理服务的现有入口做一次：时间必须是 UTC，`StartTime` 不得晚于 `EndTime`。分布式事件放在 `Validate` / `ValidateRecovery`，与 `ValidateBefore` 同处；Webhook 放在 `GetDeliveriesAsync` 现有的 `Validator.ValidateObject` 路径上。端点只负责绑定，EF 只负责比较。
- 存储层按 AC1、AC2 的字段比较，与 `Before` 并列。`DistributedEventRecovery.cs` 的三处按条件恢复读取同一组筛选。
- 四个列表 GET（分布式事件三个、Webhook 一个）增加 `startTime`、`endTime` 查询参数；恢复端点的请求体经 `filter` 自动带上新字段。新增属性写公共 XML 注释。随包文档 `framework/docs/components/event-bus.md`（列表参数与恢复请求体）、`webhooks.md`（投递列表参数）同步更新。

**模板后端**

- `TenantStaffOutputDto` 增加 `LinkedUsername`，取关联账号（member）在库里的真实用户名。已授予的行与修改对话框都显示它；授予前的预览按"新建时使用 platform.<用户名>"表述。

**模板前端**

- 时间范围：三处（分布式事件、Webhook、操作记录）都组合现有的 `hlm-date-range-picker` 和 `hlm-date-range-input`，沿用 `zoned-time.ts` 与 `formatAppDate`。实现相同的部分（URL 参数回填为日期、手输解析、区间格式化、日期换算成 UTC 起止）抽成 `shared` 里的纯函数，三处共用；URL 导航、清除、页码重置和统计跳转留在各页。操作记录改为调用这些函数，行为保持不变（它在只有一端时仍逐字段转发，本次不改）。
- 分布式事件：重排传输行、指标卡与工具栏，加状态 toggle-group 和选择条；表格每行渲染复选框，不可恢复的禁用；徽章按视图取变体和文案；Sheet 按 AC15 调整。DTO、服务与按条件恢复都带上 `startTime` / `endTime`。
- Webhook：工具栏右组加时间范围；状态改用 toggle-group；`showFailedDeliveries()` 带入当前显示的统计数据的 `range.start` / `range.end`；列表查询的去重键包含时间参数；投递 Sheet 按 AC15 调整。
- 平台人员：抽屉按 AC20–AC23 调整；对话框改用 Spartan Combobox 选人，复用现有的候选人搜索流和已授予判断；授予与修改改用 Signal Forms、`injectFormSubmission` 与 Spartan Field，角色列表是两者共用的模板片段。
- Mock 同步：`_mock/api/distributed-event.ts` 的三个列表与四条按条件恢复分支、`webhook.ts` 的投递列表，都按 `startTime` / `endTime` 过滤；按 AC3 的写法规则和起止顺序返回 400（GET 接受 `Z` 与显式偏移，POST 只接受 `Z`）；Webhook 统计 Mock 保持现有分桶口径。`workspace.ts` 的平台人员数据返回 `linkedUsername`。对应的 Mock spec 一起更新。
- 文案三处同步：`public/i18n/{distributed-events,webhooks,tenants}/{zh-CN,en}.json` 和组件内的英文常量。

### 备选与取舍

- 列表输入只加 `StartTime`、上限复用 `Before`：字段更少，但 `Before` 的含义是"首次加载截止"，混入用户选择的上限后，按条件恢复的确认框说不清两者。采用独立的 `EndTime`。
- 方案 B（查询驱动、统计跟随列表时间范围）和方案 C（工作台、停靠详情）：偏离规范，且 B 要改统计接口。用户选了 A。

### 兼容与回滚

- 框架只增加可空属性和查询参数，方法签名、路由不变，不传新字段时行为不变，按非破坏的 `feat` 走 minor。模板沿用现有的模板包版本同步机制。
- 新前端配旧框架时，新增参数会被忽略：列表不按时间筛选，按条件恢复可能超出界面所选的区间。因此框架与模板在同一个 PR 里交付，回滚也一起回滚。
- `TenantStaffOutputDto` 增加字段，不影响现有消费方；`scripts/test-template-oidc-e2e.ps1` 只断言授予请求的字段和 200 状态，不受影响。
- 不涉及数据迁移。

## 3. 计划

全部在同一个 PR 里交付（目标 develop），框架、模板、Mock、文档一起改。

1. **框架时间范围**：两个组件的 DTO（含公共 XML 注释）、入口校验、EF 查询、`DistributedEventRecovery.cs`、端点、随包文档。验证：关系型 EF 测试覆盖边界、单边、缺省、与 `before` 叠加、按条件恢复的实际结果（AC1、AC2、AC4）；端点测试用现有宿主参数化（AC3）：GET 的 `Z` 与显式偏移为正例，缺时区与起止颠倒为 400；POST 的 `Z` 为正例，显式偏移、缺时区与起止颠倒为 400；按框架 L1 配方把受影响的包打到 `.tmp/local-feed`，检查包内容，并做隔离消费。
2. **模板后端与 Mock**：`TenantStaffOutputDto.LinkedUsername` 及前端 DTO；`TenantStaffTests` 回归列表里的真实值；三个 Mock 处理器与其 spec（AC1–AC4，其中 400 对应 AC3）。
3. **共享时间范围函数**：从操作记录抽出纯函数并改由它调用，操作记录现有 spec 保持通过。
4. **分布式事件前端**：AC1、AC4、AC5、AC8–AC15。验证：更新 `distributed-events*.spec.ts`、`distributed-event-table.spec.ts`、`distributed-event-service` 的 HTTP 参数测试。
5. **Webhook 前端**：AC2、AC6–AC8、AC14、AC15。验证：更新 `webhooks*.spec.ts`、`webhook-service.spec.ts`。
6. **平台人员前端**：AC16–AC23。验证：更新 `tenant-staff-sheet.spec.ts` 与 `e2e/workspaces.e2e.ts`（新的选人方式与按钮文案）。
7. **形态验证**（AC24）：在 worker-01 上经 remote-project-runner，用同一候选 feed 还原，生成代表形态并跑 lint、生产构建、受影响前端 spec、后端单元测试项目。形态至少包括：`identity-workspace`（两页与平台人员都在，含 `TenantStaffTests` 的 PostgreSQL 集成测试和现有 Workspace E2E）、关闭本地化、关闭 TenantWorkspace，以及不含 Webhook、分布式事件或操作记录的裁剪形态。代表场景由场景选择器给出，交给模板矩阵 `-Scenarios … -GenerateOnly` 生成后再构建测试；`test-template-generation.py --shard` 按全形态编号分片，完整形态证明交给 CI。新增文件或条件时，核对模板矩阵里登记的功能裁剪与资产断言。
8. **界面验收**（AC10、AC25）：生成项目配 Mock，用有头浏览器看 1440 与 390 宽、浅色与暗色，覆盖列折叠、菜单切换、右对齐日历浮层，以及刷新时保留原数据、失败可重试。
9. 送 Codex 做代码审查，明确"通过"后开 PR，CI 全绿后合并；填写 §4。

## 4. 结果

### 实际交付

一个 PR（目标 develop），提交按交付面拆分：规格、框架时间范围、模板实现，以及三轮代码审查的修复。

- **框架**：`Leistd.EventBus` 三个列表输入与三个恢复筛选、`Leistd.Webhooks` 投递列表输入加 `StartTime`/`EndTime`（UTC，两端闭区间）。校验在两个管理服务的现有入口各做一次；EF 查询与 `DistributedEventRecovery.cs` 按创建/接收时间比较，与 `Before` 同时生效；端点绑定查询参数。随包文档 `event-bus.md`、`webhooks.md` 写明参数与时间写法（GET 接受 `Z` 与显式偏移，恢复 JSON 只接受 `Z`）。
- **模板后端**：`TenantStaffOutputDto.LinkedUsername` 取关联账号在库里的真实用户名。
- **模板前端**：
  - 共享 `shared/utils/date-range-query.ts`：严格解析带时区的时刻（拒绝缺时区与不存在的日历日），换算 UTC 时保留全部小数位，按补齐 7 位的键比较；URL 回填、手输解析、格式化与日期换算三处共用，操作记录改为调用它。
  - 分布式事件、Webhook：按方案 A 重排；工具栏右组为时间范围 → 刷新 → 主操作；状态快捷切换；每行复选框（不可选禁用并说明原因）；选择条按动作计数；徽章带状态点；记录/投递 Sheet 按 AC15 调整；窄屏行操作收进菜单。Webhook 失败卡带统计响应的实际边界与订阅。分布式事件按条件恢复以最后一次成功加载的筛选与截止为准，加载中不可用；同条件刷新失败保留旧行，条件变化后失败清空。复制载荷与复制记录 ID 有反馈（用户走查时提出）。
  - 平台人员：抽屉加关联账号列、"已授予 N 人"、窄屏菜单与空态；授予/修改对话框按 D1 实现（Combobox 选人、账号预览、角色代码名、Signal Forms 提交时校验、修改时显示对象）。
  - Mock：`_mock/utils/time-range.ts` 统一按端点绑定规则解析时间；分布式事件、Webhook、Workspace 三个 Mock 同步。
- **顺带修复**：租户工作区侧栏"仪表盘"在其他平台页面保持选中（develop 既有缺陷，用户走查时提出）。落地页精确匹配只认无前缀路由，`/w/<名称>/platform` 退回子集匹配；改为去掉工作区前缀再判断。

### 与计划的偏差

- AC5 的"缺一端、坏格式、颠倒"之外，还把"缺时区、不存在的日历日"也当作未筛选；保留小数位与按 7 位比较来自代码审查。
- AC13 补充：列表请求进行中、或上次加载失败时，"按当前筛选"不可用（代码审查第 3、4 轮）。
- 计划里的"更多筛选"、已选条件 chip、新行高亮按非目标不实现；design.html 以本规格为准。
- worker-01 上用 `-SkipRuntime` 跑矩阵：执行器的 Docker 守卫拦截矩阵运行时冒烟阶段的 `docker rm -fv`，容器冒烟留给 CI。

### 验收记录

- **审查**：Codex 方案审查三轮后通过；代码审查四轮后通过（`41093f50`），之后只有一个 Mock 内部重构和一个 e2e 节奏修复。
- **框架定向测试**：`Leistd.EventBus.Tests`（时间范围、端点、管理用例）与 `Leistd.Webhooks.Tests`（端点、投递管理）全过；端点用例覆盖 GET 的 `Z`/偏移正例（含按 UTC 换算后的筛选结果）、缺时区与颠倒 400，以及恢复 JSON 的偏移/缺时区/颠倒 400。受影响 6 个包隔离消费通过。
- **worker-01 矩阵**（`-SkipPack -SkipRuntime`，全部通过）：

| 场景 | 后端单元 / 集成 |
| --- | --- |
| identity | 317 / 697 |
| standalone-external-login | 281 / 670 |
| identity-all-features | 339 / 823 |
| resource-capabilities-03 | 175 / 185 |
| webhooks | 323 / 721 |
| distributed-events | 323 / 728 |
| identity-workspace（含 PostgreSQL 集成测试与 16 条 e2e） | 339 / 854 |
| identity-capabilities-05（关闭本地化，含分布式事件） | 297 / 636 |
| resource-capabilities-09（关闭本地化，含 Webhook） | 175 / 240 |

- **identity-workspace e2e**：首轮 `workspaces.e2e.ts` 的授予用例在对话框刚打开时提交，被遮罩截获直至超时；改为等首个字段获得焦点再操作，并断言必填提示。用 Playwright 在 Mock 上以 1280×720 复现与验证。
- **界面**：生成项目配 Mock，有头浏览器看 1440 与 390、浅色与暗色；390 宽无横向溢出，投递日志行菜单与禁用原因、复制载荷反馈、授予对话框均按设计。

### 遗留项

无。

### 上收的常驻文档

- `framework/docs/components/event-bus.md`、`framework/docs/components/webhooks.md`：列表与恢复的时间参数及写法。

