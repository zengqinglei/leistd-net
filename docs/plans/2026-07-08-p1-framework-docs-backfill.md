# P1 — 框架文档补齐 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 补齐框架 4 个缺失家族(auditing/authorization/notifications/realtime)+ 4 个 ddd-struct 项目的组件文档,照 `framework/docs/_doc-template.md` 骨架、严格对齐源码不臆造,并更新组件/ddd 索引——为 P2(发现 skill)、P5(文档随包)、P6(对外发布)提供内容前置。

**Architecture:** 每篇文档是独立研究-撰写单元:读该组件源码的公共 API(接口/DI 扩展方法/Options/运行时语义)→ 按模板骨架撰写中文文档 → 逐一核对方法名/接口名/命名空间/常量与源码一致。ddd-struct 四项目作为一个内聚层用一篇总文档覆盖。最后更新两处索引。

**Tech Stack:** Markdown(照 `_doc-template.md`);无编译;验证=源码 API 名交叉核对(grep)+ 结构完整性检查。

## Global Constraints

- 改动范围仅 `framework/docs/components/*.md`(新增 4)、`framework/docs/ddd-struct/*.md`(新增)、`framework/docs/components/README.md` 与 `framework/docs/ddd-struct/README.md`(索引)。不改源码、不改打包/CI(那是 P5)。
- **只写源码真实存在的公共 API**;方法名/接口名/命名空间/常量与源码逐一核对,禁止臆造(`_doc-template.md` 硬性要求)。
- 章节顺序照 `_doc-template.md`:`# 概念标题` → `## 何时使用` → `## 安装` → `## 配置 Provider`(纯抽象可省)→ `## 使用` → `## 接口参考` → `## 实现行为`(有运行时语义时)→ `## 配置项`(有 Options 时)→ `## 注意事项`(可选)→ `## 相关`。必留:何时使用/使用/相关。
- 中文撰写;不抄整段源码;文档自包含(不引入 `../` 之外的相对链接依赖,与现有 11 篇一致)。
- 参考样板:`framework/docs/components/event-bus.md`、`lock.md`(已按模板撰写)。
- 每任务独立提交,前缀 `docs(framework)`,结尾附 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`。分支 `develop`。
- 家族→包映射:一个家族多个包(如 notifications = Core/EntityFrameworkCore/AspNetCore.SignalR),一篇文档覆盖该家族全部包,安装章节注明哪个是抽象/实现。

---

## Task 1: auditing 组件文档

**Files:**
- Create: `framework/docs/components/auditing.md`
- Source to read: `framework/components/auditing/Leistd.Auditing.Core/*.cs`(IAuditedObject, IAuditPropertySetter, DependencyInjection)、`Leistd.Auditing.EntityFrameworkCore/*.cs`(AuditPropertySetter, AuditSaveChangesInterceptor, DependencyInjection)

**Interfaces:**
- Produces: `auditing.md`(供 Task 6 索引引用)。

- [ ] **Step 1: 读源码提取真实 API**

Run:
```bash
cd framework
cat components/auditing/Leistd.Auditing.Core/IAuditedObject.cs components/auditing/Leistd.Auditing.Core/IAuditPropertySetter.cs components/auditing/Leistd.Auditing.Core/DependencyInjection.cs
cat components/auditing/Leistd.Auditing.EntityFrameworkCore/DependencyInjection.cs components/auditing/Leistd.Auditing.EntityFrameworkCore/AuditPropertySetter.cs components/auditing/Leistd.Auditing.EntityFrameworkCore/AuditSaveChangesInterceptor.cs
```
提取:公共接口成员(IAuditedObject 的审计属性接口族:ICreationAudited/IModificationAudited/ISoftDelete 等——以源码为准)、DI 扩展方法真名、拦截器的状态映射语义(Added→Create/Deleted+ISoftDelete→软删)。

- [ ] **Step 2: 定义验证(先失败)**

Run: `test -f framework/docs/components/auditing.md && echo EXIST || echo MISSING`
Expected: `MISSING`

- [ ] **Step 3: 撰写 auditing.md**

按模板骨架撰写。要点(以 Step 1 实读为准,不臆造):
- `# 审计` 开篇:自动记录创建人/时间、修改人/时间、软删除,免手写样板。
- `## 何时使用`:实体需要审计字段/软删除时;实现审计接口即自动填充。
- `## 安装`:`Leistd.Auditing.Core`(抽象接口)+ `Leistd.Auditing.EntityFrameworkCore`(EF 拦截器实现)。
- `## 配置 Provider`:EF 侧真实 DI 扩展方法名(读 DependencyInjection.cs)+ 拦截器注册。
- `## 使用`:实体实现审计接口 → SaveChanges 自动填充的示例。
- `## 接口参考`:表格列审计接口族成员 + IAuditPropertySetter。
- `## 实现行为`:AuditSaveChangesInterceptor 的 EntityState→审计动作映射(Added/Modified/Deleted+ISoftDelete),Creator/Deleter 幂等"已设置则跳过"语义(以源码为准)。
- `## 注意事项`:软删除是状态翻转非物理删除(若源码如此)。
- `## 相关`:`[组件总览](./README.md)`。

- [ ] **Step 4: 验证 API 一致性**

Run:
```bash
cd framework
# 文档提到的每个 DI 方法名/接口名都能在源码 grep 到
for sym in $(grep -oE "Add[A-Za-z]+|I[A-Z][A-Za-z]+|Use[A-Za-z]+" docs/components/auditing.md | sort -u); do
  grep -rq "$sym" components/auditing/ && echo "OK  $sym" || echo "MISS $sym"
done
grep -q "## 何时使用" docs/components/auditing.md && grep -q "## 使用" docs/components/auditing.md && grep -q "## 相关" docs/components/auditing.md && echo STRUCT_OK
```
Expected: 无 `MISS`(每个符号都在源码中);`STRUCT_OK`。若有 `MISS`,该符号是臆造或拼错,回 Step 3 修正。

- [ ] **Step 5: Commit**

```bash
git add framework/docs/components/auditing.md
git commit -m "docs(framework): add auditing component doc

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: authorization 组件文档

**Files:**
- Create: `framework/docs/components/authorization.md`
- Source: `components/authorization/Leistd.Authorization.Core/*.cs`(IPermissionChecker, IPermissionDefinitionProvider, IPermissionGrantManager/Store, PermissionDefinition(Manager), PermissionSubject, DefaultPermissionChecker, DependencyInjection)、`.AspNetCore/*.cs`(PermissionPolicyProvider, PermissionAuthorizationHandler, PermissionRequirement, DependencyInjection)、`.EntityFrameworkCore/*.cs`(PermissionGrantRecord(+Configuration), EfCorePermissionGrant Manager/Store, DependencyInjection)

- [ ] **Step 1: 读源码提取真实 API**

Run:
```bash
cd framework
for f in components/authorization/Leistd.Authorization.Core/*.cs; do echo "=== $f ==="; cat "$f"; done
cat components/authorization/Leistd.Authorization.AspNetCore/DependencyInjection.cs components/authorization/Leistd.Authorization.AspNetCore/PermissionPolicyProvider.cs
cat components/authorization/Leistd.Authorization.EntityFrameworkCore/DependencyInjection.cs
```
提取:权限检查接口、权限定义 provider 机制、grant store/manager、ASP.NET Core 策略集成(PolicyProvider 动态策略)、EF grant 存储。注意已有测试 `tests/Leistd.Authorization.Tests/` 可佐证行为。

- [ ] **Step 2: 定义验证**

Run: `test -f framework/docs/components/authorization.md && echo EXIST || echo MISSING`
Expected: `MISSING`

- [ ] **Step 3: 撰写 authorization.md**

按模板。要点(实读为准):
- `# 权限授权` 开篇:声明式权限定义 + 运行时检查 + ASP.NET Core 策略集成 + EF 授权存储。
- `## 何时使用`:需要细粒度权限(非仅角色)、动态策略、持久化授权时。
- `## 安装`:Core(抽象+默认 checker)+ AspNetCore(策略集成)+ EntityFrameworkCore(grant 存储)。
- `## 配置 Provider`:三层各自 DI 扩展方法真名。
- `## 使用`:定义权限(IPermissionDefinitionProvider)→ 检查(IPermissionChecker)→ 控制器 `[Authorize(policy)]` 示例。
- `## 接口参考`:表格列核心接口成员。
- `## 实现行为`:PermissionPolicyProvider 动态策略生成、DefaultPermissionChecker 检查流程、EfCore grant 解析(以源码为准)。
- `## 相关`。

- [ ] **Step 4: 验证 API 一致性**

Run:
```bash
cd framework
for sym in $(grep -oE "Add[A-Za-z]+|I[A-Z][A-Za-z]+|Permission[A-Za-z]+|Use[A-Za-z]+" docs/components/authorization.md | sort -u); do
  grep -rq "$sym" components/authorization/ && echo "OK  $sym" || echo "MISS $sym"
done
grep -q "## 何时使用" docs/components/authorization.md && grep -q "## 使用" docs/components/authorization.md && grep -q "## 相关" docs/components/authorization.md && echo STRUCT_OK
```
Expected: 无 `MISS`;`STRUCT_OK`。

- [ ] **Step 5: Commit**

```bash
git add framework/docs/components/authorization.md
git commit -m "docs(framework): add authorization component doc

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: notifications 组件文档

**Files:**
- Create: `framework/docs/components/notifications.md`
- Source: `components/notifications/Leistd.Notifications.Core/*.cs`(INotificationPublisher, INotificationStore, NotificationPublisher, NotificationOutputDto, NotificationTypes, DependencyInjection)、`.EntityFrameworkCore/*.cs`(NotificationRecord(+Configuration), EfCoreNotificationStore, DependencyInjection)、`.AspNetCore.SignalR/*.cs`(NotificationHub, SignalRNotificationSender, DependencyInjection)

- [ ] **Step 1: 读源码提取真实 API**

Run:
```bash
cd framework
for f in components/notifications/Leistd.Notifications.Core/*.cs; do echo "=== $f ==="; cat "$f"; done
cat components/notifications/Leistd.Notifications.EntityFrameworkCore/EfCoreNotificationStore.cs components/notifications/Leistd.Notifications.EntityFrameworkCore/DependencyInjection.cs
cat components/notifications/Leistd.Notifications.AspNetCore.SignalR/SignalRNotificationSender.cs components/notifications/Leistd.Notifications.AspNetCore.SignalR/NotificationHub.cs components/notifications/Leistd.Notifications.AspNetCore.SignalR/DependencyInjection.cs
```
提取:发布接口(PublishToUser/Group/All 的异步方法真名)、store 接口、SignalR sender 的 group 前缀语义、NotificationTypes 常量。注意模板 backend 已用此组件(`template/backend` PackageReference),可佐证真实用法。

- [ ] **Step 2: 定义验证**

Run: `test -f framework/docs/components/notifications.md && echo EXIST || echo MISSING`
Expected: `MISSING`

- [ ] **Step 3: 撰写 notifications.md**

按模板。要点(实读为准):
- `# 通知` 开篇:持久化通知 + 实时推送(SignalR)+ 按用户/组/全体分发。
- `## 何时使用`:需要站内通知、实时推送、通知历史时。
- `## 安装`:Core(发布抽象)+ EntityFrameworkCore(持久化)+ AspNetCore.SignalR(实时推送)。
- `## 配置 Provider`:三层 DI 方法真名。
- `## 使用`:注入 INotificationPublisher → PublishToUserAsync 示例。
- `## 接口参考`:表格列发布/存储接口成员、NotificationOutputDto、NotificationTypes。
- `## 实现行为`:PublishToUser 走 store+sender、PublishToGroup/All 跳过 store 的不对称(以源码为准);SignalR sender group 前缀;EfCoreStore 排序/maxCount/幂等 MarkAsRead。
- `## 相关`。

- [ ] **Step 4: 验证 API 一致性**

Run:
```bash
cd framework
for sym in $(grep -oE "Add[A-Za-z]+|I[A-Z][A-Za-z]+|Publish[A-Za-z]+|Notification[A-Za-z]+" docs/components/notifications.md | sort -u); do
  grep -rq "$sym" components/notifications/ && echo "OK  $sym" || echo "MISS $sym"
done
grep -q "## 何时使用" docs/components/notifications.md && grep -q "## 使用" docs/components/notifications.md && grep -q "## 相关" docs/components/notifications.md && echo STRUCT_OK
```
Expected: 无 `MISS`;`STRUCT_OK`。

- [ ] **Step 5: Commit**

```bash
git add framework/docs/components/notifications.md
git commit -m "docs(framework): add notifications component doc

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: realtime 组件文档

**Files:**
- Create: `framework/docs/components/realtime.md`
- Source: `components/realtime/Leistd.RealTime.Core/*.cs`(IBusinessEventPublisher, IPresenceService, IRealtimeSubscriptionAuthorizer, RealTimeOptions, DependencyInjection)、`.AspNetCore.SignalR/*.cs`(RealTimeHub, SignalRBusinessEventPublisher, SignalRPresenceService, ClaimsSignalRUserIdProvider, DependencyInjection)

- [ ] **Step 1: 读源码提取真实 API**

Run:
```bash
cd framework
for f in components/realtime/Leistd.RealTime.Core/*.cs; do echo "=== $f ==="; cat "$f"; done
for f in components/realtime/Leistd.RealTime.AspNetCore.SignalR/*.cs; do echo "=== $f ==="; cat "$f"; done
```
提取:业务事件发布接口、在线状态服务(多连接计数语义)、订阅授权接口、RealTimeOptions 配置项、UserId provider 回退链。注意已有测试 `tests/Leistd.RealTime.Tests/`(含 TestDoubles.cs)佐证行为。

- [ ] **Step 2: 定义验证**

Run: `test -f framework/docs/components/realtime.md && echo EXIST || echo MISSING`
Expected: `MISSING`

- [ ] **Step 3: 撰写 realtime.md**

按模板。要点(实读为准):
- `# 实时通信` 开篇:业务事件实时推送 + 在线状态 + 订阅授权,基于 SignalR。
- `## 何时使用`:需要服务端主动推送业务事件、在线状态、按权限订阅频道时。与 notifications 的区别(realtime=通用事件通道,notifications=站内通知)。
- `## 安装`:Core(抽象+Options)+ AspNetCore.SignalR(实现)。
- `## 配置 Provider`:DI 方法真名 + RealTimeOptions 配置。
- `## 使用`:注入 IBusinessEventPublisher 推事件 + IPresenceService 查在线示例。
- `## 接口参考`:表格列三接口成员 + RealTimeOptions。
- `## 实现行为`:SignalRPresenceService 多连接计数(同用户多标签页)、订阅授权流程、ClaimsSignalRUserIdProvider 回退链(以源码为准);注明静态字典的进程内语义。
- `## 配置项`:RealTimeOptions 属性 + 默认值。
- `## 相关`。

- [ ] **Step 4: 验证 API 一致性**

Run:
```bash
cd framework
for sym in $(grep -oE "Add[A-Za-z]+|I[A-Z][A-Za-z]+|RealTime[A-Za-z]+|Use[A-Za-z]+" docs/components/realtime.md | sort -u); do
  grep -rq "$sym" components/realtime/ && echo "OK  $sym" || echo "MISS $sym"
done
grep -q "## 何时使用" docs/components/realtime.md && grep -q "## 使用" docs/components/realtime.md && grep -q "## 相关" docs/components/realtime.md && echo STRUCT_OK
```
Expected: 无 `MISS`;`STRUCT_OK`。

- [ ] **Step 5: Commit**

```bash
git add framework/docs/components/realtime.md
git commit -m "docs(framework): add realtime component doc

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: ddd-struct 层文档(覆盖 4 项目)

**Files:**
- Create: `framework/docs/ddd-struct/ddd-struct.md`(一篇覆盖 Domain/Application/Application.Contracts/Infrastructure 四层)
- Source: `ddd-struct/Leistd.Ddd.Domain/*.cs`(Entity, DataFilter, Repositories, ExtraProperties)、`Leistd.Ddd.Application/*.cs`(AppService 基类, ObjectMapperExtensions)、`Leistd.Ddd.Application.Contracts/*.cs`(PagedResultDto, Dtos)、`Leistd.Ddd.Infrastructure/*.cs`(EfCoreRepository, LocalEventSaveChangesInterceptor, ModelBuilderExtensions, DependencyInjection)

**说明:** ddd-struct 是内聚的四层基座,用一篇总文档 + 每层一节比四篇碎文档更符合"问题导向"(读者要的是"如何用这套 DDD 基座",不是四个孤立包)。

- [ ] **Step 1: 读源码提取真实 API**

Run:
```bash
cd framework
for f in ddd-struct/Leistd.Ddd.Domain/*.cs ddd-struct/Leistd.Ddd.Domain/*/*.cs; do echo "=== $f ==="; cat "$f"; done 2>/dev/null
for f in ddd-struct/Leistd.Ddd.Application/*.cs ddd-struct/Leistd.Ddd.Application/*/*.cs; do echo "=== $f ==="; cat "$f"; done 2>/dev/null
for f in ddd-struct/Leistd.Ddd.Application.Contracts/*.cs ddd-struct/Leistd.Ddd.Application.Contracts/*/*.cs; do echo "=== $f ==="; cat "$f"; done 2>/dev/null
for f in ddd-struct/Leistd.Ddd.Infrastructure/*.cs ddd-struct/Leistd.Ddd.Infrastructure/*/*.cs; do echo "=== $f ==="; cat "$f"; done 2>/dev/null
```
提取:Entity 基类(本地事件 add/get/clear)、DataFilter(软删/多租等过滤开关)、仓储接口、AppService 基类、PagedResultDto、EfCoreRepository(SaveChangesIfNeeded UOW 分支)、事件拦截器、全局过滤器应用。注意模板 backend 的 Domain/Application 层已引用这些(`Leistd.Ddd.Application`/`.Contracts`),现有测试 `tests/Leistd.Ddd.Infrastructure.Tests/` 佐证。

- [ ] **Step 2: 定义验证**

Run: `test -f framework/docs/ddd-struct/ddd-struct.md && echo EXIST || echo MISSING`
Expected: `MISSING`

- [ ] **Step 3: 撰写 ddd-struct.md**

按模板(层文档,章节适配):
- `# DDD 四层基座` 开篇:提供 Domain/Application/Contracts/Infrastructure 的基类型与约定,业务项目继承即得实体、仓储、AppService、分页、事件、审计集成。
- `## 何时使用`:用本框架搭 DDD 分层应用时;各层引用对应包。
- `## 安装`:四包各自定位(Domain 基类型 / Application 服务基类 / Contracts DTO / Infrastructure EF 实现)。表格:包 → 层 → 提供什么。
- `## 使用`:定义实体(继承 Entity/AggregateRoot)→ 仓储接口 → AppService → 返回 PagedResultDto 的完整最小示例。
- `## 接口参考`:分层小节表格列各层关键基类型/接口成员(Entity 事件方法、DataFilter、Repository、AppService、PagedResultDto)。
- `## 实现行为`:EfCoreRepository 的 SaveChangesIfNeeded UOW-vs-非UOW 分支、LocalEventSaveChangesInterceptor 事件收集、DataFilter 的 AsyncLocal 嵌套语义、全局过滤器应用(以源码为准)。
- `## 相关`:`[组件总览](../components/README.md)`、`[ddd-struct 索引](./README.md)`。

- [ ] **Step 4: 验证 API 一致性**

Run:
```bash
cd framework
for sym in $(grep -oE "I[A-Z][A-Za-z]+|Entity|AggregateRoot|PagedResult[A-Za-z]*|DataFilter|Repository[A-Za-z]*|AppService" docs/ddd-struct/ddd-struct.md | sort -u); do
  grep -rq "$sym" ddd-struct/ && echo "OK  $sym" || echo "MISS $sym"
done
grep -q "## 何时使用" docs/ddd-struct/ddd-struct.md && grep -q "## 使用" docs/ddd-struct/ddd-struct.md && grep -q "## 相关" docs/ddd-struct/ddd-struct.md && echo STRUCT_OK
```
Expected: 无 `MISS`;`STRUCT_OK`。

- [ ] **Step 5: Commit**

```bash
git add framework/docs/ddd-struct/ddd-struct.md
git commit -m "docs(framework): add ddd-struct layer doc covering 4 projects

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: 更新索引

**Files:**
- Modify: `framework/docs/components/README.md`(加 auditing/authorization/notifications/realtime 行)
- Modify: `framework/docs/ddd-struct/README.md`(指向 ddd-struct.md)

**Interfaces:**
- Consumes: Task 1–5 产出的 5 篇文档。

- [ ] **Step 1: 读现有索引结构**

Run:
```bash
cd framework
cat docs/components/README.md
echo "=== ddd README ==="
cat docs/ddd-struct/README.md
```
看清组件总览的表格/列表格式和依赖图,按同格式加新行。

- [ ] **Step 2: 更新 components/README.md**

在组件列表/表格按现有格式补 4 行:auditing、authorization、notifications、realtime,各指向 `./auditing.md` 等,一句话定位。若有依赖图(Mermaid),按实际依赖补节点(auditing/notifications/authorization 依赖 Core+EfCore,realtime 依赖 SignalR)。

- [ ] **Step 3: 更新 ddd-struct/README.md**

补一条指向 `./ddd-struct.md` 的链接,一句话说明"四层基座用法总文档"。

- [ ] **Step 4: 验证索引完整**

Run:
```bash
cd framework
for c in auditing authorization notifications realtime; do
  grep -q "$c" docs/components/README.md && echo "$c indexed" || echo "$c MISSING_FROM_INDEX"
done
grep -q "ddd-struct.md" docs/ddd-struct/README.md && echo ddd_indexed
# 每个 components/*.md(除 README)都在索引里(无遗漏)
for f in docs/components/*.md; do b=$(basename "$f" .md); [ "$b" = "README" ] && continue; grep -q "$b" docs/components/README.md && echo "$b ✓" || echo "$b NOT_IN_INDEX"; done
```
Expected: 4 家族 `indexed`;`ddd_indexed`;所有组件 `✓`。

- [ ] **Step 5: Commit**

```bash
git add framework/docs/components/README.md framework/docs/ddd-struct/README.md
git commit -m "docs(framework): index new auditing/authorization/notifications/realtime + ddd-struct docs

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: 端到端核验

**Files:** 无新增,仅核验。

- [ ] **Step 1: 全部家族有文档**

Run:
```bash
cd framework
# components/ 下每个家族目录都有对应 .md(object-mapping 已有;新增 4 个)
for d in components/*/; do
  fam=$(basename "$d")
  test -f "docs/components/$fam.md" && echo "$fam ✓" || echo "$fam NO_DOC"
done
```
Expected: 所有家族 `✓`(注:core/aop 等原有的也应在;auditing/authorization/notifications/realtime 现应 ✓)。若某家族名与 doc 文件名约定不同(如 dependency-injection),以现有 11 篇的命名约定为准,不视为缺失。

- [ ] **Step 2: ddd-struct 有层文档 + 索引闭合**

Run:
```bash
cd framework
test -f docs/ddd-struct/ddd-struct.md && echo ddd_doc_OK
grep -q "ddd-struct.md" docs/ddd-struct/README.md && echo ddd_index_OK
```
Expected: `ddd_doc_OK`、`ddd_index_OK`。

- [ ] **Step 3: 无臆造 API 残留(抽查)**

Run:
```bash
cd framework
# 对 5 篇新文档做一次统一的符号-源码交叉核对汇总
for doc in auditing authorization notifications realtime; do
  echo "--- $doc ---"
  for sym in $(grep -oE "\bAdd[A-Za-z]{3,}|\bUse[A-Za-z]{3,}" docs/components/$doc.md | sort -u); do
    grep -rq "$sym" components/$doc/ && echo "  OK $sym" || echo "  MISS $sym"
  done
done
```
Expected: 无 `MISS`。若有,回对应 Task 修正后重跑。

- [ ] **Step 4: 记录 P1 完成(无代码改动,可选提交核验说明)**

本任务通常无文件改动;P1 完成后在 `docs/superpowers/plans/` 同目录或 ledger 记录核验通过即可,无需额外提交。

---

## 任务→P1 目标映射

| P1 目标 | 覆盖任务 |
| --- | --- |
| 补 4 缺失家族文档 | Task 1–4 |
| 补 ddd-struct 文档 | Task 5 |
| 更新索引 | Task 6 |
| 全量核验(无缺失、无臆造) | Task 7 |
