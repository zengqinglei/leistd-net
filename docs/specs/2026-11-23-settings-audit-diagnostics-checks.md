# 设置的修改信息、解密诊断与按设置测试

状态：进行中 · 风险档：高 · 关联：#306（Mock 拦截器）

## 1. 需求

### 问题与背景

下游反馈三份，都围绕"机密设置只写不读之后，管理员与运维怎么确认它是对的"：

1. **修改信息：** `SettingOutputDto` 没有修改人与修改时间，机密值不回显，出了问题无从判断是不是刚被人改过。
2. **解密诊断：** 解密失败的异常只有设置名，没有 key id；宿主设置一项解不开会让整批不生效，日志没有事件号；机密设置的 purpose 含设置名，文档没写改名会解不开。
3. **按设置测试：** 管理员只能靠"测试"确认机密填对了，且需要日后看到最近一次结论；设置的操作记录分不清写入与清除。

另外两项：业务事件号全部落在框架 41xx 段，需要统一的号段分配；模板 Mock 拦截器按字段猜测响应，已在 #306 单独修复，不在本规格内。

### 目标

- 设置接口按层给出最后写入的时间与人（Id 与名字快照），机密设置同样给出。
- 解密失败抛结构化异常，带 key id，并在解密点记一条固定事件号的日志；宿主设置单项解密失败只影响该项。
- 框架提供按设置测试：登记点、最近结果存储、执行端点；依赖设置被写入或清除时结果自动标记过期。
- 设置写入与测试完成发布本地事件，模板据此记操作记录。
- 框架组件与业务项目的日志事件号段写进规范。
- 模板设置页显示修改信息与测试结论，并自带 OTLP 导出与 SMTP 连通两个测试。

### 非目标

- 设置值的完整变更历史（由操作记录承担，不在设置表里存历史）。
- 租户连接串（`Leistd.MultiTenancy`）的解密诊断：另行回复后再处理。
- 解密失败的钩子：由固定事件号与专用异常满足（见 §2 取舍）。
- 用户层（个人偏好）的测试与操作记录。

### 功能要求与验收

| # | 要求 | 验收（Given / When / Then） | 覆盖 |
| --- | --- | --- | --- |
| A1 | 写入记录修改信息 | 已登录用户写入租户层值 → 该行 `LastModificationTime`、`LastModifierId`、`LastModifierName` 为本次写入的时间与当前用户；无主体写入时人为空 | Settings EF 单测 |
| A2 | 接口按层返回修改信息 | 租户层有值 → `TenantModification` 非空；用户层无值 → `UserModification` 为空；机密设置同样返回且不含值 | Settings 单测 |
| D1 | 解密失败结构化 | 用另一密钥环写入的密文 → 读取抛 `SettingDecryptionException`（是 `InvalidOperationException`），`KeyId` 等于写入时的密钥 Id，`SettingName` 正确 | Settings 单测 |
| D2 | 解密点固定事件 | 同上 → 记一条 Error，事件号为设置组件的解密失败事件，带 `SettingName`、`KeyId` | Settings 单测 |
| D3 | 宿主设置单项隔离 | 两个宿主设置，一个密文解不开 → 另一个照常生效，解不开的那项保持当前生效值 | Hosting 单测 |
| D4 | 校验失败整体回落 | 新值让 Options 校验失败 → 整组不生效，记固定事件号的 Error | Hosting 单测（既有用例改断言） |
| C1 | 登记与执行测试 | 登记测试的设置 → GET 返回 `IsCheckable`；POST 执行 → 返回并落库结论、失败码、时间与人 | Settings 单测 + 端点测试 |
| C2 | 依赖写入即过期 | 测试通过后写入或清除任一依赖设置 → 结果 `IsStale` 为真；再测后为假 | Settings 单测 |
| C3 | 机密解不开归一为失败码 | 探针读到解不开的机密 → 结论失败码 `secret-unavailable`，不抛给调用方 | Settings 单测 |
| C4 | 测试与写入的权限 | 无写权限的调用方 POST 测试 → 403，不执行探针 | 端点测试 |
| O1 | 事件供记录 | 写入或清除租户层值 → `SettingChangedEvent` 带 `IsCleared`；测试完成 → `SettingCheckCompletedEvent` | Settings 单测 |
| O2 | 模板记操作记录 | 清除租户层值 → 记 `setting.cleared`；执行测试 → 记 `setting.checked`；目标为层级与设置名，不含值 | 模板集成测试 |
| E1 | 号段规范 | 开发规范列出框架组件号段与业务号段；设置组件事件登记在 `settings.md` | 文档检查 |
| U1 | 设置页显示 | 机密行显示"已设置 · 人 于 时间 更新"；非机密覆盖行显示弱化修改信息；带测试的行显示测试按钮、最近结论与"已过期" | 模板前端单测 + 对比页人工验收 |
| T1 | 模板自带测试 | OTLP 导出、SMTP 连通各登记一个测试；探针按失败码返回 | 模板单测 |

### 已确认的决策

- 2026-11-23 用户：按最佳实践终局方案实施；界面改动先给前后对比网页确认。
- 修改信息存名字快照（与操作记录 `ActorName` 一致），不另建用户查询。
- "已过期"由写入时直接标记，不靠时间比较（清除会删行，时间比较发现不了清除）。

## 2. 方案

### 现状事实

- `SettingRecord`（`framework/components/settings/Leistd.Settings.EntityFrameworkCore/Entities/SettingRecord.cs`）无审计列；`EfCoreSettingStore` 有值新增或更新、清空删除行。
- `DefaultSettingProvider.Decrypt` 捕获 `CryptographicException` 抛 `InvalidOperationException`，只带设置名；写入端 `DefaultSettingManager` 计算回落值走同一函数。
- `HostSettingApplier.ApplyAsync` 逐项 `GetOrNullAsync`，一项抛出则整批中断；三个调用点各记 Warning，无事件号。
- 设置写入由 `MapSettings` 的 PUT 端点完成，框架组件不直接写操作记录；`SettingChangedEvent` 在提交后分发，不带值。
- 已用事件号：事件总线 4101–4107，操作记录 7100–7102；EF Core 自 10000 起（Core 10000、Relational 20000、Provider 30000、Provider design 35000，10.0.15 实测）。

### 设计

**修改信息。** `SettingRecord` 实现 `IModificationAuditedObject`，加 `LastModifierName`。存储在新增与更新时自己写三列（不依赖审计拦截器），语义为"最后写入"。DTO 末尾追加 `UserModification`、`TenantModification`（`SettingValueModificationDto(Time, ModifierId, ModifierName)`）。存储契约 `ISettingStore` 读取改为返回值与修改信息（`SettingValue`），破坏性变更写入提交脚注。

**解密诊断。** 新增 `SettingDecryptionException : InvalidOperationException`（`SettingName`、`Purpose`、`KeyId`）。key id 按 Data Protection 载荷格式从密文头读取（base64url 解码，魔数 `0x09F0C9F0` 后 16 字节），读不出为空。解密点记 `SettingDecryptionFailed` Error 后抛出。`HostSettingApplier` 对解密失败的项沿用配置源当前值，其余照常；Options 校验失败仍整组回落。

**按设置测试。** `ISettingCheck`（执行探针，返回 `SettingCheckOutcome`）经 `AddSettingCheck<T>(settingName, dependsOn)` 登记。`ISettingCheckService` 在当前层（租户上下文为租户层，宿主上下文为宿主层）执行，捕获 `SettingDecryptionException` 归一为 `secret-unavailable`，结果写 `SettingCheckRecord`（每层每项一行），发布 `SettingCheckCompletedEvent`。`DefaultSettingManager` 写入或清除某设置时，在同一事务里把依赖它的测试结果标记过期。DTO 追加 `IsCheckable`、`LastCheck`（`SettingCheckResultDto`：`Passed`、`FailureCode`、`Time`、`CheckerId`、`CheckerName`、`IsStale`）。`MapSettings` 增加 `POST checks/{name}`，授权与租户层写入相同。失败码基线：`not-configured`、`secret-unavailable`、`auth-rejected`、`unreachable`、`rate-limited`、`service-disabled`、`unexpected-status`。

**操作记录。** 模板已经订阅 `SettingChangedEvent` 记 `setting.changed`（租户层与宿主层，目标为"层级/设置名"）。事件增加 `IsCleared`，模板把清除记为 `setting.cleared`；订阅 `SettingCheckCompletedEvent` 记 `setting.checked`。框架只发事件，不依赖操作记录组件。

**事件号段。** 框架 1000–9999，每组件一个百号段，登记在 `docs/framework/development-guide.md`；设置组件 4200–4299。生成项目 50000–59999：50000–50999 留给模板代码，51000–59999 给业务，规则写进模板后端规范。

**模板。** 迁移 `AddSettingAuditAndChecks`（Identity、Resource 两套）。设置页：机密行与非机密行的修改信息放说明区；测试按钮 `sm`，输入类主设置放在输入框尾部（`InputGroup` + `InputGroupButton`），结论行不用字段错误语义，"已过期"用 `outline` 徽章；个人页不显示。OTLP 测试向导出地址发空的 OTLP/HTTP 请求按状态码归类；SMTP 测试只连接与认证，由 `Leistd.Email.Smtp` 提供连通探测。

### 备选与取舍

- **解密失败钩子：** 不做。与日志、异常传递的信息重复；告警走事件号，降级走异常类型。
- **"已过期"按时间比较：** 不用。清除会删行，比较发现不了；写入时标记过期与事务一致。
- **修改人只存 Id、由宿主查名字：** 不用。资源服务没有用户表。
- **`email-test` 并入测试：** 不并。它是发信动作，需要收件人，结果不是配置状态。

### 兼容与回滚

- `ISettingStore` 读取签名变化、`SettingOutputDto` 追加参数、`SettingChangedEvent` 构造参数变化：自定义存储与直接构造事件的代码需要改，写入 `BREAKING CHANGE` 脚注。
- 数据库新增三列与一张表，均可空或新表，不回填；回滚即移除迁移。
- 宿主设置单项失败不再整批回落：运行时语义变化，发布说明单列。

## 3. 计划

1. **框架 PR**（`Leistd.Settings*`、`Leistd.Email.Smtp`、开发规范）：修改信息、解密诊断、按设置测试、事件、号段文档、随包文档。验证：设置与邮件组件单测、打包与隔离消费、模板生成场景编译。
2. **模板对比页**：在生成项目 Mock 中实现设置页，截 1440 与 390 两档的现状与方案，发对比页给用户确认。
3. **模板 PR**（确认后）：迁移、OTLP 与 SMTP 测试、操作记录订阅、设置页、Mock、前后端单测、后端规范的事件号段。验证：受影响场景生成、构建、测试、迁移与真实 PostgreSQL 集成测试。

不做的事：租户连接串诊断、用户层测试、设置值历史表。

## 4. 结果

（实施后填写。）
