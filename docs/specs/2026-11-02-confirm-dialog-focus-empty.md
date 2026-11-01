# 确认框忙碌焦点与空完成语义

状态：已完成 · 风险档：中

## 1. 需求

确认写请求期间，正文承接焦点却出现浏览器默认描边；action 不发值即完成时被误报为错误。本次采纳两项模板修复，并提供首包体积对照作为组件引入成本的依据。

验收标准：

- Given 两种确认框变体，When 确认进入忙碌态，Then 焦点位于正文且真实计算样式 outlineStyle 为 none；按钮禁用、取消与 Esc 受阻。由 confirm-service.spec.ts 和亮暗色浏览器人工验收覆盖。
- Given action 空完成，When 用户确认，Then 框关闭、open() 返回 false、无错误 toast。由 confirm-service.spec.ts 覆盖。
- Given action 发出 undefined 或 null，When 用户确认，Then open() 返回 true；错误仍 toast 一次并留框，重试与销毁清理不回归。由 confirm-service.spec.ts 及 users、tenants 的定向 spec 覆盖。
- Given 本地化开关两种生成场景，When lint 与生产构建，Then 均通过；静态闸门、读取成本与 Spartan 定制登记通过。
- Given identity-all-features 场景三个版本，When 以相同命令生产构建，Then 记录 Initial total 原始与估算传输体积及 stats 模块归因。

## 2. 方案

ConfirmDialog 在禁用按钮之前已将焦点转到 tabindex=-1 的正文。Spartan 1.9.0 的忙碌按钮示例使用 disabled + spinner；dialog/alert-dialog content 使用 outline-none。正文沿用这一非交互焦点落点呈现，不改变聚焦时机、错误后回焦或 aria-busy，也不修改 Helm 原件或 frontend-ui.md。

firstValueFrom 配置模块私有 Symbol 默认值；空完成返回该哨兵并关闭为 false，正常发值关闭为 true。closing$ 与 DestroyRef 的取消保护继续生效；error 通道继续留框并提示。未选 aria-disabled，因为它需要补防重入、键盘事件与样式处理；未选空完成成功或静默留框，因为两者都不能准确表达取消。

action 只承载一次写请求，成功必须发值；可取消的前置验证在 open() 前完成，需再认证重试、请求后接弹窗或跳转的流程不使用 action。空完成是防御性取消契约。

这是模板行为修复，不改框架包。回滚可撤销本次模板与规范改动；空完成恢复为旧错误语义。

## 3. 计划

1. 生成项目中增加回归断言并证明修复前失败，再将测试和修复写回模板；保护已有规范改动。
2. 重新生成本地化与非本地化场景，执行 confirm-service、users、tenants 定向 spec、lint、生产构建与静态闸门。
3. 临时 Mock 延迟请求，以键盘确认两种变体，验证 1440 宽亮暗色忙碌态并保存证据；还原环境并停止服务。
4. 对 PR #216 前、43110564 与本次候选构建同一全特性场景，记录首包与模块贡献。
5. 提交、推送并创建 develop PR，等待最终 head 的 Claude 审查与全绿 CI 后以 merge commit 合并。

## 4. 结果

已按策略完成正文焦点呈现、空完成取消语义与 action JSDoc；前端状态规范的已有修改原样随本次交付。

修复前回归准确失败于 outlineStyle=auto 与空完成留框；重新生成 identity 和 identity-all-features 后，确认框与 users、tenants 定向测试各 62 例通过，两个场景 lint、生产构建通过。50 道静态闸门、生成文档、读取成本（ui-change 52918/52932）与 Spartan 定制登记通过。

同一 identity-all-features 生产构建的 Initial total：PR #216 前为 raw 1,276,558 B / estimated transfer 279,417 B；43110564 为 1,282,707 / 280,814 B；本次为 1,283,093 / 280,875 B。本次净增 386 / 61 B；历史增量主要来自 confirm、empty 与全局 CSS，table-loading 位于懒加载块。

浏览器人工验收：1440×1000，亮暗色的 default/destructive 四组均由键盘 Enter 进入忙碌态，焦点为正文、真实 outlineStyle=none，截图目视通过。临时 Mock 与 environment 已还原，本任务会话与服务已关闭。

本地未执行全量矩阵、全量 spec 或后端测试，PR CI 按增量计划验证；最终 head 的 Claude 审查与全绿 CI 是合并条件。
