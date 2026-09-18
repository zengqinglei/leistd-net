# 操作记录：长期约束

本文是操作记录（activity log）在本仓的长期设计约束，不是某次改造的过程记录。四条规则各自都有否决力：违反其中任何一条，代价都不是"不好看"，而是事后无法补救。

## 1. 两个 genre 不可混为一谈

「审计日志 Audit Log」与「操作记录 / 活动日志」不是同一件东西的两种皮肤。三处不同：

| | 审计日志 | 操作记录 / 活动日志 |
| --- | --- | --- |
| 受众 | 合规审计、安全运营、排障工程师 | 业务管理者、客服 |
| 回答 | 「这个变更谁授权的？能否复现事件序列？」 | 「这个对象最近发生了什么？」 |
| 举证责任 | 完整、不可篡改、可长期留存 | 可读、相关、快 |
| 失败 | **必须记**（鉴权拒绝本身即安全事件） | 通常不记 |
| 形态 | 多列 + 机器码 + 原始 JSON | 一行句子 + 时间 |
| 代表 | CloudTrail、Entra、GitHub Audit | Linear、Notion、Shopify、Django admin |

分化的根源是合规基线：OWASP Logging Cheat Sheet 在 what 维度下要求 action、affected object、result status、reason；NIST `AU-3` 把 success/fail indications 列为审计记录必需内容。**每个维度都要能独立筛选取证，塞进一句话就取不出来了**——这是审计侧必然多列的原因。活动 feed 没有这个约束，优化的是扫读速度，因而塌缩成句子。

**本仓的选择**：面向业务管理者的操作记录，主列渲染成句子；但保留 `Outcome` 作为**独立列而非句子的一部分**，以满足 `AU-3` 的独立筛选要求。判断某个诉求属于哪个 genre，先看受众和举证责任，不要看它长得像什么。

> 识别退化的信号（按特征认，不按框架名认）：「一行 = 一次 HTTP 请求」而非一次业务动作；列全是技术维度（方法 / 状态码 / 耗时 / URL / 服务名 / 方法名）；业务语义要点开弹窗深入两三层才看得到。即便做到「一行 = 一次业务动作」，只要渲染方式仍是多列技术维度，读者体验就还是审计日志。

## 2. 存渲染后的句子 = 语言永久锁死

**这条具有否决力，它排除了整个国产生态的主流做法。**

### 2.1 反面证据

- **美团 `mzt-biz-log`**（芋道 yudao 直接使用）：`LogRecordInterceptor` 中 `processTemplate(...)` 后 `.action(expressionValues.get(action))`——**落库的是已渲染的中文句子，模板不入库**。`system_operate_log.action` 列存的就是 `"更新了用户【张三】: 备注从【132】修改为【1324】"`。
- **若依 RuoYi**：`@Log(title = "用户管理", businessType = BusinessType.EXPORT)`——中文硬编码在注解里直接落库，前端无 i18n 目录。
- **Zulip**：`_("{user} has marked this topic as resolved.")` 在**发送时**翻译一次写进消息内容。后果见 [zulip/zulip#19730](https://github.com/zulip/zulip/issues/19730)（priority: high）：法语用户在英语服务器上解决话题，所有人看到法语句子。至今未能按读者渲染，只能退而求其次由组织配置一个固定语言。
- 同类：Zendesk `change_description` 存 `"Role changed from Administrator to End User"`；Salesforce `Display` 写入时固化且 SOQL 不可 filter；Atlassian 组织审计日志 API 直接下发服务端渲染好的 `attributes.message.content`。

国产生态里「模板 + 参数」的机制成熟，但**没有一家存模板 + 参数，全部渲染后存句子**，因此其国际化上限是「UI 外壳可翻译、日志内容不可翻译」。本仓是中英双语，**不能走这条路**。

### 2.2 正面证据

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

- 同类：WordPress Simple History 存 `_message_key` + context，展示期 `strtr` 插值，失败模板也包在翻译函数里；GitLab `localized_action_name` 走 `s_('Event|closed')`；Mattermost 按 type 分派 `<FormattedMessage values={{username}}/>`；MediaWiki `logentry-{type}-{action}` 且把语法维度放进模板（`{{GENDER:$2|did bar}}`）。
- **飞书**（中文生态双语实证）：`event_name` 是英文 code（`space_create_doc`），命名规则 `<模块>_<动词>_<对象>`；同一份枚举附录中文给「创建云文档」、`?lang=en-US` 给 "Creates Docs"。**code 恒定，描述按语言查表。**

### 2.3 由此固化的四条约束

1. **存储层不得出现渲染好的句子列**——句子是视图，不是字段。失败原因同样存**错误码 + 具名参数**，不存渲染结果。
2. **整句进语言包 + 具名占位符**，绝不在代码里拼接片段。
3. **筛选维度与可见列解耦**——GitHub 审计日志因未做此事，官方声明 *"cannot search for entries using text"*；Shopify 退化为三列、无筛选、不可导出、上限 250 条、单条不可点开。
4. **详情层兜底**——抽屉 / 展开区承载 trace、目标 ID、授权依据，不进主列。

## 3. 注解路径刻意不回填目标名，这是安全属性

注解路径跑在**授权拒绝时**，此刻调用方**无权访问该目标**。若框架为了凑一句完整的话去查目标名回填，等于**把调用方无权查看的名字写进了他能读到的记录里**。

因此：

- 注解签名只收标识，**不增加名字参数**；
- 被拒记录只有 ID，界面按降级规则显示；
- **这一点必须写进注解的 XML 注释**，否则后人会把它当缺陷「修复」掉。

同源的两条配套规则：

- **目标名取哪个字段要定成规则**：`Role` 有 `Name` 与 `DisplayName`，`User` 有 `Username` 与可空 `DisplayName`。规则是**取人类可读且非空者**（`DisplayName ?? Name` / `DisplayName ?? Username`）。不定规则就会出现同类记录一半存登录名、一半存显示名。
- **操作人名与目标名同一取法**：操作人名取会话主体的显示名声明（`name`），缺省才退到用户名；会话签发时要把显示名写进 `name`、用户名写进 `preferred_username`。只写一个登录名时，同一个人在操作人列是 `admin`、在目标列是 `System Administrator`。
- **目标名与操作人名同为快照**：改名或销号之后靠 ID 反查，得到的要么是新名字、要么什么都没有，而审计要回答的是「当时是谁 / 当时是什么」。主流佐证：Django `LogEntry` 同时有 `object_id` 与 `object_repr`；Linear `IssueHistory` 是「实体引 ID（`fromStateId`）、标量存快照（`fromTitle`）」；Jira changelog 是 `from`/`fromString` 双存。

## 4. 可见性分层与模拟登录透明度

调研中的系统多为单租户或「组织即边界」，**这一节没有现成答案**，是本仓自定的。

| 层级 | 谁能看 |
| --- | --- |
| `Tenant` | 该租户的管理员 + 宿主 |
| `Host` | 仅宿主 |
| `Actor` | 仅操作人本人 + 上面两层（如自助改密） |

**字段级也要分层**，而不只是记录级：

- **技术异常原文仅宿主可见**。它可能带表名、内部地址、主机名——租户管理员不该看到宿主的基础设施形态。
- **链路标识仅宿主可见**（对租户是无用且泄露内部拓扑的标识）。

字段级裁剪**必须在服务端完成**。交给界面「不显示」只是把数据下发了却假装看不见。

**模拟登录是这一节的核心，也是与主流的关键差异：**

宿主管理员以租户身份操作时，**租户方必须能看见**。否则审计表对被操作的一方是瞎的——租户管理员会看到「自己的某个用户」做了他没做过的事。所以：

- `impersonation.started` / `.ended` 对**租户可见**，不是 `Host` 层；
- **同一次模拟在两层各留一对**：租户侧 `impersonation.started` / `.ended`（目标是被模拟的账号）回答"谁以我的名义进来、什么时候走的"；宿主侧 `tenant.impersonation-started` / `-ended`（目标是租户，`Host` 可见）回答"我们的人进了哪家"。只记一层时，另一层的操作记录里这件事根本不存在——分库租户的记录本就不在宿主库里，一条跨层可见的记录做不到；
- 模拟期间产生的每条记录，操作人名是被模拟者、模拟者名是真实操作人，**两者都对租户可见**。模拟者取自会话上的模拟声明，**声明名必须与记录器读取的一致**，否则这一栏恒为空且不报错；
- 结束模拟时请求主体仍是被模拟者，写宿主侧那条之前要把主体换成发起人，否则操作人 Id 记到租户账号头上——两边管理员同名时，名字上看不出错；
- 界面上模拟记录必须有**视觉区分**，不能只靠一行小字——它是「别人以你的名义做的事」。

这是一条**信任属性**：一个能让宿主悄悄操作而租户看不见的审计系统，对租户没有价值。

## 5. 未登记动作按最严可见性处理

动作码经注册式定义登记类别、可见性与严重度。**未登记的动作码不是错误，但必须按最严一层（`Host`）落库**：把来历不明的记录默认放给租户看，是用「宽容」换「泄露」。同理，筛选选项按当前读者的可见性裁剪——否则筛选框里摆着一堆他永远筛不出东西的项，而「筛了没有」与「看不到」在界面上长得一样。

## 6. 保留期：搬运而非删除

审计表**默认只增不减**。保留期是一个需要有人显式做出、并为之负责的决定——一个默认就会动审计数据的开关，会让不知情的部署某天夜里悄悄丢掉合规所需的历史。

到期记录**搬入归档表，不删除**。理由与第 3 节的安全边界同源：留了删除入口，「清理误记录」迟早会变成「清理不想被看到的记录」，而那时这张表已经不能作为证据了。搬走的数据仍在库里，只是不再参与日常查询；真要彻底销毁，那是另一个决定，需要另一次显式授权。

由此推出两条实现约束：

1. **归档不写进记录存储的契约。** 存储契约声明「没有更新与删除，保留策略属于运维范畴」，给它开删除口子就是凿穿它自己声明的不变量。归档落在应用侧、直接用本项目的数据上下文，框架边界原样保留。
2. **归档表与原表的列必须逐一对齐。** 搬运是逐字段复制，少一列就是**静默丢数据**——搬完照样报成功，缺的那列到查归档时才会发现。列长度引用同一组常量，不写字面量。

### 6.1 后台扫描必须绕过租户过滤器

**这是一个错了不会报错的陷阱，值得单独记一条。**

多租户实体带着全局查询过滤器，而该过滤器在**无租户上下文**（宿主视角）下的语义是「只放行宿主自己的行」，**不是**「放行所有租户」。后台作业恰恰跑在无租户上下文里。

于是一个遍历全表的清扫作业，若没有显式忽略查询过滤器：

- 只会处理宿主那一部分，**所有租户的数据永远不被触及**；
- **不抛异常、不报警**，日志照常打印「处理成功 N 条」；
- 监控看到的是一个健康运行的定时任务。

症状要到很久以后才会以「为什么这张表一直在涨」的形式出现，而那时已经很难把它和当初漏写的那一行联系起来。

配套的代价也要写明：**归档表刻意不实现多租户接口**，以免读归档时再中一次同样的埋伏；代价是将来为归档表开放查询接口时，**必须在那一层自己按租户过滤**，没有过滤器兜底。这一点必须写进该类型的注释，否则后人会把它当缺陷「修复」成实现接口。

相关的通用判据见[隔离与授权场景](./isolation-and-authorization-scenarios.md)。

## 相关

- [设计原则](./design-principles.md)
- [隔离与授权场景](./isolation-and-authorization-scenarios.md)
