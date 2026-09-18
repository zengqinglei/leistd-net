# 操作记录的可读性与记录契约

> 现状诊断与方案比较。**选定方案后删除本文**，有效结论上收到稳定文档。

## 1. 结论摘要

当前操作记录页是**按字段建列**（动作码 / 结果 / 操作人 / 目标 / 时间 / 授权依据），动作码以 `font-mono` 渲染裸值 `user.created`，目标是 GUID。这是**审计日志**的长相，面向排障工程师；而这个页面的实际受众是业务管理者，需要的是**操作记录**的长相。

改造方向：**存储保持结构化，展示渲染成句子**。这不是推翻原设计——`OperationRecordActions` 的注释已写明「界面按这个码本地化」，当前实现只是停在了裸码上。

需要补的契约只有一项：**目标名快照 `TargetName`**。失败原因**不补**，理由见 §7。

同时发现三处真实缺口（§4.3），其中「权限授予替换」与「用户角色替换」两个端点**成功与被拒两条路都不留痕**，属于审计覆盖漏洞而非展示问题。

## 2. 问题定性：两个 genre

调研 20 余个系统后，「审计日志」与「操作记录 / 活动日志」不是同一件东西的两种皮肤，而是受众、举证责任、失败语义三处都不同：

| | 审计日志 Audit Log | 操作记录 / 活动日志 |
| --- | --- | --- |
| 受众 | 合规审计、安全运营、排障工程师 | 业务管理者、客服 |
| 回答 | 「这个变更谁授权的？能否复现事件序列？」 | 「这个对象最近发生了什么？」 |
| 举证责任 | 完整、不可篡改、可长期留存 | 可读、相关、快 |
| 失败 | **必须记**（鉴权拒绝本身即安全事件） | 通常不记 |
| 形态 | 多列 + 机器码 + 原始 JSON | 一行句子 + 时间 |
| 代表 | CloudTrail、Entra、GitHub Audit、**ABP** | Linear、Notion、Shopify、Django admin |

驱动分化的是合规基线：OWASP Logging Cheat Sheet 在 what 维度下明确要求 action、affected object、result status、reason；NIST `AU-3` 把 success/fail indications 列为审计记录必需内容。**每个维度都要能独立筛选取证，塞进一句话就取不出来了**——这是审计侧必然多列的原因。活动 feed 没有这个约束，优化的是扫读速度，因而塌缩成句子。

**「像 ABP 审计日志」的病灶可以说得更准**：ABP 的形态特征是「一行 = 一次 HTTP 请求」，列全是技术维度（方法 / 状态码 / 耗时 / URL / 服务名 / 方法名），业务语义要点开弹窗深入两三层。我们虽然「一行 = 一次业务动作」，但**渲染方式照搬了审计侧**。

## 3. 硬约束：存渲染后的句子 = 语言永久锁死

这是本次调研中唯一具有否决力的结论，**它排除了整个国产生态的主流做法**。

### 3.1 反面证据

- **美团 `mzt-biz-log`**（芋道 yudao 直接使用）：`LogRecordInterceptor` 中 `Map<String, String> expressionValues = processTemplate(...)` 后 `.action(expressionValues.get(action))`——**落库的是已渲染的中文句子，模板不入库**。`system_operate_log.action` 列存的就是 `"更新了用户【张三】: 备注从【132】修改为【1324】"`。
- **若依 RuoYi**：`@Log(title = "用户管理", businessType = BusinessType.EXPORT)`——中文硬编码在注解里直接落库，前端无 i18n 目录。
- **Zulip**：`_("{user} has marked this topic as resolved.")` 在**发送时**翻译一次写进消息内容。后果见 [zulip/zulip#19730](https://github.com/zulip/zulip/issues/19730)（priority: high）：法语用户在英语服务器上解决话题，所有人看到法语句子。至今未能按读者渲染，只能退而求其次由组织配置一个固定语言。
- 同类：Zendesk `change_description` 存 `"Role changed from Administrator to End User"`；Salesforce `Display` 写入时固化且 SOQL 不可 filter；Atlassian 组织审计日志 API 直接下发服务端渲染好的 `attributes.message.content`。

**国产生态里「模板 + 参数」的机制成熟，但没有一家存模板 + 参数，全部渲染后存句子**，因此它们的国际化上限就是「UI 外壳可翻译、日志内容不可翻译」。本项目是中英双语，**不能走这条路**。

### 3.2 正面证据

- **Django** 把理由写进了源码（`django/contrib/admin/utils.py`）：

  ```python
  def construct_change_message(form, formsets, add):
      """
      Construct a JSON structure describing changes from a changed object.
      Translations are deactivated so that strings are stored untranslated.
      Translation happens later on LogEntry access.
      """
  ```

  [1.10 发行说明](https://docs.djangoproject.com/en/5.2/releases/1.10/)：*"The `LogEntry` model now stores change messages in a JSON structure **so that the message can be dynamically translated using the current active language**."* 同时记录了代价：*"Messages created before Django 1.10 will always be displayed in the language in which they were logged."*

- **Discourse** 是最具说服力的一条，因为中文译文把占位符顺序**翻转**了：

  ```yaml
  en:    removed_user: "Removed %{who} %{when}"
  zh_CN: removed_user: "%{when}移除了 %{who}"
  ```

  英文是「动词 + 宾语 + 时间」，中文是「时间 + 动词 + 宾语」。**任何在代码里 join 片段的做法在这里必然出错**——这是「整句进语言包」而非「翻译动词」的硬证据。

- 同类：WordPress Simple History 存 `_message_key` + context，展示期 `strtr` 插值，**失败模板也包在翻译函数里**（`'user_login_failed' => __('Failed to login with username "{login}" (incorrect password)')`）；GitLab `localized_action_name` 走 `s_('Event|closed')`；Mattermost 按 type 分派 `<FormattedMessage values={{username}}/>`；MediaWiki `logentry-{type}-{action}` 且把语法维度放进模板（`{{GENDER:$2|did bar}}`）。
- **飞书**（中文生态双语实证）：`event_name` 是英文 code（`space_create_doc`），命名规则 `<模块>_<动词>_<对象>`；同一份枚举附录中文给「创建云文档」、`?lang=en-US` 给 "Creates Docs"。**code 恒定，描述按语言查表。**

### 3.3 由此固化的四条约束

1. **存储层不得出现渲染好的句子列**——句子是视图，不是字段。
2. **整句进语言包 + 具名占位符**，绝不在代码里拼接片段。
3. **筛选维度与可见列解耦**——GitHub 审计日志因未做此事，官方声明 *"cannot search for entries using text"*；Shopify 退化为三列、无筛选、不可导出、上限 250 条、单条不可点开。
4. **详情层兜底**——抽屉 / 展开区承载 trace、目标 ID、授权依据，不进主列。

## 4. 现状盘点

### 4.1 存储契约（`OperationRecordInfo`，12 字段）

| 句子成分 | 现状 |
| --- | --- |
| 操作人 | ✅ `ActorId` + `ActorName` **快照**，另有 `ImpersonatorId/Name` |
| 动作 | ⚠️ 有稳定动作码，但界面未做「码 → 文案」映射，直接显示裸码 |
| 目标名 | ❌ **只有 `TargetId`，无名字快照** |
| 失败原因 | ❌ `Outcome` 仅 `Succeeded` / `Failed` 两档，无原因字段 |

后两项在类型注释中是**写了理由的刻意取舍**（「每一列都要能回答四问之一，回答不了的一律不进」）。

### 4.2 两条记录路径

| 路径 | 入口 | 覆盖 | 能拿到的信息 |
| --- | --- | --- | --- |
| 手动调用 | `IOperationRecorder.RecordSucceededAsync` | 业务成功 | 实体在手，**名字白拿** |
| 注解 + 中间件 | `[OperationRecordAction]` → `HttpContext.RecordDeniedOperationAsync()` | 授权阶段拒绝 | 只有路由值，**且不应查名字**（§6.3） |

模板中**一处 `RecordFailedAsync` 都没有**，唯一失败来源是授权拒绝。

### 4.3 三处真实缺口

| 缺口 | 证据 | 严重度 |
| --- | --- | --- |
| **权限授予替换不留痕** | `permission-grants.replaced` 常量存在；`PermissionAppService` 与 `PUT /permissions/grants/roles/{roleId}`（PermissionController:66）均存在；**既无记录调用，也无注解** | 高 |
| **用户角色替换不留痕** | `user.roles-replaced` 常量存在；`UserAppService.ReplaceRolesAsync:371` 与 `PUT /users/{id}/roles`（UserController:134）均存在；**既无记录调用，也无注解** | 高 |
| **`user.updated` 成功不记、被拒才记** | 注解在 UserController:61，但 `UserAppService` 无对应成功记录调用；而注解文档自称「与成功路径使用的值逐字一致」，预设了成功路径存在 | 中 |

前两条不是展示问题，是**审计覆盖漏洞**：`PUT /users/{id}/roles` 与 `PUT /permissions/grants/roles/{roleId}` 是整套权限体系里最敏感的两个写操作——「改一个人能做什么」与「改一个角色能做什么」——目前成功与被拒两条路都不留痕。

## 5. 读者视角：列形态

### 5.1 候选比较

| 候选 | 列 | 取舍 |
| --- | --- | --- |
| **A. 句子含操作人** | 时间 ｜ 操作内容（`{{actor}} 删除了角色 X`）｜ 结果 | 与需求原话一致，但**同一个名字在一行里印两遍**，且模拟登录提示塞不进句子 |
| **B. 句子不含操作人（推荐）** | 时间 ｜ 操作人 ｜ 操作内容（`删除了角色 X`）｜ 结果 | Django admin 形态；操作人可扫读、可筛选，模拟登录提示有容身处 |
| C. 严格三列 | 时间 ｜ 操作内容 ｜ 操作人 | 失败无法独立筛选与告警，违反 NIST `AU-3` / OWASP；Shopify 是此路退化的活教材 |

**选 B。** Django admin 的 `object_history` 模板逐字就是 Date/time、User、Action 三列，且 Action 列**不含操作人**（`Changed name and email.`）——人是独立一列。这正是避免重复的原因。保留结果列是在 Django 形态上的必要加列：我们**记录失败**（授权拒绝），而 Django 只记成功提交的写操作，根本没有状态字段。

### 5.2 句子模板

> **数字已过时**：本节写于只有 7 个动作码时。任务 4 把覆盖面扩到 **20 个已登记动作**（原有用户/角色/授权 7 + 租户 4 + 设置 1 + 导出 1 + 认证 4 + 模拟登录 2 + 令牌 1），因此实际是 **20 条 × 中英 = 40 条**词条。照下表的 7 条实现会漏掉 13 个动作码的文案——它们会按未登记降级显示裸码，界面上看不出报错，只是"有些行仍是机器码"。
>
> **还有一层本节没有预见到的复杂度**：20 个动作里 **7 个带条件守卫**（6 个 `LocalIdentity`、1 个 `OpenIddictServer`）。因此**词条也必须按场景裁剪**——否则 `resource` 场景的 i18n 里会躺着 7 条永远命中不到的键，而校验"动作码 ⇄ 词条"一一对应的闸门会把它判成多配。

7 个动作码 → 每码一条整句模板，中英各一份，占位符具名：

| 动作码 | zh-CN | en |
| --- | --- | --- |
| `user.created` | 创建了用户 {{target}} | Created user {{target}} |
| `user.updated` | 更新了用户 {{target}} | Updated user {{target}} |
| `user.deleted` | 删除了用户 {{target}} | Deleted user {{target}} |
| `user.roles-replaced` | 调整了用户 {{target}} 的角色 | Changed roles for user {{target}} |
| `role.created` | 创建了角色 {{target}} | Created role {{target}} |
| `role.deleted` | 删除了角色 {{target}} | Deleted role {{target}} |
| `permission-grants.replaced` | 调整了 {{target}} 的权限 | Changed permissions for {{target}} |

### 5.3 三级降级（必须显式实现）

| 情形 | 渲染 |
| --- | --- |
| 有 `targetName` | `删除了角色 管理员` |
| 有 `targetId`、无名字（被拒路径） | `删除了角色 3f2a1b8c…`（截断，`title` 给全量） |
| `targetId == "-"`（被拒的创建类） | 走**无目标变体** key：`创建用户` |
| **动作码未知**（下游业务自定义码） | **原样显示裸码**，不得漏出 `operationRecords.actions.foo` 这类键 |

最后一条是必须项：Transloco 缺键时返回 key 本身。Mattermost 的同类陷阱是——服务端渲染的句子冻结在 `post.Message` 里，前端只对已知 type 重渲染，**新增 type 忘加 renderer 就漏出服务端语言**。

### 5.4 与现有注释的冲突（实现时必须一并改）

`operation-record.dto.ts` 上写着：

> 由下游业务自行定义，**界面原样渲染、不翻译**：框架不可能预知有哪些码，为它们造词条只会得到一堆永远命中不了的键。

这句话**对框架成立，对模板项目不成立**——模板当然预知得了自己定义的那 7 个码。改造后的正确表述是：**已知码查表渲染，未知码降级为原样显示**（§5.3 第四行正是它的合理内核）。不改这条注释，实现时会撞上一条写着「别这么做」的文档。

## 6. 开发者视角：两条路径各自该传什么

### 6.1 现有签名的问题

```csharp
RecordSucceededAsync(string action, string targetId, string authorizationBasis, CancellationToken ct)
```

加上 `targetName` 就是**四个连续的 string 参数**，相邻两个互换不会报错、不会有任何症状，只会让审计表里的目标名列悄悄存进权限名。

### 6.2 建议：引入 `OperationTarget` 值对象

```csharp
public readonly record struct OperationTarget
{
    public static OperationTarget None { get; }                       // Id = "-", Name = null
    public static OperationTarget For(Guid id, string? name = null);
    public static OperationTarget For(string id, string? name = null);
    public string Id { get; }
    public string? Name { get; }
}
```

调用点：

```csharp
await operationRecorder.RecordSucceededAsync(
    OperationRecordActions.RoleDeleted,
    OperationTarget.For(role.Id, role.DisplayName),
    PermissionConstant.Roles.Delete,
    cancellationToken);
```

收益：ID 与名字**捆绑传递**，不会一个传了一个忘；与 `authorizationBasis` **类型不同，无法互换**；`"-"` 这个魔法字符串从所有调用点消失，收敛为 `OperationTarget.None`。

**可行性已核实**：`RoleAppService.DeleteAsync` 与 `UserAppService.DeleteAsync` **都在删除前持有实体**（`role` / `user` 局部变量，`role.Name` 已用于日志），名字无需额外查库。

### 6.3 注解路径：刻意不传名字，且这是安全属性

注解路径跑在**授权拒绝时**，此刻调用方**无权访问该目标**。若框架为了凑一句好看的话去查目标名回填，等于**把调用方无权查看的名字写进了他能读到的记录里**。

因此：

- `[OperationRecordAction(action, "id")]` 签名**不变**，不增加名字参数；
- 被拒行只有 ID，走 §5.3 第二级降级；
- 这一点必须写进注解的 XML 注释，否则后人会把它当缺陷"修复"掉。

### 6.4 目标名取哪个字段——必须定成规则

`Role` 有 `Name` 与 `DisplayName`；`User` 有 `Username` 与可空 `DisplayName`。**规则：取人类可读且非空者**，即 `DisplayName ?? Name` / `DisplayName ?? Username`。不定规则就会出现同类记录一半存登录名、一半存显示名。

### 6.5 快照语义

`TargetName` 与 `ActorName` 同为**快照**，理由逐字相同（`ActorName` 的注释已写明）：改名或销号之后靠 ID 反查，得到的要么是新名字、要么什么都没有，而审计要回答的是「当时是谁 / 当时是什么」。

主流佐证：Django `LogEntry` 同时有 `object_id = models.TextField(...)` 与 `object_repr = models.CharField(max_length=200)`（已核源码）；Linear `IssueHistory` 是「实体引 ID（`fromStateId`）、标量存快照（`fromTitle`）」；Jira changelog 是 `from`/`fromString` 双存。

**当前 `ActorName` 有快照而 `TargetName` 没有，是不对称，不是取舍。**

## 7. 明确不做的事

| 不做 | 理由 |
| --- | --- |
| ~~不补失败原因字段~~ | **本条已推翻，见 §7.1。**原论证（「唯一失败来源是授权拒绝，补字段等于为不存在的场景预留」）拿**模板当下的用法**去论证**框架的契约**，而框架是要分发给下游业务项目的 |
| **不把失败编进动作码** | 国际主流（Slack `user_login_failed`、GitHub `business.recovery_code_failed`、Atlassian `Denied ... export`）确实如此，但我们已有 `Outcome` 列且走 Entra / 若依形态，同为主流；改动更大、收益不明 |
| **不引入任意参数包** | 「从【132】修改为【1324】」这类 diff 需要自由参数，而那正是 `mzt-biz-log` 走向「渲染后存句子」的起点。句子模板占位符**只限 `{{target}}`** |
| ~~不存实体变更 diff~~ | **本条已推翻，见 §10.4。**原论证说它「属于实体变更追踪」，但查实 `Leistd.Auditing` 只填充 `IAuditedObject` 的审计属性、**不记录属性新旧值**，全框架没有 `EntityPropertyChange` 一类的持久化变更记录——**那个"另一处"并不存在**，等于用架构边界包装了一个真实缺口 |

## 7.1 失败原因：推翻 §7 首行后的方案

### 为什么原论证是错的

三条，逐条成立：

1. **拿模板的用法论证框架的契约。** 框架是分发给下游业务项目的 NuGet 组件，`RecordFailedAsync` 存在的理由正是业务失败；模板当下没调用它，不等于契约不需要承载它。
2. **契约自身已经明写了这个场景。** `OperationRecordOutcome.Failed` 的注释是「权限不足（授权阶段）、业务规则拒绝，**或执行中抛错**」——**它声明要承载执行期异常，却没给异常信息留任何位置**。这是既有设计的内部矛盾。
3. **与 §2 的受众定位自相矛盾。** §2 把这张表定成面向业务管理者，却把失败原因放在只有工程师够得着的请求日志里（凭 `CorrelationId` 回查）。不能让业务管理者去 grep 日志。

当前若强行要记，开发者只剩四条坏路：塞 `action`（动作码是稳定 i18n 键，每条异常造一个新码 → 基数爆炸）、塞 `targetId`（目标检索全废）、塞 `authorizationBasis`（语义错位）、或放弃记录只写日志。**四条都不可接受。**

### 既有机制盘点（结论：复用，不新建）

| 事实 | 位置 |
| --- | --- |
| `BusinessException.Code` **恒有值**，注释自称「既是对外的稳定机器契约，也是**本地化资源的查找键**」 | `Leistd.ExceptionHandling.Core/BusinessException.cs` |
| `LocalizationData` 是 `IReadOnlyDictionary<string, object?>`，供资源中具名占位符填充 | 同上 |
| **118 条错误码词条**已存在，形如 `Auth:InvalidCredentials` → `"Login failed: ... - {UsernameOrEmail}"` | `Api/Resources/{en,zh-CN}.json` |
| 渲染发生在**请求时**：`_localizer[ex.Code]` 填参后作为 `detail` 下发，`code` 放进 `Extensions["code"]` | `BusinessExceptionHandler.cs:49,303` |
| 前端**解析了 `code` 却从不使用**，`applicationErrorMessage` 只有一行 `error.message`，直接显示后端渲染好的文本 | `core/errors/application-http-error.ts` |

最后一行正是 §3 批评的 Atlassian 模式（服务端渲染句子随响应下发）。**对一次性的错误提示无害，对要落库的审计记录致命。**

### 方案：失败原因分两类，区别对待

**① 可枚举的业务规则拒绝** —— 存 `Code` + `Data`，复用那 118 条词条，**展示期渲染**。与动作码同一套路子，零新增机制。

**② 不可枚举的技术异常**（远程接口超时、数据库报错）—— 不可本地化，且**携带泄露风险**：`SqlException` 带表名列名、`HttpRequestException` 带内部地址、连接失败信息可能带主机甚至连接串。**这张表在多租户下是租户管理员可读的。**

```csharp
public readonly record struct OperationFailure
{
    public static OperationFailure None { get; }
    /// 业务拒绝：取 Code 与 LocalizationData，复用既有词条
    public static OperationFailure FromException(BusinessException ex);
    public static OperationFailure FromCode(string code, IReadOnlyDictionary<string, object?>? data = null);
    /// 技术异常：调用方对内容负责，不本地化
    public static OperationFailure FromDetail(string detail);

    public string? Code { get; }
    public string? Data { get; }     // 序列化 JSON，截断
    public string? Detail { get; }   // 原始技术信息，截断
}

Task RecordFailedAsync(string action, OperationTarget target,
                       string authorizationBasis, OperationFailure failure,
                       CancellationToken cancellationToken = default);
```

### 三条必须守住的约束

1. **刻意不提供接受任意 `Exception` 的重载。** 只接 `BusinessException`（其 `Message` 本就是给日志的英文诊断，`Code` 才是契约）。要写技术异常，必须显式 `FromDetail("调用支付网关超时")`——**强迫开发者逐次决定哪段文字可以给租户看**，杜绝 `catch (Exception ex) { ...FromException(ex) }` 把原始异常灌进审计表。这条是安全边界，不是风格偏好。
2. **渲染必须在前端。** 若由后端在查询时渲染，用户切换语言（transloco 是纯客户端切换、**不重新请求**）后失败原因会停在旧语言直到刷新——一个只在切语言时现形的 bug。前端渲染，未知码按 §5.3 第四行降级为原样显示。
3. **`Detail` 只进展开区，不进主列。** 它是技术文本，既不本地化也不该参与扫读。

### 与 §7「不引入任意参数包」的关系

不冲突。`Data` 是**失败原因模板**的参数，沿用 `BusinessException.LocalizationData` 这一既有形态；**动作句子模板的占位符仍只限 `{{target}}`**。两者是不同的模板，各自的参数面互不扩张。

### 新增待决

- 前端要渲染错误码，需要对应词条进 `public/i18n/`。118 条全搬是重复维护面，只搬「实际会被记录的失败码」则需要一条约定 + 闸门（`check-i18n-keys.ps1` 可扩展）来防漂移。**倾向后者**，但取舍需确认。
- `Detail` 在多租户下对租户管理员可见。是否需要一个权限位或宿主开关来抑制显示，待定。

## 8. 影响面

| 层 | 变更 |
| --- | --- |
| `framework/` | `OperationRecordInfo` + `OperationRecord` 实体加 `TargetName`、`FailureCode`、`FailureData`、`FailureDetail`（后三者见 §7.1）；新增 `OperationTarget` 与 `OperationFailure` 两个值对象；`IOperationRecorder` 两个方法签名；各字段长度上限与截断；EF 配置与迁移；`framework/docs/components/operation-records.md` |
| `template/` 后端 | 4 处调用改用 `OperationTarget`；补 `user.updated` 成功记录；补两处权限相关端点的记录调用与注解（§4.3）；`OperationRecordOutputDto` 加 `targetName` |
| `template/` 前端 | 表格改为 4 列 + 展开区；`operation-record.dto.ts` 加字段并改写 §5.4 注释；i18n 新增动作句子模板（含无目标变体）与**失败错误码词条**；失败原因按 §7.1 在前端渲染、`Detail` 仅进展开区；mock 数据 |
| 测试 | 框架 Store 测试、模板集成测试 |

**验证要求**：改动跨 framework 与 template，按 `maintaining-leistd-repository` 维护**一份**根仓库计划；framework 改动需 pack + 清缓存 + 验包内签名；template 改动需**重新生成**验证，不得 cp 覆盖；完成后**重跑模板矩阵**。

## 9. 待决

1. §5.2 的中英措辞需逐条确认（尤其 `permission-grants.replaced` 的 `{{target}}` 是角色，句子读作「调整了 管理员 的权限」是否清楚）。
2. 时间列是否改为相对时间（「3 分钟前」）+ 绝对时间浮层。主流 feed 多用相对时间，但审计场景需要绝对时刻；当前 `AppDate` 已按语言给出正确的绝对格式，改动非必需。
3. ~~§4.3 两处高危缺口是否拆成独立变更先行~~ —— **已定，见 §10.11**：排在推进顺序第 1 位，独立先修，不等句子化。

## 10. 最佳实践终局

> **本章不考虑与现有代码的兼容、迁移成本或改动量**，只回答一个问题：给定我们的场景（多租户 SaaS、宿主／租户双视角、支持模拟登录、Identity／Resource／Standalone 三种服务角色、中英双语），一份达到最佳实践的操作记录应该长什么样。
>
> §1–§9 是"从现状出发能走到哪"，本章是"终点在哪"。两者的差距见 §10.11。

### 10.1 先确定受众，其余都是推论

三类读者，诉求互不相同，**同一张表必须同时服务他们**：

| 读者 | 典型问题 | 需要的形态 |
| --- | --- | --- |
| 租户管理员（主要） | 「谁把这个人的角色改了？」「这单为什么被驳回？」 | 句子、可扫读、能按人／按对象筛 |
| 宿主运维 | 「这个租户最近出了什么事？」「那次故障期间谁动过配置？」 | 跨租户、可按时间窗与类别聚合、能跳日志 |
| 合规／安全 | 「谁在反复尝试他没有的权限？」「这三个月的变更能导出吗？」 | 完整、不可篡改、可导出、含认证事件 |

**§2 只写了第一类，这是 §1–§9 覆盖面偏窄的根源。** 后两类的需求不是"锦上添花"，是 NIST `AU-3` 与 OWASP 的基线。

### 10.2 事件模型：从裸字符串升级为注册式定义

现状里动作码是**散落的常量字符串**，框架"只存不读"。终局应是**注册式定义**，与本框架已有的 `IPermissionDefinitionProvider` 同构：

```csharp
public interface IOperationActionDefinitionProvider
{
    void Define(IOperationActionDefinitionContext context);
}

public sealed class OperationActionDefinition
{
    public string Code { get; }                  // user.created，稳定契约
    public string Category { get; }              // 认证 / 账号 / 授权 / 租户 / 配置 / 数据
    public OperationSeverity Severity { get; }   // Info / Notice / Critical
    public bool TracksChanges { get; }           // 是否携带变更明细
    public OperationVisibility Visibility { get; } // 见 §10.5
}
```

**这一步解锁四件今天做不到的事**：

1. **按类别筛选**——Entra 的 Category 列正是如此，界面不必硬编码有哪些码。
2. **词条完整性可闸门化**——`check-i18n-keys.ps1` 可断言「每个注册动作在 zh-CN 与 en 都有句子模板」，杜绝上线后才发现漏词条。
3. **未知码只可能来自第三方**——一方代码的码全部注册在案，§5.3 的降级规则退化为纯粹的兼容兜底。
4. **严重度驱动告警**——`Critical` 类事件（权限授予变更、模拟登录开始）可直接接告警，不必让运维去正则匹配动作码。

### 10.3 覆盖面：该记什么

判据不是"改变了什么"，而是 **OWASP 的四问 + 一条**：**谁、何时、做了什么、结果如何，以及凭什么**。凡是能改变「某人能做什么」「某数据是什么」「某人是谁」的操作，都必须记。

#### 认证与会话（当前完全缺失，优先级最高）

| 事件 | 备注 |
| --- | --- |
| `auth.login.succeeded` / `auth.login.failed` | **失败必须记**。OWASP 明列认证事件；Slack `user_login_failed`、GitHub `business.recovery_code_failed` 都在审计表内 |
| `auth.logout` | |
| `auth.password.changed` / `auth.password.reset` | 自助改密与管理员重置是**两个**码：授权依据不同 |
| `auth.mfa.enabled` / `auth.mfa.disabled` | 关闭 MFA 是降低安全等级的操作，`Critical` |
| `auth.external-login.bound` / `.unbound` | |
| `auth.token.issued`（机器主体） | 机器主体的 `client_credentials` 取令牌 |
| `impersonation.started` / `impersonation.ended` | **我们的场景独有，见 §10.5** |

**`OperationRecordActions` 注释里「登录尝试……不该记」这一条，终局里推翻。** 它的原意是防止低价值高频事件淹没审计表——这个顾虑真实，但**解法是容量策略（§10.10），不是不记**。「谁在反复尝试登录失败」恰恰是审计最该回答的问题之一，注解自己的文档里也这么写过。

#### 授权与账号

`user.*`（created／updated／deleted／enabled／disabled／roles-replaced）、`role.*`（created／updated／deleted）、`permission-grants.replaced`（`Critical`）。

#### 租户与配置

`tenant.*`（created／activated／deactivated／connection-changed）、`setting.changed`（含作用域：Host／Tenant）。**配置变更今天完全不记**，而"那次故障期间谁动过配置"是宿主运维的第一问。

#### 审计表自身

`operation-records.exported` —— **导出审计日志这件事本身要被审计**，这是主流做法（谁把三个月的操作记录导走了，是安全事件）。

### 10.4 变更明细：allowlist 的 diff

**现状认知需要更正**：`Leistd.Auditing` 只做 `IAuditedObject` 的属性填充（CreationTime／CreatorId／LastModifier…），**不记录属性的新旧值**；全框架没有 `EntityPropertyChange` 一类的持久化变更记录。所以 §7「diff 属于实体变更追踪，不属于本表」**把一个真实缺口用架构边界包装掉了——那个"另一处"并不存在**。

终局按 Jira changelog 的模型（调研中最完整的一个）：

```csharp
public sealed class OperationRecordChange
{
    public string Field { get; }        // 字段标识，界面按它本地化字段名
    public string? OldValue { get; }    // 机器值（ID、枚举名）
    public string? NewValue { get; }
    public string? OldDisplay { get; }  // 人类可读快照
    public string? NewDisplay { get; }
}
```

**ID 与显示名双存**，理由与 `TargetName` 一致（§6.5）：Jira 是 `from`/`fromString`，Linear 是 `fromStateId`/`fromTitle`。

**两条不可退让的约束：**

1. **字段必须 allowlist，绝不 denylist。** 自动 diff 全部变更属性一定会写进 `PasswordHash`、`SecurityStamp`、令牌、密钥——一次漏配就是凭据落库。**只有显式登记为"可审计"的字段才进 diff**，登记时一并声明是否脱敏。这是安全边界，不是配置偏好。
2. **diff 是记录的子项，不是列。** 它进详情抽屉，不进主列——否则每行高度不可控，扫读性直接崩掉（Shopify 的反面教训是另一端，但结果一样）。

有了 diff，「admin 更新了用户 张三」才能回答"改了什么"，否则句子化只是把一份看不懂的日志变成一份**看得懂但仍然说不清**的日志——而后者更危险，因为它读起来像交代完了。

### 10.5 可见性分层：我们场景独有的一节

调研里的系统多为单租户或"组织即边界"，**没有现成答案**。我们必须自己定：

| 层级 | 谁能看 |
| --- | --- |
| `Tenant` | 该租户的管理员 + 宿主 |
| `Host` | 仅宿主 |
| `Actor` | 仅操作人本人 + 上面两层（如自助改密） |

**字段级也要分层**，而不只是记录级：

- `FailureDetail`（§7.1 的技术异常原文）**仅宿主可见**。它可能带表名、内部地址、主机名——租户管理员不该看到宿主的基础设施形态。这也让 §7.1 的"新增待决"有了答案。
- `CorrelationId` 仅宿主可见（对租户是无用且泄露内部拓扑的标识）。

**模拟登录是这一节的核心，也是我们与主流的关键差异：**

宿主管理员以租户身份操作时，**租户方必须能看见**。否则审计表对被操作的一方是瞎的——租户管理员会看到"自己的某个用户"做了他没做过的事。所以：

- `impersonation.started` / `.ended` 对**租户可见**，不是 `Host` 层；
- 模拟期间产生的每条记录，`ActorName` 是被模拟者、`ImpersonatorName` 是真实操作人，**两者都对租户可见**（现有 DTO 已有这两个字段，方向是对的）；
- 界面上模拟记录必须有**视觉区分**，不能只靠一行小字——它是"别人以你的名义做的事"。

这是一条**信任属性**：一个能让宿主悄悄操作而租户看不见的审计系统，对租户没有价值。

### 10.6 读者视角的界面

**主列表（四列，§5.1 已定）**：时间 ｜ 操作人 ｜ 操作内容 ｜ 结果。

**时间列**：绝对时刻为主（审计需要精确），近 24 小时附「x 分钟前」次要行。不做纯相对时间——Linear／Notion 那种 feed 可以，审计表不行。

**筛选栏（与可见列解耦，§5.3 约束 3）**：类别、动作、结果、操作人、目标、时间范围；宿主视角多一个租户维度。**这是 §1–§9 与 Entra 差距最大的地方**，Entra 的筛选维度多于它建议常驻的列。

**详情抽屉**（主流首选：腾讯云、Atlassian、Stripe、Entra 都是抽屉）：变更明细 diff 表、目标标识、授权依据、失败原因与技术详情（按 §10.5 分层）、链路标识、模拟登录信息。

**深链**：每条记录有固定链接可分享；链路标识可点击跳转日志查询（Stripe 的 `request_log_url` 是范例）。

**分组**：同一操作人在短时间内的同类事件折叠为一行（Linear／Notion 做法），避免一次批量操作刷屏。

### 10.7 导出

CSV 与 JSON 两种，**遵循当前筛选条件**（而不是导全表），异步生成 + 下载链接。导出动作本身写一条 `operation-records.exported`（§10.3）。

合规读者需要它：GitHub、Figma、Salesforce、CloudTrail 全部提供。

### 10.8 留存与不可篡改

- **仅追加**：没有更新与删除的 API，这一点现有实体注释已写明（"写入后不再修改"），终局里应**在数据库层面**也保证（权限收敛到只 INSERT／SELECT）。
- **留存策略**：按租户可配置的保留期 + 到期归档，而不是无限增长。
- **防篡改**：高保障场景可加逐条哈希链（每条记录含前一条的哈希），使任何事后删改可被检出。**这是可选项，取决于是否要满足外部审计**——但要在设计上留出字段位置，事后加会导致历史记录无法纳入链。

### 10.9 开发者 API 终局

两条路径的信息契约已在 §6 定；终局再加一件事：**让"记了成功却忘了记失败"在结构上不可能发生**。

```csharp
using var op = recorder.Begin(OperationActions.OrderDeleted,
                              OperationTarget.For(order.Id, order.No),
                              PermissionConstant.Orders.Delete);
await DeleteAsync(order);
op.Succeeded();          // 未调用即视为失败
```

作用域退出时若未标记成功，自动按 §7.1 记一条失败（异常为 `BusinessException` 时取 `Code` + `LocalizationData`，否则要求调用方已通过 `op.Fail(detail)` 显式给出可公开的说明）。

**必须保留 §6 与现有 XML 注释里已确立的事务语义**：成功记录落在调用方事务内（与变更同生共死），失败记录不开独立事务、也不随回滚消失。作用域式 API 要显式处理这两种边界，而不是抹平它们。

### 10.10 容量与性能

记认证事件会显著抬高写入量，这是 §10.3 推翻"不记登录尝试"后必须一并解决的：

- **索引**：`(TenantId, CreationTime DESC)` 为主，另按 `Action`、`ActorId` 建索引；
- **失败登录的洪峰**：同一主体／IP 短窗口内的连续失败**折叠计数**（记一条带次数，而非 N 条），既保留"有人在爆破"的事实，又不让表被刷爆；
- **分区与归档**：按时间分区，配合 §10.8 的留存策略；
- **写入路径**：成功记录必须同事务（不可异步）；认证类等不参与业务事务的事件可走异步队列。

### 10.11 与 §1–§9 的差距一览

| 维度 | §1–§9 方案 | 终局 | 差距性质 |
| --- | --- | --- | --- |
| i18n 机制 | 码 + 参数、展示期渲染 | 同 | **已达标** |
| 目标名快照 | `TargetName` | 同 | **已达标** |
| 异常写入安全边界 | 只接 `BusinessException` | 同 | **已达标，且强于调研中所有系统** |
| 事件定义 | 散落常量 | 注册式定义 + 类别 + 严重度 | 结构缺失 |
| 认证事件 | 不记 | 必记，含失败 | **覆盖缺失，最严重** |
| 变更明细 | 不做 | allowlist diff，ID/显示名双存 | **能力缺失** |
| 筛选 | 关键字 + 时间 | 六维筛选，与列解耦 | 能力缺失 |
| 可见性 | 未分层 | 记录级 + 字段级三层 | 安全缺失 |
| 模拟登录透明度 | 字段已有，未成体系 | 对租户可见 + 视觉区分 | 信任属性缺失 |
| 导出 | 无 | CSV/JSON，自身被审计 | 合规缺失 |
| 留存／防篡改 | 未涉及 | 仅追加 + 留存期 + 可选哈希链 | 合规缺失 |
| 容量策略 | 未涉及 | 索引／折叠／分区 | 工程缺失 |

**按价值排序的推进顺序**（与文档前半部分的顺序不同，刻意如此）：

1. **§4.3 两个高危缺口**（角色替换、权限授予替换不留痕）——与展示形态无关，可独立先修；
2. **认证事件留痕**（§10.3）——合规基线，当前为零；
3. **可见性分层 + 模拟登录透明度**（§10.5）——安全与信任属性；
4. **变更明细 diff**（§10.4）——让"改了什么"可回答；
5. **句子化展示 + 筛选维度**（§5、§10.6）——即本文前半部分；
6. 导出、留存、容量策略（§10.7、§10.8、§10.10）。

**一份记全了的机器码日志，比一份读着顺却记漏了关键操作的日志有用。** 句子化排在第 5 位不是贬低它，而是它解决的是"看不懂"，而 1–4 解决的是"根本没记"。

## 11. 来源

- OWASP Logging Cheat Sheet；NIST SP 800-53 `AU-3`
- [Django 1.10 release notes](https://docs.djangoproject.com/en/5.2/releases/1.10/)；`django/contrib/admin/models.py`、`utils.py`
- [zulip/zulip#19730](https://github.com/zulip/zulip/issues/19730)
- Discourse `client.en.yml` / `client.zh_CN.yml` `action_codes.*`
- 飞书行为审计事件枚举（中英双版）
- 美团 `mzt-biz-log` `LogRecordInterceptor`；芋道 yudao `system_operate_log`；若依 `@Log`

**未逐字核实**（引用前需人工复核）：Salesforce 列清单来自搜索引擎摘要而非官方原文；GitHub 审计日志 UI 的列头与详情形态未找到权威来源；钉钉 / 企业微信 / 飞书管理后台页面列名未抓到（均为 SPA）。
