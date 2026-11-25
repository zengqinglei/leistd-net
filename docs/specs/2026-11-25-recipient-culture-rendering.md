# 服务端按收件人的语言与时区渲染文本

状态：进行中 · 风险档：中（只改模板，框架只补一句随包文档） · 关联：待定（与 `2026-11-25-test-strategy.md` 同一 PR）

## 1. 需求

### 问题与背景

一个下游业务项目的后台作业生成员工提醒时，用 `CultureInfo.CurrentUICulture` 取资源。后台作业没有请求语言，取到的是默认英文，中文用户因此收到了英文提醒。另一个下游项目写对了，但靠的是自写的解析器，同一套逻辑在邮件里又写了一遍。

核对模板现状：

- **按收件人解析偏好，模板已有：**
  - `IAccountPreferenceStore.GetForUserAsync`：返回生效的语言与时区，按「个人 → 组织 → 部署基线」计算，资源服务向身份服务回源；
  - `IUserTimeZoneProvider.GetForUserAsync`：返回时区，三层都没有值时用 UTC。
  - 两者都要求调用方先处在收件人所属的租户上下文。
- **在收件人语言下渲染，手写了三遍：** `NotificationSecurityAlertPublisher`、`AccountCodeMailer`、`EmailChangedEventHandler` 各自「取语言 → 设 `CurrentUICulture` → finally 恢复」。
  - **只切了 `CurrentUICulture`。** 数字和日期格式走的是 `CurrentCulture`，仍按线程区域性。`NotificationSecurityAlertPublisher` 里的时刻还是在切换之前格式化的。
  - **语言为空时不切换。** 语言为空的情况有：单语言项目、三层都没有值、资源服务暂时读不到。不切换就沿用线程区域性。后台线程的区域性来自进程或容器环境，不是部署默认语言。
- **规范没有约定。** 规范里没有写「后台或为他人产出文本必须按收件人偏好渲染」，下游照着现有写法抄，就会出错。

### 取舍依据

- `docs/architecture/design-principles.md` §5。
- **落位在模板：** 偏好的定义和层级归模板管，框架只提供通用的 `ISettingProvider`。框架级的区域性作用域只是给 .NET 官方机制包一层，不新建公共 API。
- **优先官方机制：** 在异步方法里设置 `CultureInfo.CurrentCulture` 和 `CurrentUICulture`，它们随执行上下文流动，方法返回后不会泄漏给调用方（[.NET 文档](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-globalization-cultureinfo#culture-and-task-based-asynchronous-operations)）。
- **不过度设计：** 只设一个入口，调用点已有三处；通知发布器不加专用重载，不写分析器。

### 目标与非目标

- **目标：** 模板只用一个入口，按收件人渲染服务端文本。偏好只读一次；语言为空时回落到明确的默认语言，不沿用线程区域性；现有输出格式不变。规范写明这条约定。
- **非目标：**
  - 不改框架 API；
  - 不给 `INotificationPublisher` 加渲染重载；
  - 不写分析器；
  - 不迁移已存的通知快照；
  - 不改偏好的解析规则。

### 功能要求与验收

#### R1 统一渲染入口（模板 Application 层）

入参是收件人的用户 Id 和一个**同步**渲染委托，调用方已建立收件人的租户上下文。

- **偏好只读一次。** 每次渲染只调一次 `IAccountPreferenceStore.GetForUserAsync`，从同一份生效偏好里取语言和时区。时区 ID 复用 `UserTimeZoneProvider` 现有的 `TryResolve` 解析，三层都没有值或解析失败时用 UTC。不要再调 `IUserTimeZoneProvider.GetForUserAsync`：它内部会再读一次同一份偏好。
- **不新增读取。** 单语言形态下语言恒为空。调用方不需要时区时（例如没有时间字段的验证码邮件、邮箱变更邮件），入口不读取偏好，保留这些调用点现有的「不查偏好」路径，避免新增的读取变成投递的前提条件。
- **最终回落语言。** 有两种语言要区分清楚：
  - 偏好链里的部署基线 `DisplayDefaults.Language`，已包含在生效偏好里；
  - 生效语言为空或不在启用列表时的最终回落。

  生效语言为空，并且收件人就是当前请求的用户（本人操作触发的提醒或邮件，比如改密码、改邮箱）时，沿用本次请求的语言。原实现就是这样有意设计的：部署基线可以留空（跟随设备），这时本人的浏览器语言就是正确的语言。后台作业、他人触发的场景没有属于收件人的请求，走下面的最终回落。

  最终回落在本地化形态里取 `SettingConstant.Display.SupportedLanguages` 的首项（它同时是宿主的默认语言来源），在单语言形态里取 `en`（与模板英文文案一致）。Application 层不为此新增对 Api 层或框架本地化 Options 的依赖。
- **区域性只在委托执行期间生效。** 执行委托期间，同时把 `CurrentCulture` 和 `CurrentUICulture` 设为收件人语言，结束后在 finally 里恢复。委托是同步的，读取都在委托之前完成。
- **时区交给委托。** 委托负责把 UTC 时刻按该时区换算。调用点现有的时间格式保持不变，例如锁定提醒的 `InvariantCulture` 加 `yyyy-MM-dd HH:mm` 和时区标识。
- **恢复与取消。** 委托正常返回、抛异常，或调用方在读取阶段取消后，调用方的两种区域性都不变。

命名和目录按模板后端规范。凡是生成了 `IAccountPreferenceStore` 的形态都生成这个入口，与 `IUserTimeZoneProvider` 一致。资源服务形态虽然没有这三处调用点，下游业务代码同样要用它，不能只在有调用点的形态里生成。

#### R2 替换三处手写切换

- `NotificationSecurityAlertPublisher`、`AccountCodeMailer`、`EmailChangedEventHandler` 改用 R1 的入口，删掉各自的切换和恢复代码。
- 生成的文本与改动前一致，包括时间格式。只修正一处行为：语言为空且收件人不是当前请求的用户时，原来沿用线程区域性（后台是进程区域性），现在改用最终回落语言。

#### R3 测试

1. **修复前失败的证据：** 在三处现有调用点之一，观察真实输出的差异。条件是收件人不是当前请求的用户、生效语言为空、线程区域性与最终回落语言不同。修复前取错资源，修复后按回落语言取资源。新服务的测试因类型尚不存在而编译失败，不算行为红例。
2. 收件人语言与线程区域性不同时，资源按收件人语言取。
3. 两个不同语言的收件人并发渲染，结果互不串扰。
3a. 生效语言为空、收件人就是当前用户时，按请求语言渲染；收件人不是当前用户时，按最终回落语言渲染。
4. 经最终入口调用时，委托抛异常、或调用方在读取阶段取消后，调用方的两种区域性都不变。
5. 每次渲染只读取一次偏好；单语言形态下不需要时区的调用点不读取偏好。
6. 现有的时间样式和时区名断言保持通过。
7. 单语言形态能构建，测试通过。

#### R4 规范与文档

- **模板后端规范：** 在本地化或通知相关的现有章节加一条：为已知收件人生成要投递、持久化或交付的文本（通知、邮件等）时，一律经 R1 的入口按收件人偏好渲染，不读请求或线程的区域性。当前用户请求里常规的本地化查询照常使用请求区域性。模板规范的读取成本闸门只降不升。
- **框架通知组件的随包文档：** 加一句：`Title`、`Content` 是发送时的快照，由调用方按收件人语言渲染。

## 2. 方案

- R1 是一个模板内的应用服务，由调用方注入。不新增框架类型，不新增包依赖。
- **回滚：** 撤回这几个提交即可，三处调用点恢复原样，不涉及数据和配置。

## 3. 计划

| 步骤 | 内容 | 验证 |
| --- | --- | --- |
| 1 | R1 入口和 R3 的测试（先失败） | 受影响生成场景的单元测试和集成测试全量 |
| 2 | R2 替换三处调用点 | 同上；本地化、单语言，以及含邮件和通知的形态都要构建 |
| 3 | R4 文档 | 读取成本闸门、`scripts/check-all.ps1`；改了框架随包文档要打包，并跑包消费检查 |

## 4. 验证

### 修复前的行为红例

以框架候选包生成 `identity-all-features`，Release 构建通过。对原通知发布器加一条使用真实 JSON 词条的回归用例。没有当前用户，收件人没有语言偏好，调用方的 UI 区域性为 `zh-CN`。`dotnet test -c Release --no-build --filter FullyQualifiedName~A_recipient_without_a_language_gets_the_default_even_under_another_culture` 一条失败，期望标题 `Your password was changed`，实际发布 `密码已修改`。

本人请求语言回落另做行为对照。只在生成目录临时把本人缺值的分支改回模板首选语言，Release 构建通过。运行 `dotnet test -c Release --no-build --filter FullyQualifiedName~Security_alerts_use_request_language_only_for_the_current_user_with_no_language`，五组真实词条断言里仅本人缺值组失败，期望 `密码已修改`，实际 `Your password was changed`。恢复实现后运行单测全量，`identity-all-features` 371/371，单语言 `identity-notifications` 352/352。两种形态均以 `-p:TreatWarningsAsErrors=true` 完成 Release 构建。

### 生成形态的快速验证

实际生成后运行 `pwsh scripts/verify.ps1 -Fast`，`identity-workspace` 371/371 后端单测通过，Playwright 发现闸门通过（16/20）。远程认证的 `resource-workspace` 206/206 后端单测通过，该形态未生成 E2E 闸门。较小的 `identity-capabilities-05` 形态完成 Release 构建，未运行该形态的测试。

### 生成形态的完整验证

- 本地化 `identity-workspace` 运行默认 `pwsh scripts/verify.ps1`，371 条后端单测、872 条后端集成、2,249 条前端单测通过，默认 4 worker 的 16 条 E2E 全部首次通过。`verify.ps1` 墙钟 351.42 秒。
- 单语言 `identity-notifications` 同时生成邮件和通知。`dotnet build -c Release --no-restore -p:TreatWarningsAsErrors=true` 通过，分别对单测和集成测试项目运行 `dotnet test -c Release --no-build`，352/352 和 742/742 通过。
- 远程认证 `resource-workspace` 用同样的 Release 命令验证，206/206 单测和 278/278 集成通过，覆盖远程账户偏好登记形态。

### 框架随包文档

`pwsh framework/build/pack-local-feed.ps1` 打出 82 个 `1.2.3-local.20261125090424` 候选包。`pwsh framework/build/test-package-consumption.ps1` 通过独立解包及消费检查，编译 28 个项目中的 165 段文档示例，拒绝 6 个预期反例。通知框架本轮只补充文本快照的消费契约说明。

### 仓库闸门

`pwsh scripts/check-all.ps1` 通过全部 52 道静态闸门，含模板规范读取成本和文档引用。`pwsh template-package/test-template-package.ps1` 对 `bec77530` 打包 `0.0.1-local.20261125092925`，包内 1,727 个文件与源码一致。隔离安装后，`identity-all-features` 和 `resource-capabilities-03` 核对来源标记并重建，两种形态归一化后均为零差异。
