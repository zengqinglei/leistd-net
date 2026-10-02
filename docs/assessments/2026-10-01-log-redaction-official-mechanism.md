# 官方日志脱敏机制的实测结论（评估留档）

- **为什么留这份**：DEF-04（邮件日志泄露收件人）最终**没有**采用
  `Microsoft.Extensions.Compliance.Redaction`，改为"日志不记联系方式"。
  但评估期间实测出三条反直觉的事实，与路线选择无关；将来真出现
  "必须输出个人数据、且要集中管控策略"的需求时，这三条能省掉一轮踩坑。
- **实测环境**：dotnet/extensions 10.9.0，net10.0，本机 macOS。探针为一次性代码，已删。
- **本次的实际做法**：见 `framework/docs/components/email.md` 的「注意事项」与
  `framework/components/core/Leistd.Core/Redaction/TextRedactor.cs`（公共纯静态方法，框架与模板共用）。

## 三条实测事实

### 1. 普通模板日志永远不会被脱敏

`logger.LogInformation("{to}", "alice@example.com")` 在 `EnableRedaction()` 已开启时
**照样输出原文**。脱敏只作用于携带分类元数据的日志状态，而模板日志的
`FormattedLogValues` 不带分类。

因此"引个包、开个开关就脱敏"不成立：采用官方机制等于把每个要脱敏的调用点
改写成带 `[LoggerMessage]` 的源生成方法并给参数加分类标注。

**官方惯用法是把 `[LoggerMessage]` 方法放进同一个 `partial` 类里**，不另立旁挂的
`XxxLog` 类——否则每个有日志的类都要配一个伴生类。

### 2. `services.AddSerilog(configure)` 与 `EnableRedaction()` 冲突，会让日志全部消失

| 接线 | 结果 |
| --- | --- |
| `builder.Services.AddSerilog(configure)` + `EnableRedaction()` | **一条日志都不输出** |
| `builder.Logging.AddSerilog(logger)` + `EnableRedaction()` | 正常，且脱敏生效 |

两种调用顺序都试过，与顺序无关。失败形态是**全量日志静默消失**，不是丢字段——
比它要防的泄露更严重。模板当前用的正是前者（`Api/Hosting/LoggingExtensions.cs`），
而那个重载是为配置热重载有意选的；将来要接脱敏，必须先解决这个冲突并端到端确认日志还在。

### 3. 漏配是失败关闭，不是失败泄露

宿主漏调 `EnableRedaction()` 时，被分类标注的参数在日志里输出为空（`(null)`），
而不是原文。代价是丢掉那个字段的可观测性，不是泄露。

## 顺带记下的两点

- **HMAC 脱敏器仍是实验性**：`SetHmacRedactor` 带 `EXTEXP0002`，本仓库开了
  `TreatWarningsAsErrors`，采用需自行 `NoWarn`。要"跨行关联同一主体而不存原文"时才考虑。
- **包边界**：类库侧只需 `Microsoft.Extensions.Telemetry.Abstractions`
  （传递带入 `Compliance.Abstractions`），它同时满足「`Microsoft.Extensions.*`」与
  「`*.Abstractions`」两个子句，不违反 Core 包只依赖抽象的约束；
  完整 `Telemetry` 与 `Compliance.Redaction` 只有宿主需要。

## 本次为什么没采用

按设计原则 §5 第 5 条「一行文档能说明的，不写成代码」与第 4 条「只修默认就会出错」：

- 缺陷是"组件默认把个人数据写进日志"，其终局修法是**默认不写**，
  而不是建一套"能把个人数据安全写出去"的机制。
- 官方机制的代价：1 个新包、新组件家族、`Leistd.Core` 多一个传递依赖、
  模板改 Serilog 接线（失败形态是全量丢日志）、宿主多两行接线否则丢字段、
  15 处调用点改成全仓从未用过的范式。
- 而仓库里已有正确先例：`Leistd.Notifications.Email` 的渠道日志记 `{UserId}` 而非地址。

真正需要官方机制的场景是「**必须**输出这个值，且要集中管控策略」。出现那种需求时再回看本文。

## 命名依据（调研过官方与 ABP）

自写掩码该叫什么，取官方词汇而不是自创：

| 来源 | 该场景的词汇 |
| --- | --- |
| **微软** `Microsoft.Extensions.Compliance.Abstractions`（从 10.3.0 程序集读出） | 动作 `Redact` / `TryRedact` / `AppendRedacted` / `GetRedactedLength`；类型 `Redactor` / `NullRedactor` / `IRedactorProvider` / `IRedactionBuilder`；命名空间 `Redaction`；分类 `DataClassification` / `DataClassificationSet` / `TaxonomyName` |
| **ABP** | **没有掩码概念**。同类场景用 `[DisableAuditing]` 把值从审计日志里**排除**（置 null），不做掩码，因此不作命名参照 |
| 本仓库 | 无同类先例 |

据此定名：`Leistd.Redaction.TextRedactor`，放在 `Leistd.Core`
（框架邮件组件与模板三处调用点共用，不各写一份）。

**方法按"保留什么"命名，不按数据类型命名。** 一版草稿曾写成 `RedactEmail`，并计划"将来加
`RedactPhone` / `RedactNationalId`"——那等于让通用组件维护一份业务数据类型目录，
跟着业务长。哪些字段敏感、保留几位是业务策略；通用侧只提供策略，由业务决定自己的字段用哪个。
业务要对自己的多种类型集中管控时，归属是官方机制（在业务侧声明自己的数据分类），框架不代劳。
动词用官方的 `Redact`；**不**把静态辅助类命名成 `XxxRedactor`——`Redactor` 在官方是一个要继承的抽象类，
静态工具借这个名字会让人以为它能接进官方的脱敏管道。

日志字段名保留 `{ToDomain}` / `{EmailDomain}` 这种描述值本身的形态，而不是 `{ToRedacted}`：
字段名要对查询日志的人说清"这里是什么"，代价是将来若改脱敏形态，字段名要跟着改（对日志管道是破坏性的）。

## 顺带确立的日志口径

- **联系方式不进日志**：邮箱只记域名，手机号同理。取不到域名时记 `-`，不回落原文。
- **账号名可以进日志**：模板的用户名受 `^[a-zA-Z0-9_]+$` 约束、不可能是邮箱，
  是系统自身的账号标识而非联系方式，而且正是这些日志可读性的来源——
  换成 GUID 会让翻日志的人每条都得回库查一次。
- **IP 不进日志**：它只落库（会话、最近登录），属留存期问题，由留存作业处理。
