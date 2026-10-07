# 阶段五：文档单源与精简收口

## 1. 目标与判据

在阶段一至四的基础上，补齐两条要求：

1. **单一信息源**：一条规则或事实只在一处权威表述；其他位置只给一句话加链接。跨分发边界（随包文档与仓库文档、生成项目与仓库）因读者拿不到对方，允许保留最小必要副本，但措辞须与权威处一致。
2. **精简**：删除或压缩历史叙事、比规则长得多的理由、只复述规则的示例、闸门已强制却仍展开的内容、框架常识、与代码不符的陈述，以及模板仓库自身的维护事实。

权威归属沿用既有分类：项目 Skill 写通用流程；`template/docs/standards` 写项目技术栈规则；`backend/README`、`frontend/README` 写开发运行；`docs/deploy/README.md` 写部署；仓库 `docs/README.md` 规定三棵文档树；`docs/architecture/collaboration-scenarios.md` 规定 Skill 边界。

完成判据：

- 第 3 节每条处置完毕，或附理由保留。
- 第 2 节的不一致全部改正。
- 读取成本上限按新读数下调并通过。
- 新闸门能拦住文档引用不存在的文件或脚本。

## 2. 先修：与代码不符或互相矛盾（已核实）

| # | 位置 | 问题 | 处置 |
| --- | --- | --- | --- |
| C1 | `template/docs/standards/coding-backend.md:176` | 写的是仅 Development 开启 `ValidateScopes`，实际 `Program.cs:70` 在生产以外都开 | 改为“生产以外开启” |
| C2 | `template/README.md:18` | 目录树把 `scripts/` 标为条件出现，但 `check-error-codes.py` 无条件生成 | 去掉该条件 |
| C3 | `template/frontend/README.md:266-313` | 写了 `ng e2e`（项目没有 e2e 目标）、`ng generate module`、`.eslintrc.json`、`tailwind.config.js`、`src/assets`，均与实际不符 | 删除，目录说明改链接到 coding-frontend |
| C4 | `template/frontend/README.md:32` 与根 README | 一处写 `npm install`，一处写 `npm ci` | 统一为 `npm ci` |
| C5 | `template/docs/standards/api.md:139` 与 `coding-frontend.md:100` | 一处说按状态码处理，一处说不按状态码分支 | 补上限定：没有 `code` 时按状态码 |
| C6 | `README.md:184,190` | 写 `BREAKING CHANGE` → major，preview 版本形态缺 `.<run_number>` | 改为链接 versioning.md，不再复述 |
| C7 | `README.md:68` | 提到 push 脚本，但 `framework/build` 下没有 | 改为实际脚本名 |
| C8 | `docs/architecture/design-principles.md:66` | 引用“架构地图”，全仓库不存在 | 改为链接 collaboration §6 |
| C9 | `docs/architecture/frontend-ui-library.md:17` | 写“当前 21/22”，实际锁定 22.2.0 | 核实后修正或删除 |
| C10 | `template/README.md:163-181` | 贴出 schema 与历史表字面量，实际已由 `DatabaseSchema.Name` 统一 | 改为一句“改名只改 DatabaseSchema” |
| C11 | Spartan `rules/forms.md:50` 示例写 `error.message`，`frontend-ui` §2.6 按 `error.kind` 取词条 | 上游 Skill 与项目规范冲突 | 在 frontend-ui 写明以项目规范为准，不改上游 Skill |

## 3. 去重与精简清单

### 3.1 生成项目文档（D1a：运维事实）

| 事实 | 现位置 | 权威处 | 其余处理 |
| --- | --- | --- | --- |
| DbMigrator 预演与 `--apply`、DDL/DML 身份分离 | README、backend/README、deploy | 命令归 backend/README；身份与顺序归 deploy | 根 README 只留一行 |
| MigrationTarget，单租户失败不阻断其他租户 | README、backend/README、deploy | backend/README | 其余只留链接 |
| 配置分层、DefaultAdmin、Data Protection | README、backend/README、deploy | 开发归 backend/README；部署归 deploy | 根 README 只留启动命令 |
| 同源部署与 SameSite | auth、deploy、frontend/README（3 处）、backend/README、service-invocation | auth「浏览器认证」 | 其余一句加链接 |
| 依赖方在 Identity 的登记、`OAuth:ApiResources` | auth、service-invocation、两份 README、deploy | service-invocation | 其余只留链接 |
| 签名公钥 kid 刷新；Identity 多实例共享资源 | auth、deploy | kid 刷新归 auth；共享资源归 deploy | 另一侧只留链接 |
| ForwardedHeaders 写了两遍 | deploy:48 与 :54 | :48 | :54 只保留“不要关闭 HTTPS 要求” |
| 测试命令与 Vitest 说明 | README、backend/README、frontend/README、testing | testing | 各 README 只留一行 |
| 多语言接线 | frontend/README、frontend-i18n §3 | frontend-i18n | README 只留链接 |
| 精简项 | frontend/README 的环境准备、VS Code 插件、外链；deploy:224 讲模板仓库 CI；deploy:229-230 复述 Skill 的授权核对 | — | 删除 |

### 3.2 生成项目规范与 Skill（D1b）

| 事实 | 现位置 | 权威处 | 其余处理 |
| --- | --- | --- | --- |
| 缺规范时不停工 | SKILL「缺失文档」、development、quality | SKILL | 删 development:20；quality:16 只留测试相关的半句 |
| 同步精简 | SKILL:39、documentation:45 | documentation | SKILL 只留一句指针 |
| 审查与 CI 并行 | quality:10、testing §1.3 | quality | 删 testing §1.3 |
| 参考邻近同类实现 | SKILL、docs/README:7、coding-common §3.1、development | SKILL 加 coding-common | 删 docs/README:7 前半句 |
| DTO 层校验 | coding-common §3.3、coding-backend §5 | coding-backend | coding-common 只留指针 |
| 分层依赖方向 | coding-common、coding-backend §2、project-structure、backend/README | coding-backend §2 | 其余只留链接 |
| 成功返回体 / IActionResult | coding-backend:130、api §2.2 | api | coding-backend 只留链接 |
| 分页命名与排序白名单 | coding-backend:117,186、api §5 | api §5 | 删 api §5.2 中的重复部分和 coding-backend:117 |
| 未登记业务码默认 400、码必填 | api.md 7 处 | api §4 表 | 只在表中写一次 |
| 前端显示 detail、按 code 分支 | api、coding-frontend §6、frontend-i18n | coding-frontend §6 | 其余只留链接 |
| ServiceClientException 映射到 5xx | api:140、service-invocation | service-invocation | api 只留链接 |
| 敏感信息不进日志与响应 | coding-common、coding-backend、auth、api | coding-common §1 | 删 auth「敏感信息」节，其余只留指针 |
| 禁用 ngModel / Reactive Forms | coding-frontend:10、frontend-ui:54 | frontend-ui | coding-frontend 写“eslint 拦截” |
| Spartan 升级流程写了两遍 | frontend-spartan:7 与 :26 | :26 | 删 :7 |
| 精简项 | frontend-i18n:23-28（Transloco 内部机制）、:35（列出 check-i18n 判据）、:9（scope 清单）；coding-frontend:30-44 的依赖表（eslint 已管）；coding-backend:32-36、coding-common:66（.editorconfig 已管）；testing:93-113 的理由段；tech-stack:31-59 的选型套话 | — | 压到规则本身，或改为一句闸门指针 |

Spartan Skill 的 Nx 分支属于上游维护内容，不在本阶段删改。

### 3.3 仓库与框架文档（D2）

| 事实 | 现位置 | 权威处 | 其余处理 |
| --- | --- | --- | --- |
| 方案、计划与评估的生命周期 | docs/README.md，以及三个仓库 Skill 各一段 | docs/README.md | Skill 各留一句 |
| 分发面不写方案 | maintaining、framework、template 三个 Skill | template Skill「对外分发边界」 | 另两处各留一句 |
| 框架自检的 5 条命令 | dev-guide §8、framework Skill:55-59（逐字相同） | dev-guide §8 | Skill 只留链接 |
| 模板验证命令 | template dev-guide、template Skill、framework QA | framework QA 的 L1 表 | 其余只留链接 |
| Core 只依赖抽象；交付不留待办；规范入口表 | design-principles、两份 dev-guide、collaboration | 依次归 dev-guide §5、design-principles §4、collaboration | 删 design-principles §6，其余只留链接 |
| 模板 CI 计划、回执、预检、docs-only 规则 | framework QA、template QA | framework QA | template QA 只留模板差异 |
| 生成项目的测试规则 | template QA、template dev-guide、framework QA | 生成项目的 testing.md | 仓库文档只留链接 |
| 后台作业被租户过滤器漏处理 | isolation 场景文档、op-records-principles §6.1（逐句重复） | isolation 场景文档 | principles 只留分库那一层 |
| 操作记录的行为 | op-records-principles §5、§6、§8、§9 与组件文档、XML | 组件文档加 XML | principles 只留跨层决策依据 |
| 前端选型里的执行细节 | frontend-ui-library 与 template 规范 | template 规范 | frontend-ui-library 只留决定、依据与备选 |
| 版本递增与 Skill 安装命令 | 根 README、versioning、skills/README | versioning 与 skills/README | 根 README 只留链接 |
| 精简项 | upgrade-0.13.0 §22（非升级内容）与两节无编号；framework QA:119、:131-141（过程叙事与效率方法论）；template dev-guide §9:186-208（叙事与推测）；dev-guide:32-43、:387-394 与 template dev-guide:85-92（闸门已强制，展开过长）；op-records-principles:177（迁移叙事）；maintaining Skill 中重复的路由规则（:19 与 :21） | — | 删除，或压成一句规则加闸门名 |

`docs/reports/2026-10-03-*.md`（约 110K）属于 develop 上 CI 优化工作的在途证据，由那项工作的计划收尾时处理，本阶段不动。

## 4. 闸门（G5）

1. **文档引用存在性**：新增 `scripts/check-doc-references.py`，在模板源码和生成产物上检查文档中的引用，引用了不存在的东西就失败。
   - 检查对象：反引号包裹的仓库相对路径、`npm run <脚本>`、`ng <命令>` 的目标、`scripts/*.py` 和 `*.ps1`。
   - 白名单：有意引用生成后才出现的文件，或外部文件。
   - 带 `--self-test`；接入 check-all，并在矩阵的生成产物上执行。
2. **读取成本上限**：按本阶段结束时的读数下调，留约 1% 余量；不接入 check-all（沿用 P9 的裁定）。

## 5. 工作包与顺序

| 包 | 文件 | 依赖 |
| --- | --- | --- |
| D1a | 根 README、backend/README、frontend/README、deploy/README、auth.md、service-invocation.md | 无 |
| D1b | 项目 Skill、docs/README.md、api、coding-*、testing、frontend-*、tech-stack、project-structure | 无（与 D1a 文件不重叠） |
| D2 | `.agents/skills/*`、`skills/README.md`、`docs/{architecture,framework,template}/**`、根 README.md | 无 |
| G5 | 新闸门、check-all、矩阵接线、读取成本上限 | D1a、D1b |
| Z | Codex 终审、全量验证、删除本计划、推送 | 全部 |

约束：

- 每个包先对照源码核实前提再删改，并报告“删了什么、改为指向哪里”。
- 跨分发边界的副本只核对措辞一致，不删除。
- 模板文档里条件块整行独占，改完要生成后检查。

验证：

- `check-all`、锚点检查、`validate-skills`、`measure-template-read-cost --check`；
- 生成 identity-all-features、resource、standalone 三个场景并检查文档；
- `test-template-generation.py`。
