# 前端列表、表格与详情规范

通用样式与交互见[前端界面规范](./frontend-ui.md)。

## 1. 状态与数据

- 分页、排序、筛选只取 `queryParamMap`，用 `router.navigate({ queryParams })` 回写，非法值回退默认，不存 localStorage。
- 详情页 id 只取路由参数，分段／标签选中项放 query（可分享、可回退）；切 id 重载并丢弃迟到响应，状态留组件。
- 加载、失败与空态见[界面规范](./frontend-ui.md#1-设计原则)，服务端分页与排序见 [API §5](./api.md#5-分页规范)。

## 2. 数据表格

- 滚动列 `flex-1 overflow-y-auto` 加 `*:shrink-0`，避免 `overflow-hidden` 子项被压扁。
- 用 TanStack Table（`@tanstack/angular-table`，服务端 `manualPagination`/`manualSorting`/`rowCount`）、`app-table-paginator` 和 `app-faceted-filter`。
- 列按优先级 `primary`/`secondary`/`tertiary` 裁剪，`tableViewportSignal()` 给档位上限；吸附操作列的外框挂 `[appTableFit]="tableViewport()" [appTableFitContent]="rows()"`，放不下再降档，更新数据换新数组。主列用 `TITLE_COLUMN_META`、`TITLE_CONTENT_CLASS`，最窄档截断长名。
- 数据列标 `detail: true`；TanStack 按 `getRowId` 的实体 id 管展开，不另建状态；按定义顺序补 `hiddenDetailColumns(table)`，复用 `columnLabelKeys` 和 `ng-template[appColumnValue]`（`cell`/`detail`），显示全文和完整时刻；末尾补主行未列的额外字段。
- 有界标量、枚举、短文本随行返回，展开即渲染；无上界的快照、正文、JSON、日志、嵌套集合只返摘要，展开或开抽屉时按 id 查详情。分页子数据按需取，按行记状态、丢弃迟到响应。
- 值带按钮时 `dl` 加 `items-baseline`，`dd` 用 `flex items-center gap-1`，先值（`min-w-0 break-all`）后按钮（`shrink-0`），空值写「-」。
- 抽屉要在刷新或分享后还原，状态写入 query；右侧 Spartan Sheet 展示长详情，query 取实体 id，顶部列齐表格各列。
<!--#if (IncludeWebhooks)-->
- 抽屉示例：Webhook 投递日志的投递详情。
<!--#endif-->
- 可排序列（`column.getCanSort()`）在表头放官方排序按钮 `hlmBtn variant="ghost" size="sm"`，文字与列对齐；`[attr.aria-sort]` 用 `tableSortAria`，只给排序列，spec 用 `expectAriaSortOnSortedHeaderOnly` 测无排序、排序与换列。
- 骨架用 `app-table-loading`，传实际行高。表头（含吸附列）用 `bg-muted`，正文跟随行的常态与悬停；保留 Spartan 表头 `px-2`、正文格 `p-2`，只加首列 `first:ps-4`。时间列用 `appDate` 的 `short`，把 `full` 放入可聚焦 Tooltip；审计表格按秒对时，用 `full`。
- 操作按常用在前、破坏性在后；桌面 ≤3 项平铺图标按钮加 Tooltip，>3 项留两项，其余进「…」，`<sm` 全收。破坏性项用 `variant="destructive"`，以分隔线隔开。
- 只读溢出用 `readonly-list-popover`；摘要全可点、数量完整，浮层可聚焦并按视口限高。仅此处条目单行截断可免键盘与触屏全文入口。
- 禁用原因跟随控件，以 `aria-describedby` 关联；置灰按钮的 Tooltip 挂外层，菜单说明放该项下方（`text-xs text-muted-foreground`）。原因不进数据格；身份列保留两行，姓名截断、「你」徽章 `shrink-0`。

## 3. 后台列表页

- 指标卡 → `hlm-tabs-list variant="line"`（外层 `border-b`）→ 工具栏 → 表格 → 分页；Tab 若带计数，只标待关注异常。
- `app-stat-panel` 网格用 `grid-cols-2 @[54rem]:grid-cols-4`（三张卡用三列）；可下钻用按钮带筛选跳转。
- 异常数为零用默认前景色，正数用 `text-destructive`；对比数据缺失（如超保留期）显示「无可比数据」，不补零。
- 工具栏右组依次放 `hlm-date-range-picker`、`secondary` 刷新、当前 Tab 的主操作（若有）；仅记录列表有时间范围，切 Tab 清除。
- 状态用单选 `hlm-toggle-group`，再点回到全部；其他分类用分面筛选。
- 支持批量操作时，有勾选或筛到可批量状态才显示选择条；不可操作行保留禁用复选框，原因按 §2。
- 批量操作如需确认，写明条数、范围、后果及可能重复执行的副作用。
- 整行和「详情」均可开抽屉，行内控件各自响应。

## 4. 详情抽屉

- 记录详情 Sheet 窄屏占满，用 `data-[side=right]:w-full data-[side=right]:sm:max-w-2xl`。
- `hlmSheetTitle` 后紧接状态徽章，下行 `text-xs` 元信息和可复制 ID；操作贴标题行右侧，破坏性项收进「…」，按 §2 分隔。
- 失败原因与下一步置于正文顶部 `hlmAlert`；正文纵排分区，不用 Tabs，标题行 `text-sm font-semibold border-b pb-3`。
- `dl` 用 `grid-cols-[8rem_minmax(0,1fr)]` 对齐标签列；长标识用 `break-all` 等折行，不省略。
- 原始长文本用 `pre`（`bg-muted rounded-md font-mono text-xs`）；独立滚动块用 `max-h-*` 与 `overflow-auto`，载荷复制有反馈。
