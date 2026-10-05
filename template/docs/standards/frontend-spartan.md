# Spartan 维护约定

加组件、升级 Spartan 或修改 `libs/ui` 中的 helm 组件时遵循本文。组件用法见 [前端界面规范](./frontend-ui.md)，组件 API 以项目 `spartan` Skill、本地 `libs/ui` 源码与锁定版本为准。

- **两层结构**：brain 层 `@spartan-ng/brain` 是无头基元，作为 npm 依赖引入、不改；helm 层是样式实现，通过 CLI **复制进本项目** `libs/ui/`，属自有代码，可自由修改。
- **加组件**：`ng g @spartan-ng/cli:ui --name=<comp>`，把对应 helm 组件生成到 `libs/ui/`。
- **升级**：升级 `@spartan-ng/brain` + `@spartan-ng/cli` 后跑 `ng g @spartan-ng/cli:healthcheck` 检查兼容性；**已改过的 helm 组件禁用 `migrate-helm-libraries`**（它会用上游版本覆盖自定义改动），需对照上游变更**手动合入**。为保稳定，锁定 brain / CLI 的小版本，只走官方 `healthcheck` 流程升级。
- **已定制的 helm 组件**（升级时逐个对照上游手动合入）：

| 组件                                              | 改动                                                                                      | 原因                                                                                                                                                                                                                             |
| ------------------------------------------------- | ----------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `button`                                          | `default` / `lg` 加 `pointer-coarse:h-11`，`icon` / `icon-lg` 加 `pointer-coarse:size-11` | 触屏设备的点按目标不小于 44px；`xs` / `sm` 是刻意选的紧凑尺寸，不改                                                                                                                                                              |
| `input`、`input-group`                            | 加 `pointer-coarse:h-11`                                                                  | 与按钮同高，表单里并排时对齐                                                                                                                                                                                                     |
| `select`（trigger）                               | `data-[size=default]` 下加 `pointer-coarse:h-11`                                          | 同上                                                                                                                                                                                                                             |
| `dialog`、`alert-dialog`、`sheet`、`popover`、`tooltip`、`select`、`combobox`、`navigation-menu`（内容与遮罩） | 进出场动画加 `motion-safe:` 前缀；`sheet` 内容、`navigation-menu` 内容与触发器箭头的过渡另加 `motion-reduce:transition-none` | 系统开启"减少动效"时不播放缩放、滑入。写法与上游 `dropdown-menu` 一致；Brain 关闭浮层时只等待正在播放的动画，没有动画就立即关闭 |
| `sidebar`（`hlm-sidebar`、`-menu-button`、`-group-label`、`-group-action`、`-menu-action`、`-rail`） | 宽度、位置、外边距与位移过渡加 `motion-reduce:transition-none` | 同上：折叠、展开侧栏时不播放滑动；颜色等非位移反馈不受影响 |
| `dropdown-menu`（`hlm-dropdown-menu-trigger.ts`） | 改 `menuPosition` 后调用 CDK 触发器的 `ngOnChanges`，让已建好的 overlay 更新定位策略      | 上游直接赋值，不经过 `ngOnChanges`，菜单打开过一次后再改 `side` / `align` 不生效；侧栏内容在桌面与手机抽屉间复用同一实例，用户菜单与区域切换器的方向随断点变化，会被摆错。由 `dropdown-side-switch.spec.ts` 钉住，上游修复后删除 |

用 `pointer-coarse` 而不是屏幕宽度判断：平板横屏很宽，但仍是手指操作。

升级步骤：升级 brain / CLI 后跑 `healthcheck`；未定制的组件用 `ng g @spartan-ng/cli:migrate-helm-libraries --libraries=<name>` 同步到新版本（传 `--libraries` 即非交互）；表中已定制的组件不执行覆盖式迁移，对照上游变更逐个手动合入。helm 与 CLI 版本脱节时，新参数与无障碍改进不会自动到位——升级 CLI 不等于 helm 已更新。
