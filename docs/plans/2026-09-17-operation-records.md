# 操作记录改造实施计划

> 已选方案的可执行任务、顺序与验收。**全部任务完成后删除本文**，有效结论上收到稳定文档。
>
> 方案依据：`docs/assessments/2026-09-17-operation-records-readability.md`（§10 最佳实践终局）。
> 决定：**终局全量纳入改造**。

## 0. 计划前提

| 事实 | 依据 | 对计划的影响 |
| --- | --- | --- |
| 0.x 期破坏性变更按 **Minor** 递增（`0.12.0` → `0.13.0`） | `docs/framework/versioning.md:22` | **不做"旧签名并存 + `[Obsolete]`"支线**，直接改签名 |
| 框架只出 `ConfigureOperationRecords()`，迁移在模板 | `Leistd.OperationRecords.EntityFrameworkCore/DependencyInjection.cs`；`template/.../Infrastructure/Persistence/Migrations` | 字段与子表：框架改配置，**模板出迁移** |
| `AuthController`／`ConnectController` **整文件**包在 `#if (LocalIdentity)` | 两文件首行 | `auth.*` 只存在于 Identity／Standalone；动作码常量、i18n、前端筛选项都要条件裁剪，并受矩阵 `ForbiddenTokens` 检查 |
| `AuthController` 已有 `end-impersonation` 端点 | `AuthController.cs:97` | §10.5 的 `impersonation.*` 有现成挂载点 |
| 跨交付面只维护**一份**根仓库计划 | `maintaining-leistd-repository` | 不在 `framework/`／`template/` 复制计划 |

**分层原则**：`framework/` 提供能力与契约，`template/` 消费并给出可运行事实，两层不复制权威事实。每层各自验证（技能工作流第 4 步）。

## 1. 任务总览与顺序

顺序按**价值**排，不按改动量。理由见 assessment §10.11：任务 1–4 解决「根本没记」，任务 5 解决「看不懂」。

| # | 任务 | 交付面 | 阻塞关系 |
| --- | --- | --- | --- |
| 1 | 补齐两个高危缺口的留痕 | template | 无，可立即开始 |
| 2 | 事件定义注册化 | framework + template | 阻塞 3／4／5 的词条闸门 |
| 3 | 契约扩展：目标名、失败原因、变更明细 | framework + template | 依赖 2 |
| 4 | 覆盖面：认证事件与配置变更 | template | 依赖 2、3 |
| 5 | 可见性分层与模拟登录透明度 | framework + template | 依赖 2、3 |
| 6 | 展示改造：句子化 + 四列 + 筛选 + 抽屉 | template 前端 | 依赖 3、4、5 |
| 7 | 导出 | template | 依赖 6 |
| 8 | 留存、不可篡改、容量策略 | framework + template | 依赖 3 |
| 9 | 文档上收与 assessment 删除 | docs | 依赖全部 |

**任务 6 不得早于 4。** 单独上线句子化会让日志「读起来像交代完了，却说不出改了什么」——比裸码更容易误导（assessment §10.4 末段）。

---

## 2. 任务详情

### 任务 1：补齐两个高危缺口

**背景**：`user.roles-replaced` 与 `permission-grants.replaced` 两个常量、应用服务、端点**全部存在**，却既无记录调用也无注解 —— 成功与被拒两条路都不留痕。它们是「改一个人能做什么」与「改一个角色能做什么」。

- [x] `UserAppService.ReplaceRolesAsync`（:371）补 `RecordSucceededAsync`
- [x] `PermissionAppService` 的角色授予替换补 `RecordSucceededAsync`
- [x] `UserController` `PUT {id}/roles`（:134）补 `[OperationRecordAction(..., "id")]`
- [x] `PermissionController` `PUT grants/roles/{roleId}`（:66）补注解，`TargetIdPrefix = "Role/"`
- [x] 补 `UserAppService.UpdateAsync` 的成功记录（现状：被拒才记，成功不记）

**验收**：集成测试断言四个端点在成功与 403 两条路径各落一条记录，且动作码逐字一致。

### 任务 2：事件定义注册化

与 `IPermissionDefinitionProvider` 同构（`Leistd.Authorization.Core/Abstractions/`）。

- [x] framework：`IOperationActionDefinitionProvider` + `IOperationActionDefinitionContext` + 注册表（`internal sealed`，重复注册启动期抛异常，与权限一致）
- [x] framework：`OperationActionDefinition` = `Code` / `Category` / `Severity` / `TracksChanges` / `Visibility`
- [x] **`Visibility` 必填、无默认值** —— 直接沿用权限定义里 `MultiTenancySides side` 的设计理由：省略时静默落到租户可见就是越权，且只在真建了租户后才暴露
- [x] template：`OperationRecordActions` 常量类改为定义提供者
- [ ] ~~闸门：扩展 `scripts/check-i18n-keys.ps1`~~ —— **归属改到任务 6**。它要断言的 `operationRecords.actions.*` 词条要到任务 6 才存在；现在写闸门会立刻全红，等于给自己造一个必然失败的门。

**验收**：启动期重复码抛异常；模板生成后能编译并解析出 `IOperationActionDefinitionManager`。

### 任务 3：契约扩展

- [x] framework：`OperationRecordInfo` / `OperationRecord` 加 `TargetName`、`FailureCode`、`FailureData`、`FailureDetail`
- [x] framework：新增 `OperationTarget`、`OperationFailure` 两个值对象（assessment §6.2、§7.1）
- [x] framework：`IOperationRecorder` 两方法签名改为 `(action, OperationTarget, basis, [OperationFailure], ct)`
- [ ] framework：`OperationRecordChange` 子表（`Field` / `OldValue` / `NewValue` / `OldDisplay` / `NewDisplay`），ID 与显示名双存
- [ ] framework：**字段 allowlist 注册机制** —— 只有显式登记为可审计的字段进 diff
- [x] framework：各字段长度上限与截断（沿用现有「就地截断不抛异常」策略）
- [x] framework：`OperationRecordConfiguration` 加列与子表映射
- [x] template：迁移 —— **改基线而非追加**。依据模板 `backend/README.md`「模板携带可审查的基线迁移」：模板分发的是新项目的起点，不该一上来就跑「建表 + 立刻加五列」两个迁移。共改六处：`Identity`/`Resource` 两套基线迁移的建表列与新增索引、两份 `ModelSnapshot`、两份 `.Designer.cs` 内嵌快照。**Designer 差点漏掉**——它记录"该迁移执行后模型长什么样"，漏改不报错，但下次 `migrations add` 算出的差异是错的。快照里属性按**字母序**排列，插错位置同样无症状而后患无穷。
- [x] template：4 处现有调用点改用 `OperationTarget`；目标名取 `DisplayName ?? Name/Username`（assessment §6.4）

**两条不可退让的约束（写入 XML 注释，否则后人会当缺陷"修复"掉）**：

1. **不提供接受任意 `Exception` 的重载**，只接 `BusinessException`。技术异常必须显式 `FromDetail(...)`，强迫开发者逐次决定哪段文字可给租户看 —— 杜绝 `catch (Exception ex)` 把 `SqlException` 的表名列名、`HttpRequestException` 的内部地址灌进租户可读的表。
2. **diff 字段 allowlist 而非 denylist**。自动 diff 全部变更属性必然写进 `PasswordHash`、`SecurityStamp`、令牌 —— 一次漏配就是凭据落库。

**验收**：框架单测覆盖截断、allowlist 拦截、`OperationFailure` 三个工厂；包内 XML 含新签名。

### 任务 4：覆盖面

- [x] template：`auth.*` 事件（login.succeeded／failed、logout、password.changed／reset、mfa.enabled／disabled、external-login.bound／unbound、token.issued），**整组包 `#if (LocalIdentity)`**
- [x] template：`impersonation.started` / `.ended`（挂 `AuthController:97` 既有端点）
- [x] template：`tenant.*`、`setting.changed`（含作用域 Host／Tenant）。**`SetForCurrentUserAsync`（用户改自己的时区/语言偏好）刻意不记**：判据见 assessment §10.3——值得留痕的是能改变「谁能做什么」「某数据是什么」「某人是谁」的操作，个人偏好两样都不改变却是高频写入，正撞上常量类原注释的「审计表的价值来自密度」；Django、Shopify 同样不审计个人偏好。这同时消解了一个可见性冲突：若记用户级设置，`setting.changed` 一个码要同时承载 `Actor` 与 `Tenant` 两种可见性，而可见性按定义登记。只记租户/宿主级之后，宿主级变更写入时 `currentTenant.Id` 为 null、记录天然落到宿主侧，`Tenant` 可见性对它等价于"仅宿主可见"，定义无需改动。
- [x] template：`operation-records.exported`（任务 7 的被审计对象）
- [x] 推翻 `OperationRecordActions` 注释里「登录尝试……不该记」：解法是容量策略（任务 8），不是不记

**验收**：矩阵 9 场景全绿 —— 尤其 `resource` / `resource-notifications` / `resource-localization` 三个场景**不得出现任何 `auth.*` 符号**（`ForbiddenTokens` 会检查）。

### 任务 5：可见性分层

- [x] framework：`OperationVisibility` = `Tenant` / `Host` / `Actor`，**记录级 + 字段级**
- [x] framework：`FailureDetail` 与 `CorrelationId` 标记为仅宿主可见
- [x] template：查询按当前主体的层级过滤
- [x] template：`impersonation.*` 为 **Tenant 层**（不是 Host）
- [ ] template 前端：模拟登录记录**视觉区分**，不只靠一行小字

**这是信任属性**：一个能让宿主悄悄操作而租户看不见的审计系统，对租户没有价值。

**验收**：集成测试断言租户主体查询时读不到 `Host` 层记录与 `FailureDetail`／`CorrelationId` 字段；租户能读到针对自己租户的模拟登录记录。

### 任务 6：展示改造

- [x] 四列：时间 ｜ 操作人 ｜ 操作内容 ｜ 结果（Django 形态，**句子不含操作人**，避免一行印两遍）
- [x] 句子模板：整句进语言包 + 具名占位符，**绝不 join 片段**（Discourse 中文译文翻转占位符顺序是硬证据）
- [x] 三级降级 + 未知码原样显示（assessment §5.3）
- [x] **动作码词条的键结构：扁平点号键可用，维持现状**（已查证 transloco `^8.4.0` 官方源码）。运行时 `flatten()` 把整棵 JSON 拍平成单层点号键映射，`translate()` 走 `translation[key]` 直接查表；`flatten` 对叶子直接写 `result[prop] = curr`，因此字面量键 `"user.created"` 原样保留，与嵌套 `{user:{created}}` 拍平后**得到同一个键**，两者等价。
  ⚠️ **教训留痕**：查证前我一度把**自己刚写进去的 22 条**当成"已有先例"——循环论证。真实情况是：排除本轮新增后，既有 557 个键里键名含点号或连字符的**为 0**，且 `node_modules` 未安装、无法从依赖源码查证。**结论来自官方文档，不是来自我的推断**；这次查证省掉的是一次本会凭感觉推倒重来的大改。
- [x] **失败原因在前端渲染**：后端渲染会导致切语言后停在旧语言直到刷新（transloco 纯客户端切换，不重新请求）
- [ ] 筛选栏：类别／动作／结果／操作人／目标／时间范围；宿主视角多租户维度

  ⚠️ **这一条被写成一个复选框，但它不是前端活，量级与任务 5 第二批相当**（已查证）：
  - `IOperationRecordStore.GetPagedListAsync` 目前只有 `keyword / startTime / endTime / skip / take / scope`，**没有类别、动作码、结果三个维度** → 要**第三次**改这个签名（前两次：加时间区间、加可见范围），并同步 EF 查询、2 个 Store 替身、12 处 Store 测试调用；
  - `IOperationActionDefinitionManager` **只在 DI 里注册过，从未对外暴露** → 前端拿不到"有哪些类别、哪些动作码"。硬编码 20 个码正是任务 2 引入注册式定义要消除的东西，所以要新增一个**下发筛选项**的端点；
  - `OperationRecordController` 目前**只有一个 `[HttpGet]`**，没有取筛选项的入口。

  因此它横跨 framework + template + 前端三层。**不要按"加三个下拉框"估工。**

  分层推进（每层单独验证）：
  - [ ] framework：`GetPagedListAsync` 加 `actions`（动作码集合，命中任一即匹配）与 `outcome` 两个可选参数 + EF 实现 + 2 个 Store 替身。**不加 `category` 参数**——类别定义在 `IOperationActionDefinition` 上而记录里只有动作码，让存储去查定义管理器等于给它加一个它不该有的依赖（该存储只注入 `IDbContextProvider`，全项目不认识定义类型）。**调用方把类别展开成动作码再传**，与 `scope` 是同一条边界。
  - [ ] ⚠️ **新参数的默认行为必须有用例钉住**：`null` = 不过滤。上一次给 `scope` 加默认值时，我写的注释与实现语义相反（`IsUnrestricted` 在 `default` 时为 `false`），13 个测试红才发现；修好后若没有专门用例，这类反转再发生一次同样无人察觉。
  - [ ] template：后端 DTO 加 `Categories`/`Actions`/`Outcome` 三个可空属性（照 `GetUserPagedInputDto` 的范式：`List<string>?` + `[Display]` + `[MaxLength]` 条数上限）；AppService 把类别展开成动作码。
  - [ ] template：新增下发筛选项的端点（类别与动作码来自 `IOperationActionDefinitionManager.GetCategories()` / `GetAll()`）。
  - [x] 前端：照 `settings.html` 的 `hlm-select` 全套用法（`hlm-select` + `-trigger` + `-value` + `-content *hlmSelectPortal` + `-item`，绑 `[value]`/`(valueChange)`）。tsconfig 已映射、组件已 vendored，**有先例可抄，不必猜 API**。

  **两处刻意的取舍，都要如实记着：**

  1. **类别与动作先做单选，不做多选。** `HlmSelectMultiple` 是独立指令（`hlm-select-multiple`），不是 `HlmSelect` 上的 `[multiple]` 输入——这一条已查源码证实，若按"应该有 `[multiple]`"下笔会写出一个属性被静默忽略、看起来是单选的下拉。但它的 `valueChange` 值类型**无法验证**（`node_modules` 不存在、brain 源码读不到），而写一个"看起来是多选、实际收到的不是数组"的下拉比少一个多选更糟。**后端契约已按数组定**，前端传单元素数组，将来能验证时只需换标签，契约不动。

  2. ⚠️ **已知缺陷：下拉里的动作名是残句。** `actionText()` 复用句子模板（`translate(key, { target: '' })` 再 trim），得到的是"创建了用户"这类带动词的残句；且 `{{target}}` 在句子中间时（"调整了用户 {{target}} 的角色"）trim 去不掉中间空洞。作为选项读起来别扭。**没有修，是因为**另建 20 条"动作短名"词条会与句子模板重复维护、且要再扩一道闸门守同步；权衡后接受现状。要修的话，正解是给每个动作码补一条 `operationRecords.actionNames.<码>`，并把现有闸门扩成同时校验两套。
- [x] ~~详情抽屉~~ → **沿用既有行展开**（`createExpandableRows`），放目标标识与名字、授权依据、失败原因与技术详情（按任务 5 分层）、链路标识、模拟登录。

  **对 assessment §10.6「详情抽屉」的刻意偏离，理由是证据而非省事**：行展开是本仓五个平台表格（操作记录、角色、租户、开放应用、用户）**统一使用**的详情呈现方式；而 `sheet` 组件虽已 vendored，**全仓零先例**——它有 10 个指令，触发器绑定与受控状态都得靠猜。日期选择器那次正是这么栽的：自以为 `hlmPopoverTrigger` 能用，实际渲染成 0×0。抽屉的独有价值是承载 diff 这类大块内容，而 diff 不在本次范围内，**现在引入只有风险没有收益**。diff 真正落地时再议。
- [ ] 时间列：绝对时刻为主，近 24 小时附相对时间次要行 —— **未做**。锦上添花项，不影响可读性主线；绝对时刻已按展示时区渲染且中英格式正确。
- [x] 改写 `operation-record.dto.ts` 上「界面原样渲染、**不翻译**」的注释 —— 该说法对框架成立、对模板自有动作码不成立（assessment §5.4）
- [ ] 深链、同类事件折叠
- [x] ~~**词条按条件守卫分场景**~~ —— **撤回，这是我发明的要求，与仓库惯例相反**。三条证据：前端 i18n 是 JSON，**不支持 `//#if`**（现有文件里条件块计数为 0）；`template.json` 对前端 i18n 只有整目录排除（`!IncludeLocalization` 排掉 `public/i18n/**`），没有键级裁剪；而**既有做法已给出答案**——`tenants`（50 键）、`openApp`（22 键）、`impersonation`（4 键）对应的组件目录在相应场景被整个排除，**词条却照样留在 JSON 里**。本仓本来就接受"某些场景下有用不到的词条"，76 个既有键即是先例。
  **因此新闸门必须是单向校验**（每个已登记动作码都有词条），**不校验反向多余**——否则会把这 76 个既有键一并判红。
- [x] **新写一道闸门**（不是扩展现有的）：断言**每个已登记的动作码在 zh-CN 与 en 都有句子模板**，且反向无多余键。

  **更正任务 2 时的说法**：我当时说"把这道闸门挪到任务 6"，但读 `check-i18n-keys.ps1` 后确认**它做不到**——该脚本只校验 en ⇄ zh-CN 键集合自洽，其文档明写「键是否被代码引用不在此闸门范围（另由构建/lint 保障）」。要断言 "C# 常量 ⇄ JSON 键" 的对应关系，需要一道**跨语言比对的新脚本**，并登记进 `check-all.ps1`。这是新增工作量，不是搬运。

**验收**：中英双语各截图核对；切语言不刷新页面时失败原因**随之改变**。

### 任务 7：导出

> ⚠️ **范围需要重新界定——原条目在本仓没有落脚点（动手前查证所得，非做到一半才发现）。**
>
> 三项查证结果：
> - **后端无导出先例**：全仓无 `FileContentResult`/`FileStreamResult`/`text/csv`；控制器里的 `IActionResult` 只出现在 OpenIddict 协议端点。
> - **前端无下载先例**：无 `Blob`/`createObjectURL`/`responseType: 'blob'`（唯一命中的 `responseType: 'code'` 是 OAuth 参数）。
> - **无后台任务基础设施**：框架无 job/queue 组件；模板的三个 `IHostedService` 全是启动期初始化器（`ApplicationInitializer`、`RemoteIdentityReadinessInitializer`、`HostSettingRefreshJob`），不是任务队列。
>
> 所以「**异步生成 + 下载链接**」要额外引入：任务队列 + 产物存储 + 过期清理 + 状态查询端点。**那是一个独立子系统，量级超过任务 1–6 之和**，不该塞在一个复选框里。
>
> **同步导出是可行的替代，但边界是真实的**：`PagedRequestDto.MaximumLimit = 1000` 是单次请求封顶，同步导出只能导「筛选结果的前 N 条」。**"导出全部"必须异步**，这一条不能含糊。

**决定（用户定夺）：做同步导出，不做异步导出子系统。** 已实现并验证闭环。

> 📌 **更正上面那条「只能导前 1000 条」的判断——它是错的。**
> `PagedRequestDto` 的类注释写明：「`MaximumLimit` 是影响面封顶，**确需更大值的接口应自定义入参类型**」。
> 也就是说 1000 是**翻页接口**的约束，不是同步导出的硬边界；我把一个可绕开的约束当成了不可绕开的。
> 导出因此定义了独立入参类型，不继承 `PagedRequestDto`。

- [x] **同步导出 CSV**。上限 **10000 条**（`ExportOperationRecordsInputDto.MaximumExportCount`）：按每行约 300 字节估算约 3MB，同步生成与传输都在合理区间；再往上响应时长与内存不再适合跑在请求线程上。**"导出全部"仍然做不到**，那需要异步生成＋产物存储，本轮明确不做。
- [x] **与分页查询共用同一条可见性路径**。抽出 `ResolveScope()` / `ParseOutcome()`，`ResolveRequestedActions` 改成接收两个列表；前端把 `filtersFromParams` 从 `queryFromParams` 中拆出，查询与导出共用同一份筛选构造。**两处各写一份必然漂移，症状是「界面看不到的记录能被导出来」——那是越权，不是显示差异。**
- [x] **「筛了但展开为空」导出只有表头的文件**，与分页返回空页同语义。若改为不下传条件就成了全量导出，而文件本身看不出这个差别。
- [x] **CSV 两处硬化**：UTF-8 BOM（否则 Excel 按本地代码页解释，中文全是乱码，而用户只会认为导出坏了）；**公式注入中和**——`= + - @ \t \r` 开头的字段加前置单引号，因为目标名与操作人名是**用户可控内容**，把显示名改成 `=cmd|...` 就能让打开这份审计文件的管理员执行任意命令。
- [x] **独立权限 `App.OperationRecords.Export`** 与 **`operation-records.exported` 动作码**（先前因"不做导出"撤回，现已随实现恢复，这次**有真实调用点**）。导出**成功之后**才记录，避免"记了但没发生"。
- [x] 前端：`responseType: 'blob'` + `createObjectURL`/`revokeObjectURL`（本仓首例）；按钮按导出权限裁剪，且该权限**不加入** `PLATFORM_ENTRY_PERMISSIONS`——只有导出权限而无查看权限的人不该进这个页。

> ⚠️ **实现期查证推翻的一个假设，记下来免得重犯**：我曾据"框架里有 `NoWrapAttribute`"就给端点加了 `[NoWrap]`，并把「本项目默认把返回值包进统一 JSON 信封」写进注释。
> 实际是：模板**零调用** `AddResponseWrapper`、**无任何 csproj 引用** `Leistd.Response.*`、也没有自己的包装机制——**这个项目根本不包信封**。
> 正确修法是删掉，而不是为一个不存在的问题往模板里拉新依赖。端点注释里保留了"若将来启用信封，此端点必须加放行特性"的提示。

**验证**：26 道静态闸门全绿（20 动作码 / 573 前端 i18n 键 / 152 后端 i18n 键）；模板矩阵 `identity` / `resource` / `standalone` 三场景 × 五列（Backend/Runtime/Lint/Frontend/Test）全 `pass`，覆盖 `LocalIdentity` 开与关两侧，前端 267 测试通过。

### 任务 8：留存、不可篡改、容量

> 📌 **与任务 7 的关系已经分开了。** 先前记的是"两者共用同一个待决问题"，那在导出走异步时成立；
> 既然导出定为**同步**、不引入队列，归档就成了**唯一**还需要后台任务的条目。
>
> **用户已决定建这块基础设施**，按参考项目 `ai-relay` 的 `HostedServices/{Initializer,BackgroundServices,Workers}`
> 目录与命名来，并按 .NET 官方文档确定正确写法。
>
> ⚠️ **但归档本身还卡在一条别人写下的边界上，不能绕过去**：`IOperationRecordStore` 的契约注释明写
> 「**没有更新与删除。**……留了入口，"清理误记录"迟早会变成"清理不想被看到的记录"，而那时这张表已经不能作为证据了。
> **保留策略属于运维范畴，用数据库分区或归档作业处理。**」`IOperationRecordAppService` 也有一份同样措辞。
> 所以**不能给这个接口加 `DeleteAsync`**——那是凿穿它自己声明的不变量。注释同时给了方向：走数据库层。
> 落地方式（分区轮换 / 搬历史表 / 其他）需单独出对比后再动手。

- [x] **索引 `(TenantId, CreationTime DESC)`** —— 任务 2 已顺带落地：`OperationRecordConfiguration.cs:59` `HasIndex(TenantId, CreationTime).IsDescending(false, true)`，另有第 63 行 `(TenantId, Visibility, CreationTime DESC)`。
- [ ] 索引再按 `Action` / `ActorId` —— **需要证据再动，不默认要做**。这是写多读少的追加表，每加一个索引的代价摊到每一次记录写入上。动作筛选目前走 `(TenantId, Visibility, CreationTime)` 先取时间窗再筛已够用；在没有真实慢查询之前加索引是凭猜测付写入成本。
- [x] **失败登录洪峰折叠（主体维度）** —— 任务 4 已落地且**次数随记录落库**：阈值采样（第 1/5/25/100 次，其后每 100 次）＋ `OperationFailure` 数据带 `{"attempts":N,"windowMinutes":5}`。读者看到的是「5 分钟内第 25 次失败」一条，不是 25 行雷同记录。
- [ ] **失败登录洪峰折叠（IP 维度）—— 未做**。计数缓存键是主体标识的 SHA256（`AuthAppService.TrackFailedLoginAsync`），只按主体聚合：换 IP 不换主体仍命中同一计数器，**换主体不换 IP 则完全绕开**。分布式撞库正是后者的形态。
- [x] **成功记录同事务写入（不可异步）** —— 已成立：`EfCoreOperationRecordStore.InsertAsync` 用调用方的同一个 `DbContext` 同步写入，无 `Task.Run`、无独立事务；调用方持有显式事务时即与业务同原子。
  - ⚠️ **既有副作用（非本轮引入，需单独决策）**：它调用 `SaveChangesAsync`，会把调用方当时**所有未提交的变更一并刷出**——记一条操作因此不是「只写一行」。要么约定调用点在记录时没有待刷的脏实体，要么改用独立 `DbContext`，但后者会丢掉同事务原子性。这是真实取舍，不是笔误。
- [x] **按租户可配置保留期 + 到期归档** —— 已实现。后台任务基础设施照参考项目 `ai-relay` 的
  `HostedServices/{Initializer,BackgroundServices,Workers}` 目录与命名落地，写法按 .NET 官方文档核对：
  - `Workers/BackgroundTaskWorker.cs`：`BackgroundService` + `IBackgroundTaskQueue` **一个实例三处注册**
    （分开注册会得到两个实例，生产者写 A、消费的是 B，工作项永不执行且无报错）。队列用
    **`Channel.CreateBounded` + `BoundedChannelFullMode.Wait`**——官方文档的背压形态；
    **刻意不照抄参考项目的 `CreateUnbounded`**：操作记录写入量与请求量同阶，无界队列只会把
    「生产快于消费」推迟到内存耗尽才暴露。每项独立 try/catch（`ExecuteAsync` 一旦抛出就不再被调度，
    一个坏工作项会让整个队列永久停摆）。
  - `BackgroundServices/OperationRecordArchiveJob.cs`：每日定时，算到下一个固定时刻而非固定间隔
    （固定间隔会让「凌晨跑」随重启逐渐漂移到业务高峰）。
  - `Infrastructure/OperationRecords/`：归档实体 + 服务，**方案 B 搬历史表**。
- [x] **归档不碰框架的不变量**：`IOperationRecordStore` 注释明写「没有更新与删除……保留策略属于运维范畴」，
  因此归档落在**模板侧**、直接用 `MyProjectDbContext`，**没有**给框架接口加 `DeleteAsync`。
- [x] **两处易静默出错的地方已处置**：
  - 归档查询必须 `IgnoreQueryFilters()`。`OperationRecord` 实现 `IMultiTenant`，而后台任务跑在**无租户上下文**里，
    此时过滤器语义是「只放行宿主自己的行」——不加这一句，**只会归档宿主那部分，所有租户记录永远留着，
    且日志照样报「归档成功 N 条」**。
  - 归档实体**不实现 `IMultiTenant`**，否则将来读归档会再中一次同样的埋伏。代价是：为归档表开放查询接口时
    必须在那一层自己按租户过滤，没有过滤器兜底。
- [x] **默认关闭**（`OperationRecordRetention:Enabled`）。审计表只增不减是安全的默认值；
  一个默认就会删审计数据的开关，会让不知情的部署某天夜里悄悄丢掉合规所需的历史。
- [x] 迁移：**并入基线迁移**（`InitialIdentity` / `InitialResource`），共改六处——两套基线迁移的
  `CreateTable`／`CreateIndex`／`DropTable`、两份 `ModelSnapshot`、两份基线 `.Designer.cs` 内嵌快照。
  列定义**由 EF 在生成项目里算出后回植**，而非手写 18 列——手写出错面大且错了不报错。
  快照与 Designer **只插入归档实体段、不整体替换**：模板带 `#if (ExternalLogin)` / `#if (IncludeNotifications)` 守卫，
  而 EF 产物是裁剪后的，整体拷回去会永久抹掉这些守卫，之后在某些符号组合下模型与库对不上且不报错。

> ⚠️ **我先走错了一次，记下来免得重犯。**
> 我最初把归档表做成**新增迁移**（`AddOperationRecordArchives`），理由是"EF 迁移机制本就为此设计、改基线要手改六处、手改易错"。
> 查证时我看了 `template.json` 的包含规则、`DbMigrator` 是否支持多迁移、git 历史有无多迁移先例——**唯独没查本仓已经写下的约定**。
> 而约定就在两处：`template/backend/README.md`「**模板携带可审查的基线迁移**」，以及**本文件任务 2 的第 75 行**，
> 那是我自己在做 `Visibility` 改动时写下的「改基线而非追加」，连"Designer 漏改不报错""快照按字母序插错位置同样无症状"都一并记了。
> 抓住这个错误的是 `test-template-postgresql-e2e.ps1` 的五条 `Assert-Equal "1"`（迁移历史表行数）——
> **它们不是脆弱的硬编码，正是守这条约定的护栏**。教训：动一个有既定约定的地方之前，先查本仓已有的记载，
> 尤其是自己刚写下的那份。

- [x] **集成测试** `OperationRecordArchiveTests`：宿主侧与租户侧各造一条过期、一条未过期，断言四条各归其位。
  **必须同时造两侧**——只造宿主侧的用例在漏掉 `IgnoreQueryFilters()` 时照样全绿，那样的用例比没有更糟。

**任务 8 的验证（返工后重跑，三类性质不同、缺一不可）**

| 验证 | 结论 |
| --- | --- |
| 26 道静态闸门 | ✅ 全绿。其中「模板条件块结构」（273 个含条件块文件 `#if/#endif` 配平）覆盖了 `Down()` 插入点紧邻条件块的风险 |
| PostgreSQL E2E | ✅ 脚本自判 `passed`；归档表 DDL 实际执行（`CREATE TABLE` / `PK_` / 带 `DESC` 的索引）；**迁移计数回到每目标 1 份**，E2E 断言一行未改 |
| 全量 9 场景矩阵 | ✅ 9 × 5 列全 `pass`；Identity 集成测试 160 → **161**，与新增的一个 `[Fact]` 对上 |
| 快照/Designer 一致性 | ✅ 两份 `ModelSnapshot` + 两份基线 `Designer` 的归档实体段**逐字相同、各一次**（各 82 行） |

> 最后一项是单独查的：EF 只用 `ModelSnapshot` 做待定变更判定，**基线 `Designer` 的内嵌快照不参与**，
> 写歪了编译器和 E2E 都不报，要到下次 `migrations add` 算差异时才炸。

### 浏览器端到端测出的缺陷：宿主看不到 `Actor` 层记录

**现象**：宿主管理员登录后进操作记录页，界面显示「暂无操作记录」，而库里明明有一条
`auth.login.succeeded`。服务端返回 `{"totalCount":0,"items":[]}`——不是前端没渲染，是查询真的查不到。

**根因**：`EfCoreOperationRecordStore` 的可见性谓词写的是

```csharp
|| (x.Visibility == OperationVisibility.Actor && actorId != null && x.ActorId == actorId)
```

而 `OperationRecordVisibilityScope.Host` 是 `new(true, true, null)`，`ActorId` 恒为 `null`，
于是 `actorId != null` 恒假，**`Actor` 层对宿主被整层滤掉**。

**这是实现错，不是设计错**，三方证据一致：
- 架构文档：`Actor` = 仅操作人本人 **+ 上面两层**；
- `Host` 自己的 XML 注释：「宿主视角：**所有层级都可见**」；
- 而实现与两者都矛盾。

**为什么能活到端到端才暴露**：三条既有存储用例分别覆盖"不传即不过滤"、租户读者、未知读者，
**没有任何一条用 `Host` 作用域查询过**。`Host` 在测试里零覆盖，闸门与 9 场景矩阵都不可能发现——
它不是编译问题，也不是静态可查的问题。

**修复**：`Actor` 分支改为 `includesHostRecords || (actorId != null && x.ActorId == actorId)`。
复用 `IncludesHostRecords` 表达"能看 Host 层的就是宿主"，而不新增一个恒等于它的布尔——
那种冗余状态迟早漂移。`ForTenantReader` 的该位为 `false`，新增分支不触发，既有语义一字不变。

**补上的测试**：`A_host_reader_sees_every_layer_including_actor_records_that_are_not_theirs`，
同时钉两种 `Actor` 记录——别人的（`actorId="other"`）和**没有操作人的**。后者正是登录记录的真实形态：
`auth.login.succeeded` 写在登录成功的同一次请求里，此刻主体仍匿名，操作人为空是**刻意**的
（"谁登录了"由目标承载，见 `AuthAppService` 注释），因此它既不属于任何人、又必须对宿主可见。

> **教训**：一个类型的公共契约（这里是 `Host` 的"所有层级都可见"）如果没有任何用例直接查询它，
> 注释与实现可以长期相安无事地互相矛盾。**零覆盖的公共入口 = 迟早发散的两份事实。**
- [ ] 数据库层面仅追加（权限收敛到 INSERT／SELECT）—— **代码保证不了**：应用连接串所用角色的权限属部署层，模板至多在文档里给出授权脚本样例。
- [ ] 按时间分区 —— 部署层 DDL（PostgreSQL 声明式分区），不依赖后台任务组件；同样应先有数据量证据。
- [ ] **哈希链 —— 原条目的理由不成立，已更正。**
  原文：「是否启用可后定，但事后加会导致历史记录无法纳入链」。**链的完整性来自写入时算出并落库的哈希值，不来自列是否存在**：现在只加空列而不计算，这些行的哈希是 NULL，照样在链外，与以后再加列没有任何区别；PostgreSQL 11 起加可空列是元数据操作，也省不下迁移代价。所以「先占位」这个中间态没有收益。
  真正的选择是二选一：**(a) 现在就在写入路径上算链**——每写一条要先读前一条，记录写入被串行化，与追加表的并发写直接冲突；**(b) 承认链从启用之时才开始**，启用前的历史靠数据库层仅追加权限保障。
  当前实体确无任何哈希/链字段（已核实）。

### 任务 9：文档上收与清理

**这一步不能漏**：`docs/README.md` 规定 assessment「选定方案后删除」，但其中的**长期规则**必须先上收，否则删除即丢失。

- [ ] 上收到 `docs/architecture/`：两个 genre 的判据；「存渲染后的句子 = 语言永久锁死」及其证据链（Django 源码注释、Discourse 占位符翻转、Zulip #19730）；allowlist 而非 denylist 的安全边界；模拟登录透明度作为信任属性
- [ ] 同步 `framework/docs/components/operation-records.md`（**只写已成立的公共契约**，不写方案比较与任务状态）
- [ ] 同步 `template/docs/`（生成项目事实）
- [ ] 删除 `docs/assessments/2026-09-17-operation-records-readability.md`
- [ ] 任务全部完成后删除本计划

## 3. 验证要求（每层分别完成）

| 层 | 验证 |
| --- | --- |
| framework | 构建 + 单测；`dotnet pack` 到 `.tmp/local-feed`；**解包验 XML 内新签名**（同版本号会命中 NuGet 缓存，必须 `rm -rf <NUGET_PACKAGES>/leistd.*`） |
| template | **重新 `dotnet new` 生成**验证，**绝不 cp 覆盖**（模板源含 `//#if` 双分支，本身不是合法可编译代码） |
| 跨层 | `scripts/check-all.ps1` 24 道闸门；`scripts/test-template-matrix.ps1` **9 场景**；PostgreSQL E2E |

**矩阵调用方式**（三种写法只有一种对）：

```bash
pwsh -Command "& ./scripts/test-template-matrix.ps1 -Scenarios identity,resource,standalone -SkipPack"
```

内存紧张时分 3 批 × 3 场景，**批间 `dotnet build-server shutdown`**（实测空闲内存由约 54MB 回到 2.5GB）。`-SkipPack` 前必须验 `.tmp/local-feed` 内包的新鲜度 —— 脚本**只校验目录存在、不校验内容**。

`pack-local-feed.ps1` 在 `framework/build/`，不在 `scripts/`。

## 4. 风险

| 风险 | 应对 |
| --- | --- |
| diff 的 allowlist 漏配导致凭据落库 | allowlist 是唯一机制，无 denylist 回退路径；单测覆盖"未登记字段不进 diff" |
| 认证事件抬高写入量 | 任务 8 的折叠计数与分区；成功记录同事务、认证事件异步 |
| `auth.*` 泄入 Resource 场景 | 文件级 `#if (LocalIdentity)`；矩阵 `ForbiddenTokens` 兜底 |
| 破坏性签名变更影响下游 | 0.x 按 Minor 递增至 `0.13.0`；release notes 归入「破坏性变更」小节 |
| 任务 6 先于 4 完成 | 顺序硬约束：可读性提升而信息量未跟上，比裸码更误导 |
