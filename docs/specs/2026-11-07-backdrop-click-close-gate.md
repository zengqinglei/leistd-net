# 遮罩点击经关闭闸门

状态：进行中 · 风险档：中

## 1. 需求

下游验收反馈：有的弹窗、抽屉点遮罩能关，有的不能，用户无从预判。现状（develop `67bf27e3`）：frontend-ui §2.7 要求业务弹窗打开前静态 `disableClose`、禁外点，退出只认 ✕、Esc、取消；不带 `app-dialog-close` 的只读弹窗与抽屉（如 Webhook 投递详情）按 brain 默认点遮罩即关。

禁外点是关闭闸门出现之前防误丢修改的做法；现在闸门已经覆盖这一点（有修改先确认放弃，无修改关掉不丢东西），继续禁外点只剩不一致。Radix/shadcn 的 Dialog、Sheet 与 Angular Material 默认都点遮罩即关；需要拦截时 Material 的官方写法正是 `disableClose: true` 加订阅 `backdropClick` 自行决定。AlertDialog 语义要求明确作答，不支持点遮罩关闭。

**目标**：业务弹窗与抽屉的遮罩点击与 ✕、Esc、取消一样经关闭闸门；确认类对话框（`ConfirmService` 弹出的确认，含放弃确认本身；确认身份）保留禁外点。Dialog 与 Sheet 同样处理。

**非目标**：不改 helm；不带 `app-dialog-close` 的只读弹窗、抽屉保持 brain 默认。

| 要求 | 验收（Given / When / Then） | 覆盖 |
| --- | --- | --- |
| 无修改直接关 | Given 带 `app-dialog-close` 的业务弹窗或抽屉无修改，When 点遮罩，Then 关闭 | `dialog-close.spec.ts`（弹窗、抽屉各一条）；浏览器：用户编辑 |
| 有修改先确认 | Given 有修改，When 点遮罩，Then 弹放弃确认；选继续编辑后弹窗仍在 | `dialog-close.spec.ts`；浏览器：用户编辑 |
| 写请求进行中拒绝退出 | Given busy，When 点遮罩，Then 不关、不弹确认 | `dialog-close.spec.ts`；`role-edit-dialog.spec.ts` 四个出口 |
| 确认类对话框不经遮罩关闭 | Given `ConfirmService` 确认（有无框内写请求）或确认身份已打开，When 点遮罩，Then 不关闭、不作答 | `confirm-service.spec.ts`、`reauthenticate-dialog.spec.ts`；浏览器：删除确认 |
| 嵌套只处理最上层 | Given 业务弹窗上叠着确认框，When 点遮罩，Then 只有最上层收到，确认框仍在、下层不关；放弃确认不叠第二个 | `dialog-close.spec.ts` 两条 |
| 只读抽屉不变 | Given 投递详情抽屉，When 点遮罩，Then 照常关闭 | 浏览器：投递详情 |

**方案**：业务弹窗与抽屉仍保持静态 `disableClose` 与 `closeOnOutsidePointerEvents=false`（brain `BrnDialog.dismiss` 在 `disableClose` 时丢弃遮罩与外部指针事件，CDK 自身此时只重新聚焦），不让 brain 自己关。`app-dialog-close` 以可选方式注入 CDK `DialogRef`（CDK 在弹窗内容注入器里提供，brain 的 Dialog 与 Sheet 都由它渲染），订阅 `backdropClick` 转给 `requestClose()`，随组件销毁取消订阅；放在弹窗外时没有 `DialogRef`，不订阅。确认身份对话框也用 `app-dialog-close` 承载 ✕、Esc 与取消，因此闸门组件增加 `closeOnBackdrop` 输入（默认 `true`），确认身份设为 `false`；`ConfirmService` 已 `closeOnOutsidePointerEvents: false`，不经闸门，维持现状。有遮罩时点击落在最上层遮罩上，嵌套时只有最上层的 `backdropClick` 触发。frontend-ui §2.7 改写为上述规则。

## 4. 结果

本地分支 `zengqinglei/backdrop-click-through-close-gate`，尚未推送。

- **前提实测**：`inject(DialogRef)` 在 brain 渲染的弹窗与抽屉内容里都拿得到（新用例经真实遮罩点击关闭，把注入改成 `null` 后这两条变红）。
- **修复前失败**：只加输入、不订阅时，`dialog-close.spec.ts` 有 3 条失败：无修改即关、有修改弹确认（期望关闭实际仍开、期望确认框实际为空），以及嵌套用例（前置状态达不到）。
- **变异检查**：去掉确认身份的 `[closeOnBackdrop]="false"`、`ConfirmService` 改为允许外点后，对应用例变红。
- **生成验证**：`webhooks`（本地化开）与 `identity-notifications`（本地化关），两者都含 Webhook 抽屉与订阅弹窗；lint、build 通过，`src/app/core` 与 `src/app/features/platform` 两轮分别 777/777、757/757；`scripts/check-all.ps1` 50 道闸门与读取成本闸门通过。
- **浏览器验收**（生成实例，有头）：用户编辑无修改点遮罩即关；有修改弹放弃确认，点确认框遮罩不叠第二个，选继续编辑后修改保留；删除角色确认点遮罩不关；新建订阅弹窗无修改点遮罩即关；投递详情抽屉点遮罩照常关闭。
- **常驻文档**：frontend-ui §2.7 改写。
