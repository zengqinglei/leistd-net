# 前端 spec 在测试环境关闭过渡与动画

状态：已完成 · 风险档：低 · 关联：下游反馈（前端 spec 等待动画在动画被取消时失败）

## 1. 需求

### 问题

模板前端的 spec 有 21 处在等动画结束，之后才量尺寸或检查浮层。

- 18 处使用 `Promise.all` 等待整个页面的动画，另 1 处仅等待 tooltip 子树内的动画。
- `dashboard.spec.ts` 使用 `Promise.allSettled` 等待整个页面的动画。
- `dropdown-side-switch.spec.ts` 循环等待菜单动画。

按 Web Animations 规范，动画被取消时，`finished` 会以 `AbortError` 失败。最常见的取消场景，是同一属性的 CSS 过渡被新过渡替换。页面上只要有一个动画被取消，整条等待就会失败，失败发生在准备阶段，不在断言里。

下游在 Linux CI 冷缓存下复现了这个问题：`hlmBtn` 的 `transition-all` 被重新起跑，触屏尺寸用例因此失败。在同一镜像里实测，产品尺寸没有问题。

`allSettled` 只能解决一半：被取消的过渡会立即 settle，但接替它的新过渡没有被等，量到的仍可能是中途值。

### 目标

- 统一一种终局写法，模板全量替换；下游三仓一次性跟进。
- 消除这类等待本身，而不是让等待更健壮。

### 已确认的事实（2026-11-19 查阅）

- **Angular 官方：** TestBed 默认关闭 `animate.enter` / `animate.leave`（`TestModuleMetadata.animationsEnabled` 默认 `false`），需要测动画的用例自行打开（angular.dev/guide/animations 的 Testing 一节）。但 Spartan helm 用的是按 `data-state` 切换的 Tailwind 动画类和 `transition-*`，不走 `animate.enter`，所以这个默认管不到模板的动画。
- **Spartan 官方仓库：**
  - brain 库同样用 Vitest 浏览器模式和 Playwright Chromium。它的 `libs/brain/src/test-setup.ts` 在全局注入 `*, *::before, *::after { transition: none !important; animation: none !important; }`，注释写明这样做是为了让组件状态可以同步观察，并且等同于"减少动效"用户。
  - helm 库不全局关闭。专测退场动画的 spec（如 `hlm-dialog-animation.spec.ts`）会自己注入 keyframes。
  - 生产代码里的 `waitForAnimations` 用 `allSettled` 加兜底计时器，也不在动画被取消时抛错。
- **Vitest 官方：** 浏览器模式下，等待可观察结果优先用可重试断言 `expect.element` / `expect.poll`，不写固定等待。
- **官方示例后台**（spartan-admin-dashboard，`a7ed735`，Spartan 1.9.0）：只有 6 个守卫、管道、服务的单测，没有组件或浏览器测试，不涉及这个问题，不能作为参照。
- **模板现状：** 除上面这些等待外，没有任何 spec 断言动画本身（方向、时长、是否在播放）。`dropdown-side-switch.spec.ts` 读取 `playState` 也只是为了等待。

### 验收标准

| 编号 | Given / When / Then | 覆盖 |
| --- | --- | --- |
| AC1 关闭 | Given 任意 spec；When 渲染带 `transition-*` 或 `data-state` 动画类的元素；Then `document.getAnimations()` 为空，尺寸和位置直接是终态 | 共享 setup 文件自带的 spec |
| AC2 清理 | 删除模板 spec 中 19 处 `Promise.all`、`dashboard` 的 `allSettled` 和 `dropdown-side-switch` 的循环 | 搜索 spec 中的 `getAnimations` 与 `.finished`，仅排除新增的 `template/frontend/src/motion.spec.ts`，结果为空 |
| AC3 回归 | 全部前端单测在代表形态下通过，细指针和触屏两组实例都要跑 | 生成场景的完整 `ng test` |
| AC4 规范 | 测试规范写明：测试环境关闭动效；异步状态用 `expect.element` / `expect.poll` 等待；需要测动画的用例自行局部打开 | 模板文档检查 |

## 2. 方案

### 三种做法的比较

| 做法 | 动画被取消 | 被新动画接替 | 与生产一致 | 下游迁移成本 | 官方先例 |
| --- | --- | --- | --- | --- | --- |
| 现状 `Promise.all(...finished)` | 整体失败（本次故障） | 不等 | 一致 | — | 无 |
| A. 每处改等具体的可观察条件 | 不受影响 | 不受影响 | 一致 | **高**：量对齐、未知终值这类几何断言写不出条件，58 处都要逐个人工判断 | Vitest（针对异步状态） |
| B. 共享 `settleAnimations()`：循环 `allSettled` 加上限 | 不失败 | 等 | 一致 | 低：一行替换 | Spartan 生产代码有类似写法，但用在运行时，不是测试 |
| **C. 测试环境全局关闭过渡和动画** | 不会发生 | 不会发生 | 动效不同，但终态布局相同 | **最低**：加一个 setup 文件，删掉等待 | **Angular TestBed 默认；Spartan brain 测试** |

### 决定：C，异步状态仍按 A

- **为什么选 C：**
  - 模板 spec 测的是行为和终态布局，没有一条在测动效本身。动效关掉后，量到的就是终态，问题类别直接消失，以后写新用例也不用再想"要不要等动画"。
  - Angular（TestBed 默认）和 Spartan（brain 测试）对测试环境的默认做法都是这样。
  - B 能修好这次的故障，但每个新用例仍然要记得调用辅助函数；漏调用时，只会在慢机器上偶发失败。
- **共享 setup 文件：** 新增 `frontend/src/motion.testing.ts`，登记到 `angular.json` 的 `test.options.setupFiles`，与 `web-fonts.testing.ts` 并列。
  - 内容：在 `document.head` 注入一个带固定 `id` 的 `<style>`，规则与 Spartan brain 相同：`*, *::before, *::after { transition: none !important; animation: none !important; }`。
  - 注释写明依据：Angular TestBed 默认关闭动画，Spartan brain 的测试也是这样做的。
- **需要测动效的用例：** 现在没有，不预建开关。规范里写一句：在用例内按 `id` 移除这个样式，并在 `onTestFinished` 里恢复（与 Spartan helm 专测动画时自己注入样式的做法相同）。
- **删除全部等待代码：** 等待前原有注释里关于"为什么要等"的说明（如 hlmBtn 过渡到 44px）随代码一起删掉。浮层打开、`aria-expanded` 这类异步状态，原本就用 `expect.poll` 或 `vi.waitFor` 等待，保持不变。
- **规范：** 在 `template/docs/standards/testing.md` 第 134 行那一段补一句：测试环境已关闭动效，不要等动画，异步状态用 `expect.element` / `expect.poll`。不另开小节。
- **分层：**
  - 单测层关闭动效，只验行为和终态布局。
  - 浏览器端到端（Playwright，`playwright.config.ts` 没有设置 `reducedMotion`）照常带动效运行，覆盖真实渲染路径。
  - 浮层带退场动画时的卸载时序，由 Spartan helm 自己的动画 spec 负责。
- **前提已核对：**
  - 模板组件和 `styles.css` 不靠 `animation-fill-mode: forwards` 才到达终态，也没有用 JS 的 `element.animate()` 做动画。注入的 CSS 管不到这两种，以后新增时要在用例里另行处理。
  - 视图过渡（`startViewTransition`）在相关 spec 里已经打桩，不受影响。
  - "减少动效"分支走 `matchMedia`，也不受注入样式影响。
- **不加 ESLint 规则：** 动效关闭后，残留的旧等待只会得到空数组，没有危害，不值得为它多加一条规则。

### 兼容

- 只改测试 setup、spec 和测试规范，不影响产品代码与运行时。
- **下游迁移：** 复制 `motion.testing.ts`，登记到 `setupFiles`；删除各处等待动画的代码。漏删的地方不会失败，可以分批清理。下游临时改成 `expect.poll` 的那一处可以保留。

## 3. 计划

| 任务 | 产物 | 验证 |
| --- | --- | --- |
| setup 文件 | `motion.testing.ts`、`angular.json` 登记、配套 spec（AC1） | 配套 spec 断言带过渡类的按钮 `getAnimations()` 为空、尺寸直接是终态 |
| 清理等待 | 21 处等待代码 | `git grep` 排除配套 spec 后为空（AC2） |
| 规范 | `testing.md` 一句话 | 模板文档检查、读取成本闸门、受限公开词扫描 |
| 回归 | 全部前端单测 | 在 worker-01 上经 runner 生成代表形态（开和关多语言、`identity-workspace`），跑完整 `ng test` 和 lint，细指针与触屏两组都要跑（AC3） |

## 4. 结果

- 共享 setup 关闭元素和伪元素的 CSS 过渡与动画。回归用例验证尺寸类切换后即可读到终态，页面无运行动画。AC2 清理了全部 21 处等待，扫描只排除配套回归断言。
- 工作台伪语言卡片用例原本直接聚焦末张卡片，浏览器滚动事件可能在 Spartan 打开提示后才派发。Spartan 收起提示后，旧用例隐式依赖退场动画留住提示 DOM，轮询才能读到文字。关闭动效时对照复现了提示提前卸载。用例现在先滚动，等到滚动事件派发再聚焦。提示和布局断言照旧。
- 测试规范已补充单测动效、可观察的异步等待和用例局部恢复样式的写法。
- 三种代表形态的完整 Chromium 单测和 lint 均通过。已逐路径核对 spec 发现结果和各形态的测试文件总数，细指针与触屏实例均执行。
