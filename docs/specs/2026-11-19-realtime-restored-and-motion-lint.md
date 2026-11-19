# 下游反馈：实时提示「已恢复」残留、动画等待的 lint 闸门、通知的同步级别

状态：已完成 · 风险档：中 · 关联：下游对 beta.465 的反馈（3 项）

## 1. 需求

### 条目与决定

| 编号 | 反馈 | 核对结果 | 决定 | 落位层 |
| --- | --- | --- | --- | --- |
| F1 | 「已恢复」显示的 2 秒内再次断线，「已恢复」一直挂着 | 属实。`realtime-status.ts` 最后一个 effect 在进入 `reconnecting` 时直接返回，清掉了淡出计时器，但没有重置 `restored`。`notice` 优先返回 `restored`，整个重连期间都显示「已恢复」，重连提示和倒计时也被盖住。`shown` 因此不会置位，连回后也不报告恢复。 | 采纳 | 模板 |
| F2 | 关动效后，spec 里残留的 `getAnimations` 等待会静默变成死代码，建议加 lint 规则 | 属实。全局关动效后 `getAnimations()` 恒为空数组，等待语句不报错也不起作用。develop 上只剩 `motion.spec.ts` 的自测断言。 | 采纳 | 模板 |
| F3 | beta.457 的说明写"前端依赖下游现在不用跟"，与下游跟 beta 的做法不一致 | 这句话出自给下游的反馈结论，不在 Release 正文里。事实是：框架 beta 只发 NuGet 包，模板前端依赖随模板正式版发出，框架包不依赖它们，所以不属于跟 beta 的必需项。但下游按模板基线手工吸收是现有做法，写"不用跟"会误导。 | 采纳：通知按同步级别标注 | 仓库规范 |

### 验收标准

| 编号 | Given / When / Then |
| --- | --- |
| AC1 | Given 「已恢复」正在显示；When 2 秒内再次进入 `reconnecting`；Then 「已恢复」立即消失。重连满 3 秒后显示重连提示；再次连回后重新显示「已恢复」，2 秒后消失。新增用例 `dismisses a restored notice when another outage begins`，修复前失败。 |
| AC2 | Given 单测 spec（`src/**`、`_mock/**` 下的 `*.spec.ts`）；When 调用任意 `.getAnimations()`；Then ESLint 报错，消息指向 `docs/standards/testing.md`。`src/motion.spec.ts` 豁免。e2e 不受限。原有 `no-restricted-syntax` 选择器在 spec 里照旧生效。 |
| AC3 | `docs/framework/versioning.md#发布说明` 规定：给下游的通知和回执逐项标"必需"或"可选"。必需项是框架包依赖下限和破坏性变更。模板侧改动（含前端依赖）标"可选"，写明随模板正式版发出、跟 beta 的下游可提前手工吸收，并给出入口。不写"不用跟"。下游反馈参考文档的回执一节引用这条规则。 |

## 2. 方案

- **F1**：在恢复提示的 effect 里，进入 `reconnecting` 时先 `restored.set(false)` 再返回。其他 effect 不动。
- **F2**：在 `eslint.config.mjs` 给单测 spec 加一个 `no-restricted-syntax` 选择器，匹配 `CallExpression[callee.property.name='getAnimations']`。flat config 里同名规则会整体覆盖，所以要连同该文件已有的选择器一起列出。`src/motion.spec.ts` 用单独的配置块豁免。专门测动画的用例确实要读动画时，用带理由的 `eslint-disable-next-line`。`testing.md` 现有那句只补半句"调用 `getAnimations` 由 lint 拦截"，注意读取成本闸门。
- **F3**：只改两处文档，各一两句，不加新流程。

## 3. 计划

| 任务 | 产物 | 验证 |
| --- | --- | --- |
| F1 | `realtime-status.ts`、spec 新用例 | 先确认新用例在修复前失败；组件 spec 通过，开、关多语言两种形态 |
| F2 | `eslint.config.mjs`、`testing.md` | 故意写一行违规确认报错，然后删掉；三形态完整 lint 通过 |
| F3 | `versioning.md`、下游反馈参考文档 | 文档检查、受限公开词扫描 |

## 4. 结果

- **F1** 再次断线时立即收起「已恢复」。新增回归用例在修复前卡在立即消失的断言，修复后覆盖完整的重连与恢复时序。开启和关闭多语言的实时形态均通过细指针和触屏用例。
- **F2** 单测 lint 拦截 `getAnimations()` 并指向测试规范，原有语法限制继续生效。反向验证覆盖 `src`、`_mock` 和豁免范围。开多语言、关多语言及原始默认形态的完整 lint 均通过，测试规范通过读取成本检查。
- **F3** 发布说明按「必需」「可选」标注同步项，下游反馈 Skill 的回执引用同一规则。Skill 和源文档引用检查均通过。[发布说明](../framework/versioning.md#发布说明)
- 交付见 [PR #304](https://github.com/zengqinglei/leistd-net/pull/304)。
