# 整页离开的历史与往返缓存、浮层单测写法与列表关键字

状态：已完成 · 风险档：中

## 1. 需求

下游反馈 6 项（编号沿用反馈）。逐项核对模板与框架现状后的决定：

| 编号 | 反馈 | 模板/框架现状 | 决定 |
| --- | --- | --- | --- |
| 1 | 整页去认证留下历史条目，后退从往返缓存（bfcache）恢复出冻结页 | 同类问题存在。`AuthService.loadPage`、`ReturnUrlService.loadPage` 用 `href`/`assign`；模拟登录进出、外部登录与绑定按钮直接写 `window.location.href`；资源服务退出与退出确认用表单 POST。全部留历史、无 `pageshow` 处理 | 采纳，模板。收敛到一个整页导航服务，见下文 |
| 2 | 依赖方 step-up 无因子时落到后台安全页 | 模板没有 `acr`/升级认证（auth.md 已写"模板没有内置"）；同类分支"受限会话"已经转到认证外壳 `/auth/two-factor-setup` 并带同一缓存请求续跑 | 不采纳：模板无此缺陷，无需改动 |
| 3a | 合成 `element.click()` 打开 CDK 菜单不稳定，后续"不存在"断言误通过 | `dropdown-menu-hover-focus.spec.ts` 同写法；另有若干 spec 用合成点击打开菜单、弹层或抽屉 | 采纳，模板：浮层一律真实交互打开，并先断言已打开 |
| 3b | 伪计时器测 `hlmTooltip` 不稳定 | 5 个 spec 用假计时器推进提示（truncated-tooltip、stat-panel、workspace-dashboard、readonly-list-popover、webhook-subscription-table）；testing.md 讲 `parkPointer()` 的一句提到"渲染或假计时器前" | 采纳，模板：提示改真实计时器加轮询；"不出现"断言指令判定结果 |
| 4 | Sheet 宽度覆盖须带 `data-[side=right]:` 前缀 | 模板唯一定宽处（投递详情抽屉）已带前缀并有注释。官方示例 `w-[400px] sm:w-[540px] sm:max-w-none` 不带前缀，被默认类的属性选择器压住（实施时实测确认） | 采纳为规范一句：覆盖 helm 带变体前缀的默认类时写同一前缀；不改 helm |
| 5 | 用户关键字区分大小写 | 用户、角色（模板），租户显示名与操作记录（框架）区分大小写；租户 DTO 注释早已写"大小写不敏感"，实现未做到；Webhook（框架）与开放应用已不区分；api.md 示例本身是区分大小写的写法 | 采纳，模板 + 框架：关键字统一不区分大小写的包含匹配 |
| 6 | 游标分页与共享分页器 | 模板与框架只有偏移分页（`PageRequest`/`PagedResult<T>`） | 不采纳：偏移分页可以覆盖。翻页期间的新增用"首次加载时间作上限"的过滤避免重复与漏行；总数过慢时由该接口返回近似总数；外部增量拉取按水位（时间或版本）查询，不属于界面分页。唯一不能替代的是超大表深翻页的性能，出现"实测偏移翻页慢、筛选也解决不了"的列表时再评估 |

### 1.1 整页离开（编号 1）

依据：
- WHATWG HTML `location.replace()` 替换当前会话历史条目；web.dev《Back/forward cache》建议在 `pageshow` 且 `event.persisted` 时更新或重新加载过期、敏感的页面。
- 发起整页离开的那份文档离开前已经进入"离开中"（守卫返回 false、按钮忙碌、表单已提交），从 bfcache 恢复后守卫不重跑，只能整页重载。

做法：新增 `core/routing/full-page-navigation-service.ts`（`FullPageNavigationService`，root 提供），模板里所有整页离开都经它：

| 方法 | 用途 | 历史 |
| --- | --- | --- |
| `replace(url)` | 当前条目只是中转：守卫/401 发起的登录、认证完成后的续跑（`/connect/*`）、退出后的落点、模拟登录进出 | 替换当前条目 |
| `assign(url)` | 用户在有意义的页面上主动离开、后退应回来：外部登录与外部账号绑定按钮 | 保留 |
| `post(action, fields?)` | 必须整页 POST 的离开：资源服务退出（OIDC 退出重定向由浏览器完成）、退出确认（带 `request_uri`、确认凭据与防伪令牌三个隐藏字段）。服务统一建表单、加字段、提交 | 保留（表单提交无替换选项） |

条件编译：`replace` 所有形态都有；`assign` 只在 `ExternalLogin` 下存在，`post` 只在 `RemoteTokenAuth || OpenIddictServer` 下存在，成员与对应 spec 用例都用 `//#if` 包住。服务 spec 的样例地址用中性路径，不写 `/connect/*`（Standalone 下会被 ForbiddenTokens 扫出）。

三者离开前都置"离开中"；服务构造时登记 `pageshow`，`persisted && 离开中` 时 `location.reload()`，`DestroyRef` 移除监听。重载后应用按真实会话重新启动：已退出的回到登录，外部提供方返回的回到干净的登录页。`AuthService.loadPage`、`ReturnUrlService.loadPage` 删除，调用方与 spec 改为注入并观察该服务。

不在范围：同一文档内的 SPA 中转条目（如 Standalone 登录成功后 `navigateByUrl` 压入的条目，后退回到 `/auth/login`）不涉及往返缓存，不改为 `replaceUrl`。

不做：不给 HTML 加 `Cache-Control: no-store` 阻止 bfcache（代价是全站失去往返缓存，且浏览器正在放宽该规则，不可靠）；不在每个组件各写 `pageshow`。

| 要求 | 验收（Given / When / Then） | 覆盖 |
| --- | --- | --- |
| 中转条目不留历史 | Given 整页登录的形态（Resource、Identity 授权续跑）未登录打开深链，When 守卫发起登录并完成认证回到应用，Then 后退不回到空白的发起页 | 服务 spec；生成实例浏览器验收 |
| 恢复兜底 | Given 已经整页离开，When 页面以 `persisted` 恢复，Then 重新加载；未离开时的恢复不重载 | 服务 spec |
| 退出不可后退看到旧页 | Given 应用内连续导航两次后退出，When 后退两次，Then 都不显示退出前的已登录内容（两次都靠 `pageshow` 兜底：Resource 退出走 `post`，本地退出 `replace` 后更早的条目仍属同一文档） | 生成实例浏览器验收 |
| 验收可证伪 | Given 基线 `3c74a8fb`，When 同样操作，Then 能复现冻结页，并记录到 `pageshow` 的 `event.persisted === true`；浏览器若关闭了往返缓存，先打开再验 | 基线复现记录 |
| 外部登录可返回 | Given 点外部登录，When 在提供方页面后退，Then 回到可操作的登录页（不是一直转圈） | 服务 spec；浏览器验收可用 Mock 提供方地址代替 |

### 1.2 浮层单测（编号 3）

规则写进 testing.md 前端节，替换现有"假计时器验证提示"的说法：
- 打开菜单、弹层、选择框、抽屉用 `vitest/browser` 的 `userEvent`，先断言已打开（`aria-expanded="true"` 或浮层出现），再做后续断言。合成 `click()` 只用于不经指针模态的普通按钮。
- 提示用真实计时器：出现与消失用 `expect.poll`/`vi.waitFor` 等待。"不出现"直接断言被测指令的判定结果（如 `TruncatedTooltip` 判定未截断、提示被禁用），同一 spec 保留一条真实交互的出现用例作对照；依据引用 testing.md §4 已有的因果屏障条款，不另写。不用"先让另一个提示出现"作屏障：brain tooltip 失焦与指针离开会经 `switchMap` 取消未触发的显示，同组提示还会跳过延时，那样的断言恒为通过。假计时器只留给业务自己的防抖与最短时长。
- testing.md 改的是讲 `parkPointer()` 的那一句（"渲染或假计时器前"），不另起条款。
- 实施前先在 touch 实例下复现合成点击关菜单与假计时器漏读；复现不了也按"先断言已打开"整改，依据是断言可证伪。

排查范围：模板全部 spec 中经合成点击打开浮层和用假计时器推进提示的用例。

### 1.3 关键字（编号 5）

终局写法：关键字 `Trim().ToLowerInvariant()`，列 `.ToLower().Contains(keyword)`。依据：
- 可移植：Npgsql 译为 `lower()`，InMemory 测试同样可执行；`EF.Functions.ILike` 是 PostgreSQL 专有，InMemory 不支持。
- 不损失索引：前置通配的包含匹配本来用不上 B-tree 索引。
- 非确定性排序规则在 PostgreSQL 上不支持 `LIKE`，`citext` 要加扩展并改列类型，代价超过收益。

落点：模板用户、角色；框架租户显示名（`NormalizedName` 已规范化，不变）与操作记录（`Action`、`TargetId`、`TargetName`、`ActorName`）；框架 `ITenantManager.GetPagedAsync` 的 XML 注释、`multi-tenancy.md`、`operation-records.md` 的关键字说明补"不区分大小写"；api.md 的 `keyword` 行写明"不区分大小写的包含匹配"，coding-backend.md 示例改为这一写法。

| 要求 | 验收 | 覆盖 |
| --- | --- | --- |
| 不区分大小写 | Given 显示名 `Super Administrator`，When `keyword=admin`，Then 命中；用户名、邮箱、角色名、租户显示名与操作记录同理 | 模板集成测试；框架租户与操作记录组件测试 |

## 2. 设计

见 §1.1–§1.3。交付面：
- 模板：前端整页导航服务及调用方、spec 整改、testing.md / frontend-routing.md / frontend-spartan.md / api.md / coding-backend.md；后端用户与角色查询。
- 框架：`Leistd.MultiTenancy.EntityFrameworkCore` 租户列表与 `Leistd.OperationRecords.EntityFrameworkCore` 操作记录查询。属于实现向既有文档对齐的修复：提交用 `fix(multi-tenancy)`、`fix(operation-records)`，Patch，不带 `!`；合入 develop 自动发 beta。模板 PR 不依赖它，生成项目在下次同步框架版本时拿到修复，下游通知写确切的 beta 版本。

## 3. 计划

| 任务 | 产物 | 验证 |
| --- | --- | --- |
| A 整页离开 | 服务与 spec；替换全部整页离开调用；frontend-routing.md 在"前进/后退预期"条后补一条 | 三种形态生成（Identity + 外部登录、Standalone 不开外部登录、Resource）各跑 build、lint 与受影响 spec；基线复现后，Identity 与 Resource 生成实例有头浏览器验证后退 |
| B 浮层单测 | 复现记录；整改 spec；testing.md 一句 | 复现前后对比；整改过的 spec 连续多次（含 touch 实例）；全量交给 CI |
| C Sheet 规范 | 实测官方示例在当前 helm 下的实际宽度；frontend-spartan.md 一句 | 浏览器 spec 量宽或生成实例量宽 |
| D 关键字 | 模板用户/角色、框架租户与操作记录；模板文档两处、框架文档与 XML 注释 | 新增回归测试先失败后通过；框架组件测试；模板集成测试 |
| 交付 | 框架一个 PR（D 框架部分）；模板一个 PR（A–D 模板部分） | 读取成本闸门；受影响场景生成；代码审查通过且 CI 全绿后合并 |
| 通知 | 下游通知 | 写明基线与 beta 版本 |

## 4. 结果

交付：框架 PR #231（D 框架部分，合并提交 `6930fff6`，发布 `v2.0.0-beta.178`）；模板 PR #232（A–D 模板部分，合并提交 `925b8452`，随下一次 develop→main 发布）。Resource 真实 OIDC 链路的浏览器验收经用户确认不补测。

与计划的偏差：
- A：资源服务退出的地址改为 `AuthService.logoutUrl` 常量传给 `post`。Mock 覆盖闸门只把赋给表单 `action` 的字面量算作 POST，字面量直接传给 `post(...)` 会被当成 GET 而误报；常量在调用处不单独计数，端点本身已有 Mock。
- A：本地退出用 `replace` 后，Chrome 对同一浏览实例内的替换不放入往返缓存（`BrowsingInstanceNotSwapped`），后退是新加载、经守卫回到登录页；与"靠 `pageshow` 兜底"的预期不同，结论一致（看不到旧内容）。
- A：Identity 登录页启用了通行密钥条件式请求（WebAuthn），浏览器不把它放入往返缓存，登录页本身不会冻结；冻结复现在退出确认页与个人设置的外部账号绑定上。
- B：在触屏实例下各 60 次未复现"合成点击打开的菜单被关掉"与"假计时器漏读提示"，按"先断言已打开"整改，依据是断言可证伪。
- C：官方示例 `w-[400px] sm:w-[540px] sm:max-w-none` 在当前 helm 下实测 384px（视口 1280）与 375px（视口 500），带 `data-[side=right]:` 前缀后为 540px 与 400px。

验收记录：
- 基线 `3c74a8fb`：往返缓存可用（`pageshow` 的 `persisted === true`）；Identity 退出确认 POST 后后退两按钮禁用转圈、绑定 GitHub 后后退"绑定"按钮停在忙碌；Resource（Mock）守卫发起登录后后退回到空白发起页、退出后后退显示退出前内容。
- 改后：上述页面恢复后整页重载（导航类型 `reload`），按钮可用；授权续跑后历史长度不变、后退不回到发起页；本地退出后两次后退都不显示已登录内容。
- 关键字：新增集成测试修复前失败、修复后通过。

遗留：Resource 真实链路（Resource 后端 + Identity 登记 OIDC 客户端、Mock 关闭）的浏览器验收未执行，Resource 只有 Mock 的辅助证据；模拟登录进出未做浏览器验收（服务 spec 覆盖）；Mock 覆盖闸门把直接传给 `post(...)` 的字面量判为 GET，需另立一项修正闸门。

已上收：`template/docs/standards/frontend-routing.md`（整页离开）、`testing.md`（浮层与提示）、`frontend-spartan.md`（变体前缀）、`api.md` 与 `coding-backend.md`（关键字）。
