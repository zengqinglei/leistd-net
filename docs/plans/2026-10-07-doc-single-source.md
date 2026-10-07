# 阶段五：文档单源与精简收口

## 1. 目标与判据

在阶段一至四的基础上，补齐两条要求：

1. **单一信息源**：一条规则或事实只在一处权威表述；其他位置只给一句话加链接。跨分发边界（随包文档与仓库文档、生成项目与仓库）因读者拿不到对方，允许保留最小必要副本，但措辞须与权威处一致。
2. **精简**：删除或压缩历史叙事、比规则长得多的理由、只复述规则的示例、闸门已强制却仍展开的内容、框架常识、与代码不符的陈述，以及模板仓库自身的维护事实。

权威归属沿用既有分类：项目 Skill 写通用流程；`template/docs/standards` 写项目技术栈规则；`backend/README`、`frontend/README` 写开发运行；`docs/deploy/README.md` 写部署；仓库 `docs/README.md` 规定三棵文档树；`docs/architecture/collaboration-scenarios.md` 规定 Skill 边界。

完成判据：

- 第 3 节每条处置完毕，或附理由保留。
- 删除任何副本前，先把该副本独有的条款（限定、例外、边界）合入权威处；每个包报告“删了什么、指向哪里、独有规则落在哪里”。
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
| C5 | `template/docs/standards/api.md:139` 与 `coding-frontend.md:100` | 一处说按状态码处理，一处说不按状态码分支 | 写明分工：认证与协议处置由拦截器、启动服务按状态码处理（eslint 只对它们放行）；feature 使用归一化错误反馈，特定业务交互按稳定 `code` 分支；缺少 `code` 不构成 feature 按状态码分支的豁免 |
| C6 | `README.md:184,190` | 写 `BREAKING CHANGE` → major，preview 版本形态缺 `.<run_number>` | 改为链接 versioning.md，不再复述 |
| C7 | `README.md:68` | 提到 push 脚本，但 `framework/build` 下没有 | 改为实际脚本名 |
| C8 | `docs/architecture/design-principles.md:66` | 引用“架构地图”，全仓库不存在 | 改为链接 collaboration §6 |
| C9 | `docs/architecture/frontend-ui-library.md:17` | “当前 21/22”是支持范围（Brain 的 peerDependencies 允许 21、22），不是错误，但“当前”枚举容易过时 | 保留支持策略，去掉易过时的枚举，改为“以锁定依赖的 peerDependencies 为准” |
| C10 | `template/README.md:163-181` | 贴出 schema 与历史表字面量，实际运行时配置已由 `DatabaseSchema` 集中，但已生成的迁移保存快照值 | 改为：运行时 schema 与历史表配置集中改 `DatabaseSchema`；未部署时可重建基线迁移，已部署数据库须走正式迁移；去掉字面量清单 |
| C12 | `framework/docs/components/operation-records.md:19` | 写“登录尝试都不该记”，与 `docs/architecture/operation-records-principles.md:167` 及模板实现（`AuthAppService.cs:112,249` 记录登录成功与失败）矛盾 | 纠正组件文档的笼统排除：安全相关登录事件应记，高频失败按采样或限频边界处理；归 D2 |
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
| deploy:224 | 混有“模板仓库 CI 用真实 PostgreSQL”与“用生产数据副本验证升级路径与迁移重跑” | deploy | 只删前者，后者保留 |
| 测试命令与 Vitest 说明 | README、backend/README、frontend/README、testing | testing | 各 README 只留一行 |
| 多语言接线 | frontend/README、frontend-i18n §3 | frontend-i18n | README 只留链接 |
| 精简项 | frontend/README 的环境准备、VS Code 插件、外链；deploy:229-230 复述 Skill 的授权核对 | — | 删除 |

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
| 敏感信息不进日志与响应 | coding-common、coding-backend、auth、api | coding-common §1 | 先把独有边界合入 coding-common §1（日志禁止联系方式；匿名场景防账号枚举、管理员场景允许有帮助的非敏感提示），再删 auth「敏感信息」节，其余只留指针 |
| 禁用 ngModel / Reactive Forms | coding-frontend:10、frontend-ui:54 | frontend-ui | coding-frontend 写“eslint 拦截” |
| Spartan 升级流程写了两遍 | frontend-spartan:7 与 :26 | :26 | 先把 :7 独有的“小版本锁定”并入 :26，再删 :7 |
| 精简项 | frontend-i18n:23-28 的 Transloco 内部机制（保留行为规则：切换失败保持原语言、最后一次切换生效、深链失败保留目标并重试）；frontend-i18n:35（列出 check-i18n 判据）、:9（scope 清单）；coding-frontend:30-44 的依赖表（eslint 已管，保留下沉原则）；coding-backend §3.1 只删“文件范围 namespace”一行（IDE0161 已管；主构造函数、DTO 的 record/init/required 未被 .editorconfig 覆盖，保留），coding-common:66 的导入短名规则保留（IDE0001/0002 构建期不执行，靠评审）；testing:93-113 只压理由（保留配置变体复用的例外、批量更新后换作用域读取、真实缓存 TTL 验证）；tech-stack:31-59 的选型套话 | — | 压到规则本身；只有闸门真正覆盖的规则才改为一句闸门指针 |

Spartan Skill 的 Nx 分支属于上游维护内容，不在本阶段删改。

### 3.3 仓库与框架文档（D2）

| 事实 | 现位置 | 权威处 | 其余处理 |
| --- | --- | --- | --- |
| 方案、计划与评估的生命周期 | docs/README.md，以及三个仓库 Skill 各一段 | docs/README.md | Skill 各留一句 |
| 分发面不写方案 | maintaining、framework、template 三个 Skill | 仓库 `docs/README.md`（跨交付面的共同政策） | 各 Skill 只保留自身载荷范围（template/、framework/docs/）加链接 |
| 框架自检的 5 条命令 | dev-guide §8、framework Skill:55-59（逐字相同） | dev-guide §8 | Skill 只留链接 |
| 模板验证命令 | template dev-guide、template Skill、framework QA | framework QA 的 L1 表 | 其余只留链接 |
| Core 只依赖抽象；交付不留待办；规范入口表 | design-principles、两份 dev-guide、collaboration | 依次归 dev-guide §5、design-principles §4、collaboration | 删 design-principles §6，其余只留链接 |
| 模板 CI 计划、回执、预检、docs-only 规则 | framework QA、template QA | framework QA | template QA 只留模板差异 |
| 生成项目的测试规则 | template QA、template dev-guide、framework QA | 生成项目的 testing.md | 仓库文档只留链接 |
| 后台作业被租户过滤器漏处理 | isolation 场景文档、op-records-principles §6.1（逐句重复） | isolation 场景文档 | principles 只留分库那一层 |
| 操作记录的行为 | op-records-principles §5、§6、§8、§9 与组件文档、XML | 组件文档加 XML | principles 只留跨层决策依据 |
| 前端选型里的执行细节 | frontend-ui-library 与 template 规范 | template 规范 | frontend-ui-library 只留决定、依据与备选 |
| 版本递增与 Skill 安装命令 | 根 README、versioning、skills/README | versioning 与 skills/README | 根 README 只留链接 |
| 升级清单结构 | upgrade-0.13.0 §22 混有仓库 CI 叙事与消费者迁移要求（Angular 依赖更新与重装，`:591`）；`:630`、`:754` 两节无编号且含迁移要求（外部登录 API/字段删除与存量库删列；`IOperationRecordStore` 拆分后的消费者迁移） | upgrade-0.13.0 | 删去仓库 CI 叙事；无编号两节补编号；每节保持“旧用法 → 新用法 → 必要迁移动作”，迁移要求不删 |
| 精简项 | framework QA:119（过程叙事）、:131-141（效率方法论：保留定量性能改善的比较条件与 CI 计时口径，删过程数字）；template dev-guide §9:186-208（叙事与推测）；dev-guide:32-43、:387-394 与 template dev-guide:85-92（闸门已强制，展开过长）；op-records-principles:177（迁移叙事）；maintaining Skill 中重复的路由规则（:19 与 :21） | — | 删除，或压成一句规则加闸门名 |

`docs/reports/2026-10-03-*.md`（约 110K）属于 develop 上 CI 优化工作的在途证据，由那项工作的计划收尾时处理，本阶段不动。

## 4. 闸门（G5）

1. **文档引用存在性**：新增 `scripts/check-doc-references.py`。
   - **两种运行方式**：
     - 源码模式：根为仓库，扫描 `template/**/*.md`（不含 node_modules、libs/ui）、`docs/**/*.md`、`framework/docs/**/*.md`、`.agents/skills/**/*.md`、`skills/**/*.md`。路径按“文档所在交付面”的根解析：模板文档以 `template/` 为根，其余以仓库根为根。
     - 生成模式（`--root <生成项目>`）：根为生成项目，扫描其全部 `.md`，路径以生成项目根解析。
   - **检查对象**：
     - 反引号包裹、看起来像相对路径的引用（含 `/` 且带已知扩展名或以已知目录开头）。
     - `npm run <脚本>`：所属项目定为离文档最近的 `package.json`，模板为 `frontend/`。
     - `ng <执行目标>`（build、serve、test、lint、e2e 等）对照 `angular.json` 的 architect 目标；`ng generate` 视为 generator，不检查。
     - `python3|py scripts/*.py`、`pwsh *.ps1`。
   - **失败条件**：目标缺失、`package.json` 或 `angular.json` 解析失败、所属项目无法确定、扫描到的文档或引用数量为零。
   - **白名单**：放在脚本内，每条写明来源文件、引用原文、适用模式（源码或生成）和理由，精确匹配，不做前缀或模糊匹配。“生成后才出现”只豁免源码模式，生成模式仍须存在。
   - **自检反例**：缺文件、缺 npm 脚本、缺 Angular 目标、配置解析失败、白名单近似拼写不匹配、源码存在但生成后缺失、扫描为空。
   - **Markdown 文件链接**：生成项目内的链接已由 `test-template-matrix.ps1` 检查；仓库文档（含 D2 改动）的文件链接由本脚本源码模式检查。锚点仍由 `check-markdown-anchors.py` 负责。
   - **接线**：源码模式接入 check-all；生成模式接入矩阵的文档检查步骤。
2. **读取成本**：规则若迁入另一份必读文档，同步更新 `measure-template-read-cost.py` 的文件集合，并在提交说明里写明口径变化；上限按本阶段结束时的读数下调，留约 1% 余量；不接入 check-all（沿用 P9 的裁定）。

## 5. 工作包与顺序

| 包 | 文件 | 依赖 |
| --- | --- | --- |
| D1a | `template/README.md`、`template/backend/README.md`、`template/frontend/README.md`、`template/docs/deploy/README.md`、`template/docs/standards/auth.md`、`template/docs/standards/service-invocation.md` | 无 |
| D1b | `template/.agents/skills/leistd-project-workflow/**`、`template/docs/README.md`、`template/docs/standards/` 下除 auth、service-invocation 外的文件 | 无（与 D1a 不重叠；敏感信息边界并入 coding-common 由 D1b 做，D1a 删 auth 对应节须在其后） |
| D2 | `.agents/skills/{developing-leistd-framework,developing-leistd-template,maintaining-leistd-repository}/**`、`skills/README.md`、`docs/{README.md,architecture,framework,template}/**`、`framework/docs/components/operation-records.md`、仓库根 `README.md` | 无 |
| G5 | 新闸门、check-all、矩阵接线、读取成本集合与上限 | D1a、D1b、D2 |
| Z | Codex 终审、全量验证、删除本计划、推送 | 全部 |

约束：

- 每个包先对照源码核实前提再删改，并报告“删了什么、改为指向哪里”。
- 跨分发边界的副本只核对措辞一致，不删除。
- 模板文档里条件块整行独占，改完要生成后检查。

验证：

- `check-all`、锚点检查、`validate-skills`、`measure-template-read-cost --check`；
- 生成 identity-all-features、resource、identity-capabilities-08（关闭本地化）、resource-host-api-realtime（无前端）四个场景，检查移动过的条件块；
- `test-template-generation.py`。
