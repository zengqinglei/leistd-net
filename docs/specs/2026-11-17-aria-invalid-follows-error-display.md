# 读屏无效状态与画面报错同步

状态：已完成 · 风险档：中（低到中） · 关联：[PR #290](https://github.com/zengqinglei/leistd-net/pull/290)

## 1. 需求

用户于 2026-11-17 确认：`aria-invalid` 与画面报错同源，以 Brain 的 `spartanInvalid`（含 `forceInvalid`）为准。提交前失焦不标无效；提交后空必填项也标为 `"true"`，改对与 reset 后移除。保留 `data-invalid` 和首错查找规则。

验收：Given 未提交的无效字段，When 失焦，Then 无错误与 `aria-invalid`；When 提交，Then 实际控件标无效并关联可见错误、焦点落在首错（含内部输入）；When 改对或 reset，Then 同步消除。由 `submit-error-state-matcher.spec.ts`、批量角色弹窗 spec 和生成前端 Mock 登录页有头抽查覆盖。

## 2. 方案

Helm host 覆盖 Brain host 绑定，内部 Brn 元素在 Helm 模板绑定。checkbox/switch/radio 的可操作元素由 Brain 私有模板生成且无 ARIA 输入；字段原本有效、之后无效时，稳定为 null 的 Helm 绑定也不会重写 Brain 的后续更新。两者在 Helm utils 用共用渲染后同步函数收口，普通输入无自身字段状态时由组合控件处理。自动态统一转换为 `"true"` 或移除；官方 `aria-invalid` 输入保留显式值优先语义（用户于同日确认）。不改 Brain。批量角色 fieldset 复用 `isSubmittedInvalid`。已有 OTP、设置行、两步验证的同步逻辑保留。

依据：[MDN aria-invalid](https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA/Reference/Attributes/aria-invalid) 要求空必填项在尝试提交后才标无效；[Angular host directive 执行顺序](https://angular.dev/guide/directives/directive-composition-api#host-directive-semantics) 允许宿主覆盖；[Material MatInput](https://github.com/angular/components/blob/main/src/material/input/input.ts) 同样从 `errorState` 推导。

## 3. 计划

1. 先在生成项目复现原始无效状态，再修改 Helm、批量角色与定向回归。
2. 更新 Helm 定制表与表单规范，检查读取预算。
3. runner auto 串行验证指定五形态的 lint、生产构建与受影响 spec；补受影响静态闸门和 Mock 登录有头抽查，开 PR 到 develop。

## 4. 结果

[PR #290](https://github.com/zengqinglei/leistd-net/pull/290) 已开至 develop，待用户审查合并。Helm 自动态与画面报错同源，官方手动覆盖语义保留；批量角色 fieldset 已接统一规则。五形态 lint、生产构建和受影响 spec、相关静态闸门、Mock 登录页有头抽查通过。表单规范、读取索引与 Helm 定制表已同步，读取预算不变。

上游已提 [spartan-ng/spartan#1881](https://github.com/spartan-ng/spartan/issues/1881)：Brain 改为按 `spartanInvalid` 绑定 `aria-invalid` 后，删除 `syncHlmAriaInvalid` 并撤销对应 Helm 定制。
