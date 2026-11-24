# 设置测试改为配置块测试区，撤销设置组件的测试与修改信息存储

状态：进行中 · 风险档：高 · 关联：#307（被本规格部分撤销）

## 1. 需求

### 问题与背景

#307（随 `v2.0.0-beta.510` 发布）按下游三份反馈给设置组件加了修改信息与按设置测试：`SettingRecord` 存最后写入人与时间，`SettingCheckRecord` 存每层每项的最近测试结论，写入依赖设置时把结论标记过期，模板设置页在输入框尾部放“测试”按钮、在说明区显示结论与“已过期”。

2026-11-24 在演示站评审时确认了三个问题：

- 邮件分组同时有输入框里的“测试”（只连接、认证）和底部的“发送测试邮件”（真实投递），两个都叫测试，结论可能互相矛盾。
- 测试验证的是一组配置，按钮挂在单个输入框上，会让人以为只测了这一项。
- 修改人与修改时间已由模板写进操作记录（`setting.changed` / `setting.cleared`，关键字可按设置名查到），设置表再存一份是重复；测试结论的留存、过期与并发计数是设置组件里最复杂的一块，却只服务于“日后看最近结论”，这个需要由业务按需记操作记录即可满足。

### 目标

- 设置页不再有输入框里的测试按钮。需要测试的配置块，在块下方放一个测试区：带边框的 Item 容器，不需要输入时一行一个按钮，一项输入（如收件人）时输入框与按钮同行，多项输入时默认收起、点开在容器内展开表单。
- 测试结果只用 toast 提示：成功自动消失，失败保留到手动关闭并给出具体原因；不保存、不显示历史、没有“已过期”。
- 设置组件不存测试结论，也不存修改信息。审计由项目按需写操作记录：有外部副作用的测试（真实发信）记一条，纯探测不记。
- 机密行的状态只由输入框占位符表达，标签下不再重复。
- 保留 #307 中与上述无关的部分：解密诊断（`SettingDecryptionException`、固定事件号、宿主设置单项隔离）、`SettingChangedEvent.IsCleared` 与 `setting.cleared` 整条链路、事件号段规范。

### 非目标

- 业务三仓的改动：由各自会话线在框架回复后同步，本规格只写回执里的同步项。
- 渠道等实体对话框里的测试（例如渠道测试发送）：属于业务领域，可选择复用模板测试区组件，不在本规格内改。
- 设置页“查看修改记录”入口：暂不做。

### 功能要求与验收

| # | 要求 | 验收（Given / When / Then） | 覆盖 |
| --- | --- | --- | --- |
| F1 | 设置接口不含测试与修改信息 | 读取设置 → 输出 DTO 没有 `IsCheckable`、`LastCheck`、`UserModification`、`TenantModification`；`MapSettings` 不再映射测试端点，GET 的授权判断回到 `canManage || AllowsUserScope` | Settings 端点与单测 |
| F2 | 存储回到只存值 | `SettingRecord` 没有修改信息三列；没有 `SettingCheckRecord`；`ISettingStore` 不再有修改信息的读写参数；`DefaultSettingManager` 构造函数不再要 `currentUser`、`timeProvider`、`checks`、`checkStore` | Settings EF 单测与编译 |
| F3 | 解密诊断不变 | 用另一密钥环写入的密文 → 读取抛 `SettingDecryptionException` 并记固定事件号；宿主设置单项解不开不影响其他项 | 既有 Settings 与 Hosting 单测 |
| F4 | 连通探测与部署基线撤除 | `Leistd.Email.Smtp` 没有 `ProbeAsync` 与 `SmtpProbeResult`；`Leistd.Settings.Hosting` 没有 `IHostSettingBaseline`、`HostSettingBaseline`，`HostSettingDefaultsProvider` 回到 #307 之前的内联查找 | 编译与 Email、Hosting 单测 |
| T1 | 迁移回到 #307 之前 | 用第 1 步新打包的框架生成 identity-all-features 与 resource-capabilities 场景 → 不含 `AddSettingAuditAndChecks`，快照与实体一致，没有待生成的模型变更 | 生成场景迁移检查 + 真实 PostgreSQL 集成测试 |
| T2 | 邮件分组测试区 | 邮件分组 → 底部测试区含收件人输入框与“发送”按钮，输入框里没有测试按钮；发送成功显示成功 toast；发送失败（均为 422：`TestEmailUnreachable` 连不上、`TestEmailAuthRejected` 认证被拒、`TestEmailRejected` 投递被拒，无法归类为 `TestEmailFailed`）按码显示该查的项，失败 toast 保留到关闭；仍记 `setting.test-email-sent` | 前端单测（用真实状态码）+ 集成测试 |
| T3 | 运维分组测试区 | 运维分组、部署允许配置导出且导出地址行显示（开关打开）→ 测试区是导出开关子项块的最后一项；测试区一个“测试”按钮；`POST api/v1/settings/telemetry/test` 用当前生效的导出地址与认证头探测，通过返回 200（空响应，与发信测试一致），失败按错误码返回 422；没有 `Settings.Operations` 返回 403；结果只 toast，操作记录数不变 | 后端单测 + 集成测试 + 前端单测；含一个不含邮件的生成场景 |
| T4 | 测试用当前生效的值 | 同组有写入在排队 → 点测试先等写入完成再发请求；有保存失败的输入 → 不发请求并提示先修正 | 前端单测 |
| T5 | 多项输入可展开 | 测试区声明可展开 → 默认收起为一行带箭头，点开在容器内显示表单，再点收起；触发器有 `aria-expanded` | 前端单测（组件自身，模板暂无多项输入的实际用例） |
| T6 | 机密状态只说一次 | 机密行 → 状态只由输入框占位符给出（“已设置（输入新值即替换）”或“未设置”），标签下没有说明行 | 设置页单测 |
| T7 | 词条与生成形态 | 新增、删除的词条在 `settings`、`operationRecords` 的 en 与 zh-CN、`setting-section.ts` 的内联英文表、后端 Resources 中一致；不含本地化的场景能拿到错误码 | `scripts/check-i18n-repo.py`、`template/scripts/check-i18n.py`、不含本地化的场景构建 |
| T8 | 视觉 | 1440 与 390 两档与评审画布一致 | 人工验收 |

### 已确认的决策

- 2026-11-24 用户：输入框里移除测试按钮，改为配置块下方测试区（带边框容器；多项输入展开）；默认不持久化、toast 提示、不显示历史。
- 2026-11-24 用户：持久化不放在设置组件里；修改了什么、测试审计由业务按需写操作记录。修改信息三列随之撤除（操作记录已能按设置名查到修改人与时间）。
- 2026-11-24 用户：机密行标签下的“已设置”与占位符重复，删除，只保留占位符。
- 2026-11-24 业务侧建议（用户已确认）：依赖开关的测试区作为子项块最后一项、与子项同一缩进；测试区随被测的行一起出现。模板遥测测试同样处理。
- 2026-11-24 用户：业务三仓由各自会话线同步；回复写在设置测试反馈文件末尾，通知媒体线会话并抄送总协调。
- 设计评审画布：`设置测试区设计`（现状、邮件发送、合规供应商、多项输入展开、手机 390 五张）。

## 2. 方案

### 现状事实

- 测试与修改信息的框架代码：`framework/components/settings/Leistd.Settings.Core/Checks/`、`Stores/SettingModification.cs`、`Leistd.Settings.EntityFrameworkCore/Entities/SettingCheckRecord.cs` 及其配置与 `Stores/EfCoreSettingCheckStore.cs`、`Leistd.Settings.Hosting/IHostSettingBaseline.cs` 与 `Configuration/HostSettingBaseline.cs`（`HostSettingDefaultsProvider` 经它查找默认值）；`SettingDtos.cs`、`ISettingStore.cs`、`EfCoreSettingStore.cs`、`DefaultSettingManager.cs`、`SettingManagementService.cs`、`SettingEndpoints.cs` 中的相应改动；`Leistd.Email.Smtp` 的 `ProbeAsync`。
- `SettingWriteLock` 由 #307 从 `DefaultSettingManager` 抽出，设置写入在用；`HostSettingsConfigurationProvider.TryGetCurrent` 是单项隔离在用。两者保留，前者若只剩一个使用者可内联回去。
- 模板后端：`Api/Configuration/SmtpConnectionCheck.cs`、`Api/Hosting/TelemetryExportCheck.cs`、`HostSettingBindings.AddSettingChecks`（含不跟随重定向、10 秒超时的具名 HttpClient）、`ComponentAuditHandlers` 的 `SettingCheckCompletedAuditHandler` 与 `SettingChecked` 动作、两套 `20261123120000_AddSettingAuditAndChecks` 迁移与快照（#307 之后没有其他提交改过它们）、`template.json` 对 `SmtpConnectionCheck` 及其单测的两条登记、`TenancyTests` 与三个集成测试里的相应引用。
- `SettingController` 整个文件在 `#if (LocalIdentity)` 内且 `!Email` 时排除；运维分组的 `Settings.Operations` 权限无条件存在，遥测设置在所有场景都有。
- 前端写请求的 5xx 一律按“可能已生效”处理，只保留错误码（`core/errors/application-http-error.ts`）；`TestEmailFailed` 现映射 503，失败 toast 实际显示通用文案。
- 模板前端：`setting-section` 的行内测试按钮、结论行、“已过期”、修改信息行，`setting-section.ts` 的内联英文表与 `KNOWN_CHECK_FAILURES`，`setting.dto.ts`，Mock 的 `checks` 路由与 `SETTING_CHECKS`、`SETTING_CHECK_RESULTS`、`TENANT_SETTING_MODIFICATIONS`；`hlm-input-group-button` 的 `pointer-coarse:min-w-11` 与 `frontend-spartan.md` 以“设置的测试”为理由的一行；发送测试邮件在 `setting-section/widgets/email-test`。
- 下游三仓的 `Directory.Build.props` 均为 `2.0.0-beta.465`，各自有业务侧的测试结果与修改信息过渡实现；模板 #307 之后未正式发布。

### 设计

**框架。** 删除按设置测试与修改信息：`Checks/` 整个目录及注册、`SettingCheckRecord` 及其配置与存储、`SettingModification`、DTO 末尾四个参数、测试端点（GET 授权回到 `canManage || AllowsUserScope`）、写入时推进修订号与标记过期的逻辑、`DefaultSettingManager` 构造函数为此加的参数、`IHostSettingBaseline` 与 `HostSettingBaseline`（`HostSettingDefaultsProvider` 恢复 #307 之前的内联查找）、`SmtpEmailSender.ProbeAsync` 与 `SmtpProbeResult`。`ISettingStore` 与 `EfCoreSettingStore` 回到只读写值。保留 `SettingDecryptionException`、`ProtectedPayload`、`SettingLogEvents`、`HostSettingApplier` 的单项隔离、`SettingChangedEvent.IsCleared` 与随包文档的排障一节；`settings.md`、`email.md` 删去测试与修改信息的段落。

**框架补充：发信失败分类。** `Leistd.Email.Core` 新增 `EmailDeliveryException` 与 `EmailDeliveryFailureKind`（`Unreachable`、`AuthenticationRejected`、`DeliveryRejected`），写法照 `ServiceClientException` / `ServiceClientFailureKind`。`SmtpEmailSender` 在连接、认证、发送三个阶段把能归类的 MailKit 异常转换为它并保留内部异常，其余（取消、发送阶段的超时与协议错误、其他状态码）原样抛出。消息只含主机与端口，不含收件人与账号。属于新增公共 API，非破坏；但直接捕获 MailKit 异常类型的调用方会收到包装后的异常。

**模板后端。**

- 删除 `SmtpConnectionCheck`、`TelemetryExportCheck`、`AddSettingChecks`、`SettingCheckCompletedAuditHandler`、`SettingChecked` 动作及词条、两套迁移（快照回到 `516d64aa^1`）、`template.json` 两条登记；改写以“清除会删掉修改信息”解释 `setting.cleared` 的注释（新理由：清除删行，写入与清除是两种动作，操作记录要分开）。
- 新增无条件的 `TelemetryExportController`：`POST api/v1/settings/telemetry/test`，`[Authorize(Policy = PermissionConstant.Settings.Operations)]`。读 `IOptionsMonitor<TelemetryExportOptions>.CurrentValue` 的地址与认证头，经自有具名 HttpClient（不跟随重定向、10 秒超时，不经 `TelemetryExportHandler`）向 `{地址}/v1/traces` 发空的 OTLP/HTTP 请求。导出开关关着也测（开启前先验证）。2xx 时动作正常结束（200 空响应，同发信测试）；其余抛业务异常：`TelemetryExportNotConfigured`（没有地址）、`TelemetryExportUnavailable`（部署关闭了 SDK，选项未注册）、`TelemetryExportAuthRejected`（401/403）、`TelemetryExportUnreachable`（连接失败或超时）、`TelemetryExportUnexpectedStatus`（其他状态）。纯探测，不记操作记录。
- 错误码状态：上述码与发信测试的码都显式映射 422（请求有效、这组配置不可用），登记进 `ApiExceptionMappingsTests`。`TestEmailFailed` 由 503 改为 422，前端才能显示原因。
- 发信测试按原因细分：`TestEmailUnreachable`（连不上服务器，含 TLS 握手与超时）、`TestEmailAuthRejected`（认证被拒，含服务器要求认证而未配账号）、`TestEmailRejected`（发件人、收件人或正文被拒），无法归类的仍为 `TestEmailFailed`（含服务器回 4xx 的临时认证故障）。分类来自框架（见下），模板不接触 SMTP 客户端的异常类型。

**模板前端。**

- 新增通用测试区组件 `setting-test-panel`：Spartan Item（outline）承载图标、标题、说明与操作区，内容投影放输入与按钮；`expandable` 时整行为展开触发器（`aria-expanded`），表单在容器内展开。组件只管布局与展开；请求、等待写入队列、toast 由调用方负责。
- `setting-section` 按分组渲染测试区：邮件分组改写 `email-test`（收件人 + 发送），运维分组新增 OTLP 测试（无输入）。测试区的位置与显隐统一按“被测的行”判定：被测行出现在哪一层，测试区就是那一层的最后一项——被测行是上级开关的子项时，测试区放在子项块末尾、与子项同一缩进；上级关闭或换成别的取值、被测行不在当前子项里时，测试区不出现（免得“先填地址”的提示指向看不到的输入框）。一组里有两块可测配置时各自贴在自己那块下面。后端端点本身不看开关。失败 toast 按 `error.code` 取本地词条（OTLP 各码、发信三个细分码与 `TestEmailFailed`、未知码兜底），不依赖服务端文案。
- 删除行内测试按钮、结论行、“已过期”徽章、修改信息行、内联英文表里的对应键与 `KNOWN_CHECK_FAILURES`；DTO 去掉字段；Mock 删 `checks` 路由与三份数据，新增 OTLP 测试路由（给出确定结论）。
- `hlm-input-group-button` 的 `pointer-coarse:min-w-11` 与 `frontend-spartan.md` 那一行：没有其他使用者则撤回，有则改写理由。

### 备选与取舍

- **留存最近结论但不显示历史：** 不做。留存就要处理过期与并发，复杂度回到原样；需要留存的场景（例如业务规则要求某修订测过才能启用）属于业务领域，由业务实体自己存。
- **保留修改信息三列：** 不做。操作记录已能按设置名查到，两处存储会不一致。
- **测试读存储值而不是生效值：** 不做，有意撤销 #307 的做法。生效值在本进程写入提交后立即应用；多副本时另一副本要等下一轮宿主设置刷新，与现有发信测试一致，接受。
- **测试失败返回 200 加结论体：** 不做。与发信测试的异常加错误码写法统一，前端走同一条错误处理。
- **新增一条删除迁移而不是撤掉原迁移：** 不做。模板与下游都未消费这条迁移；已执行过它的只有两个演示站，启动时的结构检查只看待执行迁移，不受影响，下次更新时手工清理。

### 兼容与回滚

- 框架破坏性变更，提交写 `BREAKING CHANGE` 脚注，逐项列出：
  - 修改信息：`SettingModification`、`SettingModificationDto` 移除；`SettingRecord` 的 `LastModificationTime`、`LastModifierId`、`LastModifierName` 移除；`ISettingStore.SetAsync` 与 `EfCoreSettingStore<TDbContext>.SetAsync` 去掉修改信息参数，`GetModificationsAsync` 移除；`SettingOutputDto` 去掉 `UserModification`、`TenantModification`。
  - 按设置测试：`Leistd.Settings.Checks` 命名空间下全部类型、`AddSettingCheck<T>`、`SettingCheckRecord` 及其配置、`EfCoreSettingCheckStore<TDbContext>`、`SettingCheckResultDto`、`SettingOutputDto` 的 `IsCheckable` 与 `LastCheck`、`MapSettings` 的 `POST checks/{name}`、`SettingEndpoints.CheckName` 移除。
  - 其他：`DefaultSettingManager` 构造函数去掉 `currentUser`、`timeProvider`、`checks`、`checkStore`；`IHostSettingBaseline` 移除；`SmtpEmailSender.ProbeAsync` 与 `SmtpProbeResult` 移除。
  - beta.510 的直接消费者需按上列同步；下游三仓尚未升级到 beta.510，不受影响。
- 框架：`SmtpEmailSender` 对可归类的失败改抛 `EmailDeliveryException`（内部异常为原 MailKit 异常）；直接捕获 MailKit 异常类型的调用方需改为捕获它或检查内部异常。
- 模板：`TestEmailFailed` 状态 503 → 422，并按失败原因细分错误码（连不上、认证被拒、投递被拒），同为 422。
- 演示站数据库已有 `SettingCheckRecord` 表与三列：新模型不映射它们，运行不受影响；下次演示站更新时备份后删除表、列与迁移历史行。
- 回滚即还原本 PR；旧规格 `2026-11-23-settings-audit-diagnostics-checks.md` 状态行注明部分被本规格撤销。

## 3. 计划

1. **框架**：按设计删除并调整测试与文档。验证：Settings、Email、Notifications 单测与框架构建；打包到 `.tmp/local-feed`，核对包内文档。
2. **模板后端**：删除探针、处理器、动作与迁移，新增遥测测试控制器、错误码与映射及单测。验证：基于第 1 步新包生成（清 NuGet 缓存）identity-all-features、resource-capabilities 与一个不含邮件、一个不含本地化的场景，构建、完整 UnitTests 项目（含控制器授权约定）、真实 PostgreSQL 集成测试、迁移无待生成变更、i18n 两个检查。
3. **模板前端**：测试区组件、邮件与运维分组接入、清理行内测试与 DTO/Mock。验证：组件与设置页单测、Mock spec、有头浏览器 1440/390 对照画布。
4. **审查与合入**：送 Codex 审查，PR，CI 全绿后合入 develop；旧规格状态行同步。
5. **回复下游**：在设置测试反馈文件末尾追加更正回复并通知。

不做的事：业务三仓的代码、渠道对话框测试、演示站数据库清理（随下次更新）。
