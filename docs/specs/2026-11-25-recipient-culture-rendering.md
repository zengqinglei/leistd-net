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

- **目标：** 模板只用一个入口，按收件人渲染服务端文本。语言同时作用于资源和格式化；语言为空时回落到部署默认语言，不沿用线程区域性。规范写明这条约定。
- **非目标：**
  - 不改框架 API；
  - 不给 `INotificationPublisher` 加渲染重载；
  - 不写分析器；
  - 不迁移已存的通知快照；
  - 不改偏好的解析规则。

### 功能要求与验收

#### R1 统一渲染入口（模板 Application 层）

入参是收件人的用户 Id 和渲染委托，调用方已建立收件人的租户上下文。入口负责：

- 用 `IAccountPreferenceStore.GetForUserAsync` 取生效语言，用 `IUserTimeZoneProvider.GetForUserAsync` 取时区。
- 在委托执行期间，同时把 `CurrentCulture` 和 `CurrentUICulture` 设为收件人语言。语言为空或不在启用列表里时，用宿主本地化配置里的默认语言，不用线程区域性。
- 把时区交给委托，委托负责把 UTC 时刻按该时区换算后再格式化。
- 委托正常返回、抛异常或被取消后，调用方的区域性都不变。

命名和目录按模板后端规范。凡是生成了 `IAccountPreferenceStore` 的形态都生成这个入口，与 `IUserTimeZoneProvider` 一致。资源服务形态虽然没有这三处调用点，下游业务代码同样要用它，不能只在有调用点的形态里生成。

单语言形态下语言恒为空，按默认语言渲染。

#### R2 替换三处手写切换

- `NotificationSecurityAlertPublisher`、`AccountCodeMailer`、`EmailChangedEventHandler` 改用 R1 的入口，删掉各自的切换和恢复代码。
- 时刻的格式化放进委托，在收件人区域性下进行。
- 生成的文本和改动前一致，只修正上面两处缺陷：切换之前格式化、语言为空时沿用线程区域性。

#### R3 测试

单元测试或集成测试覆盖以下情况，其中前两条要有修复前失败的证据：

1. 收件人语言与发起人或线程区域性不同时，资源文本和日期格式都按收件人语言。
2. 在后台路径（没有请求、线程区域性与默认语言不同）下，语言为空时按默认语言渲染。
3. 两个不同语言的收件人并发渲染，结果互不串扰。
4. 委托抛异常后，调用方的区域性不变。
5. 单语言形态能构建，测试通过。

#### R4 规范与文档

- **模板后端规范：** 在本地化或通知相关的现有章节加一条：服务端为他人或在后台产出文本（通知、邮件、导出等），一律经 R1 的入口按收件人偏好渲染，不读请求或线程的区域性。模板规范的读取成本闸门只降不升。
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

（实施后填写。）
