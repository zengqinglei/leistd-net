# 确认框内执行写请求

状态：已完成 · 风险档：中

## 1. 需求

用户于 2026-11-01 确认由模板先实现确认框内写入。现有确认即关闭让请求反馈脱离上下文，入口 spinner 会改变表格宽度。新增可选 action，成功才返回 true；失败留框、展示归一化错误，可重试或取消。旧用法兼容。

验收标准：

- Given action 确认框，When 确认并等待请求，Then 确认钮禁用且显示 HlmSpinner，取消/Esc 无效，焦点留框；成功关闭返回 true（ConfirmService spec）。
- Given 请求失败，When 框内收到错误，Then 正文 role=alert 展示既有归一化文案、确认钮复位并回焦；重试成功返回 true，取消返回 false（ConfirmService spec）。
- Given 写入遇到网络错误或 5xx，Then 继续显示结果不确定文案，不自动重放（ConfirmService spec + 既有 interceptor spec）。
- Given 调用组件销毁或程序化强制关闭，Then 框可退出并停止请求订阅，迟到结果不触发成功反馈（ConfirmService spec）。
- Given 无 action，Then 原确认/取消/Esc/标题/布局行为兼容（既有与新增 ConfirmService spec）。
- Given 已迁移调用点，Then 成功反馈、刷新留在调用方，失败无重复 toast，入口图标和列宽稳定且不会叠框（调用方与表格 spec；1440/390 Mock 浏览器人工验收）。

不改框架或下游项目，不发正式版；仅模板变更，不触发框架 beta。

## 2. 方案

现状事实：`template/frontend/src/app/core/feedback/confirm-service.ts` 动态打开 Vega 确认框、返回 boolean；`core/validation/form-submission.ts` 用 Promise 桥接提交，业务弹窗公开 `saveAction` 仍返回 Observable。HTTP 拦截器按方法生成 ApplicationHttpError，`applicationErrorMessage` 是展示入口；业务弹窗以静态 disableClose 和 busy 闸门处理用户退出。

- `ConfirmOptions.action?: () => Observable<unknown>` 与业务保存契约、HttpClient 一致，不扩展无消费需求的 Promise 契约。已有 Promise 服务在调用点用 defer 适配。
- 有 action 时静态 Brain disableClose，组件处理取消/Esc；不阻止程序化 close/forceClose。无 action 维持原行为。禁外点、无内置关闭钮。
- 标题区显式 `sm:grid-cols-[auto_1fr]`，防止短文案拉伸隐式图标列；描述与错误共享第二列容器，8px 间距，宽屏图标至文字固定 24px，窄屏保持居中（用户截图反馈追加要求）。
- 确认后焦点先移正文，避免禁用按钮丢焦；失败正文 role=alert，渲染复位后回焦确认钮；成功沿用 CDK 入口回焦。
- action 经 defer 在每次确认时重新执行，复用 applicationErrorMessage。确认失败不会 resolve 或自动重试；请求订阅在关闭/销毁时释放。可选 owner DestroyRef 把调用方销毁与动态框生命周期连起来。
- 原调用方的防重入标记覆盖整个确认过程，但不再传到行内组件或驱动 spinner/禁用。批量操作固定打开时选中的 Id。
- 核对全部 19 个确认后写入入口，迁移普通写入；涉及再次认证、凭据弹窗或整页跳转的 6 个流程保留原接力契约，逐项列入执行报告。纯放弃编辑确认继续无 action。

兼容：action/owner 可选，已有调用不变；回滚可恢复旧调用与规范，无数据结构或后端变更。

## 3. 计划

1. 实现服务、组件、生命周期与回归测试，迁移 13 个普通写请求入口并更新页面/表格 spec。
2. 同篇抵消规范新增字数，同步前端状态规则与文档入口；不增加词条。
3. 两种本地化场景前端全量单测、lint、构建；五个指定场景 GenerateOnly；静态闸门、读取成本、Spartan 定制检查。
4. 临时 Mock 浏览器覆盖用户启停与租户删除的请求中/失败态、1440/390 列宽及焦点，保存截图与 JSON，清理临时注入。
5. 核对最新 develop、提交推送并开 PR；CI 全绿、Claude 明确通过最终 head 后，以 match-head merge 合并。

## 4. 结果

交付本 PR 的模板实现、回归测试、§5 状态规范与读取索引。13 个普通写请求迁移；6 个涉及再次认证、凭据展示或整页跳转的入口保留原接力契约。追加截图反馈的网格约束与错误分组已落实。

验证：两场景前端全量分别 1,722 / 1,443 用例通过，追加后相关 spec 分别 126 / 105 用例通过；两场景 lint 与构建通过。五个指定场景生成检查、50 道静态闸门与 Spartan 定制检查通过；读取上限未调整。1440/390、亮暗、三种写入的 12 组浏览器验收均通过：24px 横向间距、8px 错误间距、列宽不变、失败无重复 toast、键盘留框与入口回焦。临时 Mock/服务器已清理。

与计划的偏差：像素单测受打开动画的缩放影响，改为计算样式断言列宽/间距，并以浏览器稳定态几何实测补证。编译、导入顺序与测试访问修复仅定向复验。纯模板变更，不发正式版、不出框架 beta；合并由最终 head 的 Claude 审查与 CI 双闸门约束。
