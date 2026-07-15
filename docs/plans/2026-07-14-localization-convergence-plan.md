# 多语言方案融合实施计划

> 目标：把两条并行的多语言分支融合成单一交付——以**我方运行时架构**（`feat/localization-framework`，当前在 git 暂存区）为骨架，吸收对方分支（`origin/feature/localization-support`，`928bc76`）确有价值的两处成果，补齐我方两个短板，并裁决二者互斥的框架 API。决策依据见同期方案对比（三路并行代码审读 + 2025–2026 业界一手佐证）。**本文是可执行任务分解，不是最终代码；每块附验证入口。**

---

## 一、决策与依据（为什么以我方为骨架）

两分支都实现了 `--include-localization`，但机制根本不同：

| | 我方（运行时） | 对方（编译期） |
| --- | --- | --- |
| 前端 | Transloco JSON 字典，运行时拉取 | Angular `$localize` + `.xlf`，每 locale 一个编译包 |
| 切换 | `setActiveLang()` 即时重渲染、不刷新 | `window.location.assign('/{locale}/')` 整页跳转 |
| 部署 | 单包 + 静态字典 | 多包按路径部署 |
| 后端键 | message 即键、具名占位 `{Email}`、JSON | 独立 `LocalizationKey` + 位置参 `{0}`、RESX |

**裁决：以我方运行时为骨架。** 依据：

1. **场景契合**：本项目是带登录/菜单/表单、需登录态即时切换的**管理后台**，且单包部署、CI 简单。运行时方案直接命中；编译期方案的最佳场景是「重 SEO 的多 locale 内容站」，与本项目错配，且整页刷新切换体验差。
2. **业界主流**：2025–2026 一手来源一致——React 生态 react-i18next / next-intl / react-intl、Angular 的 Transloco 均为**运行时键+JSON**；编译期 `$localize` 官方仅建议用于不需运行时切换的多包部署。antd / DaisyUI / Spartan / PrimeNG 等 UI 库**自身不做应用级 i18n**，都搭一个运行时库。我方路线与主流方向一致。
3. **AI 协作成本**：我方「加一条文案 = 改两个 JSON、无提取、无 per-locale 重建、不碰组件」，路径短、易被 AI 正确执行与验证；对方每加一句要「组件标注 → extract-i18n → 译文 → 多包重建」，AI 易只改一环致漂移。契合本仓库「AI 按最小闭环交付」的定位。

**对方并非做错**——其编译期链路已核实可用（启用时用条件 overlay 换入完整接线的 `angular.json`）。融合是取其两处强项，不是否定。

---

## 二、融合范围总览

| # | 工作块 | 来源 | 交付面 |
| --- | --- | --- | --- |
| A | 保留我方运行时骨架（框架 + 模板参数 + 后端迁移 + 前端 Transloco） | 我方（已在暂存区） | framework + template |
| B | **吸收：前端全量 UI 覆盖**（把界面文案迁成 Transloco 键） | 对方思路 | template-frontend |
| C | **吸收：模板集成测试**（culture 端到端 + 校验消息本地化） | 对方 | template-backend |
| D | **修短板 1**：关闭态 `package.json` 未用 transloco 依赖门控 | 我方短板 | template-frontend |
| E | **修短板 2**：错误 message-即-键的漏配英文兜底 | 我方短板 | framework |
| F | **裁决框架契约**：统一 `BusinessException` API（消除与对方互斥的成员） | 冲突项 | framework |

A 已完成（暂存待审）。本计划聚焦 B–F。

---

## 三、任务分解

### A. 骨架（已完成，暂存待审）

现状：framework `Leistd.Localization.*` + 异常本地化接入；模板 `--include-localization` 参数 + 后端装配 + 58 处 throw 迁键 + 双语 JSON 资源；前端 Transloco + `LanguageService` + `p-select` 切换器 + `accept-language` 拦截器；`test-template-matrix` on/off 两场景。矩阵已全绿。**本计划实施前，这批改动先按用户的一次性代码审查通过并落一个提交。**

### B. 吸收前端全量 UI 覆盖（我方最大短板）

现状差距：我方前端只收敛了错误链路 + 头部约 21 处，登录/注册/用户/仪表盘/对话框等**界面文案仍硬编码中文**；对方标注了约 225 处。

- B1. 盘点我方前端所有硬编码中文 UI 文案（`git grep` 中文字面量，排除已 transloco 化的）。可参考对方 `.xlf`（`origin/feature/localization-support:template/frontend/src/locale/messages.en-US.xlf`，2807 行，含源中文→英文译文）**作为翻译语料来源**，但落地方式改为 Transloco 键。
- B2. 定 UI 键命名规范（`模块.语义`，如 `login.title`、`users.table.name`），与后端错误键（`模块:语义`）区分冒号/点号。
- B3. 逐屏迁移：模板 `{{ 'key' | transloco }}`、TS 用 `transloco.translate()`；组件已有 `//#if (IncludeLocalization)` 门控则复用，纯 UI 文案在**关闭态保留中文字面量、启用态走 transloco**（`#if/#else`，与后端 throw 迁移同法）。
- B4. `public/i18n/{en,zh-CN}.json` 补齐所有 UI 键（en 为默认/回落）。
- B5. 规模较大（近全部功能组件），**建议用工作流按「一屏/一功能模块一个 agent」并行迁移**，统一键规范 + 双分支规则，主体汇总键入 JSON（同 A 阶段 throw 迁移的做法）。

> 验证：`test-template-matrix -Scenarios localization` 前端 `npm run lint`（含 prettier）+ `build` 通过；抽查启用态无残留中文、关闭态无 transloco 残留。

### C. 吸收模板集成测试

现状差距：我方只有框架单测（`JsonStringLocalizerTests` 6 例），无模板级 i18n 集成测试；对方有 `LocalizationTests.cs`（culture 端到端 + 校验消息本地化）。

- C1. 参考对方 `origin/feature/localization-support:template/backend/tests/CompanyName.ProjectName.IntegrationTests/LocalizationTests.cs`，在我方模板测试工程新增等价用例，但**适配我方 API**（JSON 资源 + `AddJsonLocalization` + message-即-键）：
  - C1a. 带 `Accept-Language: zh-CN` / `en-US` 请求一个会抛业务异常的端点，断言 `ProblemDetails.message` 随 culture 变。
  - C1b. 校验失败（DataAnnotations）请求，断言 `errors` 字段消息随 culture 本地化。
- C2. 该测试仅在 `IncludeLocalization` 启用时生成（`template.json` 文件级门控），加入矩阵 `localization` 场景的 `dotnet test`。

> 验证：`test-template-matrix -Scenarios localization` 后端 `dotnet test` 覆盖新用例通过；`no-localization` 场景不含该测试文件。

### D. 修短板 1：关闭态依赖门控

现状：`template/frontend/package.json` 无条件含 `@jsverse/transloco`，关闭态项目也装该未用依赖（因 JSON 不能内联 `#if`）。

- D1. 采对方的做法——**条件 `sources` overlay**：把含 transloco 的 `package.json` / `package-lock.json` 放入一个启用态专用源根（如 `.template.config/localization/frontend/`），`template.json` 加 `{ condition: IncludeLocalization, source: ..., target: frontend }` 覆盖，并在基线（关闭态）`package.json` 去掉 transloco。
- D2. 或（更简）用 `template.json` 的 `replaces`/`modifiers` 在关闭态删除该依赖行——评估哪种在 `dotnet new` 下更稳、`npm ci` 锁文件仍一致。
- D3. 确保两种 lockfile（含/不含 transloco）与各自 package.json 对齐，否则 `npm ci` 失败。

> 验证：`test-template-matrix` 两场景 `npm ci` 均过；关闭态生成项目 `package.json` 不含 `@jsverse/transloco`。

### E. 修短板 2：错误键漏配兜底

现状：我方 handler 把异常 `Message` 当键查表，查不到时原样返回——若漏配键，原始键 `Error:Xxx` / `User:Xxx` 会作为消息漏给终端用户。

- E1. 在 `BusinessExceptionHandler`（或 `JsonStringLocalizer` 消费处）加一层：当 `IStringLocalizer` 返回 `ResourceNotFound` 且键形如 `模块:语义`（含冒号、非成品句）时，回落到一个**通用可读英文**（如按 HTTP status 的 `Error:{status}` 通用键，已在框架默认资源中），而非漏出原始键。
- E2. 补单测：漏配业务键时响应 message 是通用英文兜底、不是裸键。

> 验证：`dotnet test framework/tests/Leistd.Localization.Tests`（新增兜底用例）+ 现有 6 例仍过。

### F. 裁决框架 `BusinessException` 契约（合并阻断项）

现状冲突：我方加 `WithData(name,value)` / `LocalizationData`（具名占位）；对方加 `WithLocalization(key, params args)` / `LocalizationKey` / `WithCode(int)`（独立键 + 位置参）。同一 `BusinessException` 上互斥——必须统一。

- F1. **定契约（建议保留我方具名占位为主）**：具名参数（`{Email}`）跨语言比位置参（`{0}`）更稳、AI 更易读写、不易错序。保留 `WithData` + message-即-键。
- F2. **借鉴对方两点**：
  - F2a. `WithCode(int)` 全码校验重载（校验 5 位且前缀匹配 HTTP）——纯增益，吸收。
  - F2b. 评估是否引入对方的 `IExceptionResponseLocalizer` 解耦点。**倾向不引入**：我方 handler 直接注入 `IStringLocalizer` already 够用且更简；对方该抽象的收益（多 localizer 覆盖链、把 title/errors 集中一处）在我方 JSON + 通用键方案下价值有限，引入反增概念。若未来确需业务项目替换整个本地化后端再议。
- F3. 明确**不采**对方的：独立 `LocalizationKey`/`LocalizationArguments`（与 message-即-键重复）、RESX、数字码键、`type` 改 `urn:leistd:error:{code}`（我方保留 RFC-link `type`，避免前端契约变更）。
- F4. 同步 `framework/docs/components/exception.md` 与 `localization.md` 到最终契约。

> 验证：`dotnet build framework` 0 错误；`check-docs-api-drift` / `check-docs-sync` 过；`test-package-consumption` 受影响包过。

---

## 四、实施顺序

1. **先合并 A**（用户代码审查通过 → 提交骨架）。
2. **F（框架契约裁决）** 优先——它是阻断项，且 E 依赖最终 handler 形态。F + E 一批，走框架验证入口（build/test/pack/包消费/doc 检查）。
3. **D（依赖门控）** 独立小改，随模板验证。
4. **B（前端全量覆盖）** 最大工作量，建议工作流并行；依赖 A 的 Transloco 骨架就位。
5. **C（集成测试）** 收尾，纳入矩阵。
6. 全部完成后跑 `test-template-matrix -Scenarios localization,no-localization` 两场景全绿（含前端 lint+build、后端 build+test）作为总验收。

---

## 五、残余风险 / 待确认

- **B 的翻译质量**：对方 `.xlf` 的英译可复用为语料，但需抽查（机器/人工混合翻译可能有不准）。
- **F2b 抉择**：是否引入 `IExceptionResponseLocalizer` 抽象——本计划倾向不引入，若团队预期「业务项目要整体替换本地化后端」则重新评估。
- **D 的 overlay vs replaces**：两种去依赖方式需实测哪种在 `dotnet new` + `npm ci` 下最稳，锁文件一致性是关键。
- **默认语言一致性**：确保融合后前后端默认语言统一为 `en`（我方已 en；对方前端源 locale 曾是 zh-Hans，融合时不采其前端配置，无此问题）。
- **对方分支处置**：融合后 `feature/localization-support` 的独有产物（`ci.yml`、`validate-skills` 等非 i18n 改动）是否单独摘取，超出本计划范围，另议。

---

## 六、参考

- 方案对比报告（本轮产出，含三路代码审读 + 业界佐证）。
- 我方现状方案：[`2026-07-13-localization-strategy.md`](./2026-07-13-localization-strategy.md)。
- 对方分支：`origin/feature/localization-support`（`928bc76`）。
