# 设置分区共享副本：第二轮扩展点

状态：已完成（Codex 代码审查两轮，复审通过） · 风险档：中（设置分区组件、页面状态与 spec 结构调整） · 基线：develop `bb281041` · 上一轮：`2026-11-10-settings-page-extension-points.md`（PR #264）

## 1. 需求

下游三仓按 #264 回执把 `setting-section.{ts,html,spec.ts}`、`settings-page-state{,.spec}.ts` 改成与模板逐字一致的共享副本时，比对出 8 处只靠声明表表达不了的差异（反馈 `settings-copy-align-20261110/template-feedback-2.md`）。目标是补齐扩展点，使共享文件在三仓逐字照搬，差异只留在 `setting-choices.ts`、`SettingService` 与外壳的提供者。

非目标：不改后端设置契约；不内置多值控件（框架 `AllowedValues` 是整值候选，按元素多选的语义属于项目）；不改外部登录布局。

验收（Given/When/Then）：
- **页面快照**：Given 会话刷新只返回本页范围的设置；When 初次读取或写入后重取；Then 页面显示该返回值。两次读取乱序返回时只认最后发起的那次，被取代的读取跟随最新一次落定。Given 外壳提供 `SETTINGS_LOAD_ENABLED = false`；Then 不发读取请求，页面按已加载的空快照处理。
- **写入端点**：组件只调用 `SettingService.saveSetting(setting, scope, value)`；模板实现账户走 `current-user`、系统走 `current-tenant`，项目改服务实现即可换端点。
- **分组纯函数**：`SettingScope`、`SettingGroup`、`settingsInScope`、`groupSettings`、`groupPath` 位于 `core/settings/setting-groups.ts`，layout 可直接引用。
- **偏好行**：Given `ACCOUNT_PREFERENCE_SETTINGS` 为空；Then 账户作用域不注入偏好分组、不显示"暂不可用"，也不取用偏好来源。偏好读写经 `ACCOUNT_PREFERENCE_SOURCE`（`preferences`/`overrides`/`known`/`write(field, value)`），组件不引用 `AccountPreferencesService`。
- **spec 桩表**：共享 spec 在用例内安装自己的声明表与偏好来源，不依赖项目声明表的内容；端点选择由 `setting-service.spec.ts` 覆盖。
- **写入包装**：Given 外壳提供 `SETTING_WRITE_GUARD`（如确认身份）；When 包装后的写入不发值就完成；Then 该行回到常态、保留草稿、不报错。默认透传。
- **自定义控件**：`SETTING_CONTROLS` 可登记组件类；组件经 `context` 输入拿到本行的值、占位、无效态与提交函数，行的标签、说明、错误、恢复按钮照常由分区渲染。
- **分区标题级别**：分区标题级别取 `SETTINGS_SECTION_HEADING_LEVEL`（默认 4，外壳可提供 3）。
- **非阻断**：发信测试只在 `Email` 形态编译，分组标识取自组件常量；文本类输入框带 `spellcheck="false" autocapitalize="off"`（`frontend-forms.md` 的写法）。

## 2. 方案

| 反馈 | 结论 | 落点 |
| --- | --- | --- |
| 1 页面范围快照 | 采纳 | 页面状态自持快照，`load()` 与写入后的 `refresh()` 共用序号；加 `SETTINGS_LOAD_ENABLED` |
| 2 系统作用域端点 | 采纳 | `SettingService.saveSetting`，按作用域与设置层级选端点 |
| 3 分组纯函数 | 采纳 | 移到 `core/settings/setting-groups.ts`，不再导出第二个入口 |
| 4 偏好分组与服务 | 采纳 | 表为空时整体跳过；`ACCOUNT_PREFERENCE_SOURCE` 在 `setting-choices.ts` 声明，默认取 `AccountPreferencesService`；偏好分组标识进声明表 |
| 5 spec 依赖示例 | 采纳 | spec 安装桩表与桩来源，写入断言按方法匹配 |
| 6 写入前再认证 | 采纳，改为通用写入包装 | `SETTING_WRITE_GUARD`，取消约定与 `Reauthentication.execute` 一致：不发值就完成 |
| 7 多值控件 | 不内置，改为自定义控件槽 | `SETTING_CONTROLS` 值可为组件类，`NgComponentOutlet` 传 `context` |
| 8 分区标题级别 | 采纳 | `core/settings/settings-section-heading-level.ts`，与三仓现有 token 同名同形 |

## 3. 计划

1. 新建 `setting-groups.ts`、标题级别 token；改页面状态、`SettingService`、`setting-choices.ts`、分区组件与模板。
2. spec：页面状态补乱序、范围与禁用读取用例；分区 spec 改桩表，补表为空、写入取消、自定义控件、标题级别用例；服务 spec 补端点选择。
3. 文档：`template/docs/standards/settings.md` §3 更新。
4. 两种本地化形态 spec、lint、构建；Codex 审查；PR。

## 4. 结果

- 定向：本地化开、关两种形态（identity-all-features、identity-capabilities-05）的 settings 与 core/settings spec 分别 178、139 项通过，prettier、eslint 通过。新用例做过变异：去掉空表跳过、把取消当失败、标题写死、被取代读取直接返回，四种都能让用例失败。
- 全量（worker-01）：两种形态前端 spec 分别 1854、1567 项通过，`ng build`、lint 通过。`check-all` 只有 `README.md:41` 指向 `.mcp.json` 的引用失败，原因是运行器快照排除了这个已跟踪文件，与本改动无关，由 CI 复核。
- 审查：Codex 首轮 2 个 P2、1 个 P3，已全部修复：
  - 被取代读取挂在自己的请求上，会阻塞写入队列；
  - 自定义控件取消后不能用同值重试；
  - 桩表换回顺序不对。
- 未做：浏览器验证。模板里没有设置用到自定义控件；标题级别默认值、页面快照内容都不变，可见变化只有文本框多了两个属性。
