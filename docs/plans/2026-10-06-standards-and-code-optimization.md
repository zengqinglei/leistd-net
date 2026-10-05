# 规范、Skill 与代码优化：方案与实施计划

分三阶段在同一分支 `zengqinglei/代码规范优化` 完成，阶段二结束前不合入 `develop`。每阶段方案经 Codex 审核通过后实施，阶段内改完先评审再提交。

| 阶段 | 内容 | 产出 |
| --- | --- | --- |
| 一 | Skill、规范文档、模板文档裁剪配置及配套校验 | 本计划第 2–5 节 |
| 二 | 按定稿规范审查并优化 Framework 与 Template 代码 | 审查清单（`docs/assessments/`）→ 追加到第 6 节 |
| 三 | 剩余项收尾 | 第 7 节；完成后删除本计划 |

**中间态边界**：阶段一可以让规范暂时领先代码，但领先部分必须在第 6 节有具体任务和验收；入口、文档引用、条件裁剪与校验脚本的一致性在阶段一闭环。生成文档不得把阶段二才落地的闸门写成"当前可执行"。合入前做一次"规范 → 实现 → 闸门 → 消费者"的完整核对。

## 1. 取舍原则

每条变更标注依据编号：

- **P1 终局**：一次改到位，不留兼容层、TODO 或"后续再收"；不为假想需求或名称整齐增加抽象、改公共 API。
- **P2 单一事实**：精确签名、清单和判据不重复维护；文档保留帮助开发者正确选择的契约、原因、例外与最小示例，并链接权威事实。
- **P3 按需加载**：Skill 与索引只路由；以任务的实际加载总量衡量 token 成本，而不是单个文件大小。
- **P4 可证伪**：规则写得出"怎样判定违反"；判据稳定且自动化收益成立的才做成闸门，文档如实描述闸门实际检查的范围。
- **P5 分发自洽**：`template/` 与 `framework/docs/` 只含生成项目或包使用者需要的内容，不依赖 leistd-net 仓库文件。
- **P6 开发者视角**：规则服务于写对代码，不为完整感补章节。

## 2. 生成项目的 Skill 与文档分工

### 2.1 定位与演进关系

| 载体 | 内容 | 维护者 |
| --- | --- | --- |
| `.agents/skills/leistd-project-workflow` | 与语言、技术栈无关的开发流程、通用原则和规范演进机制 | 模板维护者；生成后项目通常不改 |
| `docs/standards/` | 项目技术栈与个性化规范；生成时是 .NET/Angular 基线 | 生成后归项目所有，由 Skill 驱动积累与精简 |
| `docs/README.md` | 唯一文档索引与按任务读取表 | 项目 |
| `AGENTS.md`、`CLAUDE.md` | 入口指针 | 基本不变 |

`dotnet new` 是一次性脚手架：生成后模板升级不会覆盖项目文档或 Skill。项目新增的规则写入项目 `docs/`，不回流到通用 Skill，避免 Skill 积累技术栈耦合与 token。这一分工的权威说明放在 `docs/architecture/collaboration-scenarios.md`，其他位置链接。

### 2.2 Skill 变更

| # | 变更 | 依据 |
| --- | --- | --- |
| S1 | `coding-common.md` 的 §1–§3（开发、变更、验证规则）与 Skill `development.md`、`quality.md` 去重：Skill 未覆盖且跨技术栈都改变决策的条目（依赖最小化、安全默认）并入对应 reference，其余删除。§4 起的工程约束（DDD 分层、DTO 校验边界、导入风格等）与 §1 的项目语言约定保留在项目规范，并把文件开头与 §5 引言中"技术栈无关"的定位句改为"本项目跨前后端的约定"。不新增 reference 文件 | P2、P3 |
| S2 | `references/documentation.md` 增加"规范演进"：普通纠正或审查意见以第二次出现为默认沉淀阈值；用户明确决定、首次确认的重要契约、会导致重大错误的规则可首次沉淀。判据稳定的规则优先补闸门，文档保留原因、范围与例外。每次更新同步合并重复、删除过时条目。核心文档以约 10,000 字符为精简提示值：先删重复与冗长示例，再按独立任务主题拆分。新文档登记到索引 | P3、P4 |
| S3 | `development.md`、`quality.md` 改为"通过项目现有索引定位相关规范；没有索引或读取清单时从源码、配置、测试恢复事实"；删除 `quality.md` 中指向 `../../../../docs/standards/testing.md` 的专属链接 | P5 |
| S4 | `SKILL.md` 执行闭环末尾增加一步轻量判断：存在沉淀或精简候选时才加载 `documentation.md`；只读任务只报告候选，不改规范 | P3 |
| S5 | 模板根新增 `AGENTS.md`（只放 Skill 与 `docs/README.md` 指针）和 `CLAUDE.md`（只含 `@AGENTS.md`）；平台适配命令仍由 `template/README.md` 维护 | 发现链路 |
| S6 | `docs/README.md` 增加"按任务读取"表，允许任务组合，未列出的任务按主题索引读取。每类列出核心、专题与测试规范；写操作路由操作记录，权限或租户变化路由授权与隔离规范；前端与本地化条目保留条件裁剪；使用可点击的文件与章节链接 | P3 |

### 2.3 技术栈规范拆分

| 文件 | 内容 | 读取时机 |
| --- | --- | --- |
| `coding-common.md` | 项目跨栈约定：注释与日志语言、提交格式、工程约束（§5 原有条目）、对外契约枚举小写 | 任何代码改动 |
| `coding-backend.md` | 核心：技术栈、分层、编码、Controller（含操作记录留痕要点）、命名、数据访问、异常与日志、DI（新增） | 任何后端改动 |
| `api.md` | 核心：响应、状态码、异常映射、分页、路由、文档、兼容性 | 新增或修改接口 |
| `auth.md` | 现 `api.md` §5 与"浏览器认证"整节，保留条件块 | 改认证、授权、会话 |
| `coding-frontend.md` | 核心：技术栈、目录、命名、分层、DI、依赖方向、状态、错误处理、Mock、测试、日期时区 | 任何前端改动 |
| `frontend-ui.md` | 现 §4 组件与样式、§8 导航、界面文案（非本地化分支） + 合并 `ui-design.md` | 改界面 |
| `frontend-i18n.md` | 现 §9 多语言（本地化分支），条件裁剪与现状一致 | 新增文案、语言 |
| `testing.md` | 现有 + 数据库测试规则（见 D1） | 写测试、选验证范围 |
| `project-structure.md`、`tech-stack.md`、`service-invocation.md`、`deploy/README.md` | 保持 | 按索引 |

- 后端核心的精简以删除重复与冗长示例为主；`§3.9 设置与 Options` 等独立主题超过约 2,000 字符且只在特定任务需要时才拆为专题。
- 前端界面文案的本地化与非本地化分支各有唯一归属文件。
- `template.json` 在 `!SpaFrontend` 下排除 `frontend-ui.md`、`frontend-i18n.md`；删除 `ui-design.md`。
- **引用迁移**：全仓搜索被移动章节与文件的旧链接（含 `template/frontend/README.md`、`template/backend/README.md`、`template/docs/deploy/README.md`、根 `docs/`、Skill），逐一更新到新文件与锚点。

### 2.4 规范内容修订

**修正会误导实现的漂移**

| # | 位置 | 修订 |
| --- | --- | --- |
| C1 | `coding-backend.md` §3.5 分页示例、§5.1–§5.3 | 删除不存在的仓储 `GetPagedListAsync`、`GetQueryIncludingAsync` 与 `Leistd.Ddd.Infrastructure.Repositories`；按 `IRepository` 与 `IQueryableAsyncExecuter` 实际用法重写，含 §3.5 中直接 `CountAsync`/`ToListAsync`；删除"应用层可用 Include" |
| C2 | `api.md:184`、`coding-backend.md` 示例 | 仓储调用 `GetAsync` → `GetByIdAsync`（Controller/AppService 的 `GetAsync` 方法约定不变）；示例基类 → `BaseAppService, I*AppService`；`Guid.NewGuid()` → `Guid.CreateVersion7()`；统一实体构造、领域服务 `CreateUserAsync` 签名与应用服务中的全部调用（现 :100 与 :218 参数不一致）；保留的关键示例逐个核对可编译 |
| C3 | `coding-backend.md:519` | 删除错误指导"不要为 BCL 异常增加 `WithCode`"；`api.md:146` 的否定说明保留（防止臆造 API），不新增闸门 |
| C4 | `coding-backend.md:252` | 事务改为按需工作单元，并保留"工作单元内延迟提交、约束异常在冲刷或提交时发生"的行为说明（自开发指南 §7 下沉） |
| C5 | `coding-backend.md` §2.1、§7 | Api 目录按实际更新；区分业务周期 Job（Application `<模块>/BackgroundJobs`）、宿主启动 Initializer 与基础设施 Worker 的职责与位置 |
| C6 | `api.md` §3 | 删除"201 创建成功"，写明这是本项目业务接口约定（有响应体直接返回 DTO 即 200），不是协议层禁令 |
| C7 | `coding-frontend.md:134` | 生成项目中 `scripts/check-i18n-keys.ps1` 不存在：阶段一改为描述实际要求而不引用该脚本；随模板分发闸门列为阶段二任务 T2-F8，完成后再恢复引用 |
| C8 | 版本表述 | 区分"项目采用版本"（以 `package.json` 与锁文件为准，规范不写精确补丁号）与"UI 库支持范围"（`frontend-ui-library.md`）；同步 `tech-stack.md` |
| C9 | `coding-frontend.md` L19、L237 错误反馈 | 统一写成：拦截器负责认证处置与错误归一化；feature 决定字段回填、toast、空状态或静默；`GlobalErrorHandler` 只兜底未处理的非 HTTP 错误；同一错误不重复提示 |

**补齐缺失规则**

| # | 规则 | 定稿表述 |
| --- | --- | --- |
| R1 | 后端各层 `DependencyInjection.cs` 分工 | 每层拥有本层类型的注册，可调用实现本层能力所需的依赖注册入口；`Program.cs` 组合各层与组件入口、配置管道。部署基线 Options 在声明层或组合根绑定，宿主定向配置留在组合根 |
| R2 | 生命周期判据（框架与模板共用） | 先按状态所有权、并发安全、依赖链与实际消费作用域判断：持有请求或工作单元状态 → Scoped；跨请求共享且线程安全 → 可 Singleton；其余默认 Transient。Singleton 不得直接或间接捕获 Scoped；依赖作用域服务的 Transient 必须在正确作用域解析 |
| R3 | 注册方式 | 区分可替换单实现（`TryAdd*`）、多实现（`TryAddEnumerable`）、按业务键登记、命名 Options 与显式覆盖（`Replace`/`Add`，注释原因）。幂等定义为"相同登记重复调用不重复生效"，不同参数按入口契约处理 |
| R4 | DTO | 全部 record；`{Concept}Dto` 仅限 Client SDK 不成对响应；一个文件一个对外 DTO，仅被它内嵌使用的 item 类型可同文件 |
| R5 | 时间源 | 业务代码用 `IClock`；ASP.NET Core 认证、Cookie、票据等框架集成回调要求 `TimeProvider` 时可注入 `TimeProvider`。`check-clock-access.py` 只禁止静态读取当前时间，文档按此描述 |
| R6 | 框架 API 权威来源 | `coding-backend.md` 写最小定位方法：从 `obj/project.assets.json` 或 `dotnet list package --include-transitive` 确认实际还原版本，`dotnet nuget locals global-packages --list` → `{root}/{小写包名}/{版本}/docs/*.md`，精确签名以匹配项目 `TargetFramework` 的 `lib/{tfm}/*.xml` 为准；可选安装 `leistd-net-framework` Skill 获得完整流程 |
| R7 | 前端命名 | 补全后缀表（`.dto.ts`、`.model.ts`、`.routes.ts`、`-interceptor.ts`、`-handler.ts`、`-layout.ts`）；类名：服务带 `Service`，组件不带；函数式拦截器与类类型分别命名 |
| R8 | 前端分层 | API 数据访问服务只封装 HTTP 并返回 DTO；认证、主题、语言、启动等应用级服务可持有自身状态；页面状态优先组件 signal，复杂且有复用或生命周期收益时才抽组件级状态类；DTO 放 `dtos/`，需要行为或派生字段才建 `models/` |
| R9 | 前端 DI | 按是否跨页面共享、是否随页面销毁、是否跨子路由保留选择 `providedIn: 'root'`、组件 `providers` 或路由 `providers` |
| R10 | 前端依赖方向 | 见下方矩阵；阶段一写入 `coding-frontend.md`，阶段二按矩阵调整代码并用 eslint 守护（T2-F1） |

R10 依赖矩阵（行依赖列，✓ 允许）：

| 依赖方 \ 被依赖 | `core` | `layout` | 同一 `features/<x>` | 其他 `features/<y>` | `shared` | `_mock` |
| --- | --- | --- | --- | --- | --- | --- |
| `core` | ✓ | | | | ✓ | |
| `layout` | ✓ | ✓ | | | ✓ | |
| `features/<x>` | ✓ | | ✓ | | ✓ | |
| `shared` | | | | | ✓ | |
| `_mock` | ✓ | | ✓ | ✓ | ✓ | ✓ |

- 应用装配点（`app.config.ts`、`app.interceptors.ts`、`app.routes.ts`）可依赖全部目录，是业务代码之外唯一引入 `_mock` 的位置；测试文件（`*.spec.ts`、`*.testing.ts`）可引用 `_mock`。
- 多个 feature 共用的服务或契约下沉到 `core`（有状态或应用级）或 `shared`（无状态展示与契约）；依赖 `core` 服务的组件不放 `shared`。
- 业务代码判断 Mock 模式经应用装配点提供的注入令牌，不直接调用 `_mock` 中的函数。

**去重与下沉**

| # | 变更 |
| --- | --- |
| D1 | 先修正开发指南 :208 "省略 side 等于 Both"（side 已必填），再下沉：错误码放置 → `api.md`；事务 → `coding-backend.md`；侧别与租户维度（含"收紧侧别不撤销既有授权、需附撤销 SQL"）→ `auth.md`；只用 PostgreSQL 的规则与原因 → `testing.md`。仓库验证路径、历史测量与维护过程留在开发指南 |
| D2 | 异常分类只留 `api.md` §4，`coding-backend.md` 链接；用户名冲突的规范示例只留一处，JSON 契约示例与资源键不受影响 |
| D3 | `docs/template/browser-authentication.md` 删除与 `auth.md` 重复的运行契约，保留模板维护与测试要求并链接 `auth.md` |
| D4 | 映射规则只留 `coding-backend.md`，`coding-common.md` 链接 |

## 3. 框架规范

`docs/framework/development-guide.md` 与相关文档：

| # | 变更 | 依据 |
| --- | --- | --- |
| F1 | §1 补注册入口命名，按现状提炼：`Add{Family}` 由提供该家族主要公共能力的包承担（多为 Core），运行前提（如 Store）由组件契约声明；需宿主包补全时 Core 包用 `Add{Family}Core`；附加技术包用 `Add{Family}{Tech}`（如 `EfCore`、`SignalR`）；实现变体 `Add{Impl}{Capability}`；注册类为包根 `DependencyInjection`。配置节以 `Leistd:` 为根，允许家族、实现与命名实例层级。只约束新增 API，不为名称整齐批量改现有公共 API | P1、P4 |
| F2 | §1 目录：承认混合组织；目录按职责判断，`Filters/` 只放 MVC/Hub 管道过滤器，不按 `Filter` 后缀判断；同步修正 `coding-backend.md:580` 的同类后缀规则 | P4 |
| F3 | §5 写成"在 ddd-struct 内部 Infrastructure 只直接依赖 Domain、Application.Contracts 只依赖 `Leistd.Data`"；动态代理闭集例外只在开发指南维护，`design-principles.md` 与框架 Skill 链接 | 事实对齐 |
| F4 | §6 新增 DI 一节引用 R2、R3；"不替其他组件注册"收窄为：组件可注册完成自身功能必需且有契约声明的依赖；实现选择、业务端点与应用管道由宿主决定。同步 `design-principles.md:18` 与框架 Skill 旧表述 | P4 |
| F5 | §6.3 改为"不保留兼容层；破坏性变化进入 release notes，需要调用方迁移时补升级说明"，与 `versioning.md:161` 一致 | 消除矛盾 |
| F6 | 删除无持续价值的历史经过（开发指南 :335、:368 等，`versioning.md:154`），保留解释当前约束的简短原因；§8 命令清单链接 `check-all.ps1 -List`，§4.2"编译器与检查器各保证什么"保留 | P2 |
| F7 | 删除开发指南 :340 对模板测试名的规定，生成项目需要的部分进入模板 `testing.md` | 交付面边界 |

## 4. 仓库 Skill 与校验脚本

| # | 变更 |
| --- | --- |
| K1 | `developing-leistd-framework` 按任务路由到开发指南具体章节锚点（命名 §1、依赖 §5、API/DI/Options §6、测试 §7），删除与开发指南重复的边界句（F3、F4 定稿后） |
| K2 | `developing-leistd-template` 增加：改 `template/docs` 时同步索引读取表与核心文档精简；分工链接 2.1 节的权威说明，不复制表格 |
| K3 | `validate-skills.ps1`：允许生成项目的 `AGENTS.md`、`CLAUDE.md`；按 S1–S4 更新 workflow references 与 `requiredSkillMarkers`，只保留关键契约标记 |
| K4 | `collaboration-scenarios.md` 同步 2.1 分工与入口，补验收场景：无索引继续工作、重复意见触发沉淀、清理过时规范、只读审查不写文件 |
| K5 | `test-template-matrix.ps1`：standards 文件集合按 2.3 更新；允许 `AGENTS.md`、`CLAUDE.md` 并校验其指针；README 标记随 S5 调整；链接检查增加章节锚点校验（生成后的标题），覆盖条件裁剪删除被链接章节的情形；新增 `-GenerateOnly` 模式：只生成并执行形态断言与文档检查，不打包、构建或测试。锚点检查提供自检夹具（有效锚点、缺失锚点、本文件锚点、中文与重复标题、裁剪删除目标），纳入 `check-all.ps1` |

## 5. 阶段一验证

| 检查 | 方式 | 通过标准 |
| --- | --- | --- |
| 静态闸门 | `pwsh scripts/check-all.ps1` | 全部通过 |
| 生成产物 | `test-template-matrix.ps1 -GenerateOnly`，场景 `identity`、`standalone`、`resource`、`resource-host-api-realtime`（无前端）、`identity-localization`、`resource-localization`、`identity-all-features`、`standalone-external-login`；实施后再按实际移动的条件块核对每个分支至少被一个场景覆盖 | 文件集合、条件裁剪、文件与锚点链接、入口指针全部通过，无占位符残留；锚点检查自检通过 |
| 读取成本 | 实施前固定五类任务（后端 CRUD、全栈 CRUD、UI 调整、新增文案、只审查）的具体请求、功能开关、应读文件与章节及计算口径（去重后的实际加载字符数，含 SKILL.md、references、必需的 Spartan 文件），改前改后用同一口径实测 | 后端 CRUD 与全栈 CRUD 降低 ≥ 35%；其余任务不增加 |
| Skill 路由 | 按 `collaboration-scenarios.md` §6 抽查实现、只审查、部署、无索引项目四个场景 | 读取路径与交付边界符合预期 |

不运行框架测试、生成项目构建与测试：阶段一不改产品源码；未执行项在交付说明中列出，不记为通过。`-GenerateOnly` 由本阶段 8 个场景的实际运行与非法参数组合的拒绝验证；默认模式不变，由 PR CI 的矩阵执行覆盖。

### 5.1 阶段一结果

| 检查 | 结果 |
| --- | --- |
| 静态闸门 | `check-all.ps1` 32 道全部通过（含新增章节锚点自检与模板文档锚点） |
| 生成产物 | `-GenerateOnly` 8 个场景全部通过；移动的条件块（认证分支、前端、本地化）均被覆盖 |
| 读取成本 | 后端 CRUD 53,365 → 33,251（−38%）；全栈 CRUD 89,680 → 58,040（−35%）；UI 调整 51,672 → 39,411（−24%）；新增文案 33,266 → 18,728（−44%）；只审查 78,841 → 38,662（−51%） |
| 示例可编译 | `coding-backend.md` 的实体、领域服务、应用服务示例对框架源码编译通过 |

读取成本口径：对模板源文件（含条件标记、全部功能开启）按任务列出应读文件，去重后累加字符数（Python `len`）。公共部分为项目 Skill `SKILL.md` 与 `docs/README.md`；`S/` 表示 `docs/standards/`。

| 任务 | 改前读取 | 改后读取 |
| --- | --- | --- |
| 后端 CRUD（新增一个带分页查询的资源） | 公共 + `development.md`、`quality.md` + S/`coding-common`、`coding-backend`、`api`、`testing` | 同左 |
| 全栈 CRUD（上述资源加列表页） | 后端 CRUD + S/`coding-frontend`、`ui-design` + Spartan `SKILL.md` | 后端 CRUD + S/`coding-frontend`、`frontend-ui` + Spartan `SKILL.md` |
| UI 调整（改现有页面布局与表单） | 公共 + `development.md`、`quality.md` + S/`coding-common`、`coding-frontend`、`ui-design`、`testing` + Spartan `SKILL.md` | 同左，`ui-design` 换为 `frontend-ui` |
| 新增文案 | 公共 + `development.md` + S/`coding-common`、`coding-frontend` | 同左 + S/`frontend-i18n` |
| 只审查（全栈改动） | 公共 + `quality.md` + S/`coding-common`、`coding-backend`、`coding-frontend`、`api`、`testing` | 同左 |

与方案的差异：

- 读取成本达标需要额外拆出 `frontend-spartan.md`（Spartan 维护约定，只在加组件或升级时读），并精简 `testing.md` 中与 Skill 重复的审查流程表述。
- `frontend-i18n.md` 在 `!IncludeLocalization` 时整份排除，非本地化的界面文案归 `frontend-ui.md`。
- R6 不在 `coding-backend.md` 重复定位步骤，改为链接已有的 `backend/README.md#leistd-框架-api`。
- D3：`browser-authentication.md` 中与 `auth.md` 重复的令牌载体、票据、撤销与 Cookie 命名改为链接，保留组合根、测试接线与维护约束。

## 6. 阶段二：代码审查与优化

### 6.1 方法

按 Framework、模板后端、模板前端三块对照定稿规范审查，带证据（文件:行）的清单写入 `docs/assessments/2026-10-xx-code-audit.md`，送 Codex 审核后转为本节任务，assessment 随即删除。需要调用方迁移的公共 API 变化写入 `docs/framework/upgrade-0.13.0.md`。

### 6.2 已确认的种子任务

| # | 范围 | 任务 |
| --- | --- | --- |
| T2-K1 | Framework | 按 R3 审查非 `TryAdd` 注册（`Ddd.Infrastructure`、`Authorization.AspNetCore`、`Localization.AspNetCore` 等），区分有意覆盖与缺陷；补相同登记重复调用的测试 |
| T2-K2 | Framework | Options 入口违例：缺 `configSectionPath`、`IConfiguration` 与委托重载并存、内联校验 |
| T2-K3 | Framework | 按 R2 审查同类拦截器生命周期差异；按 F1 判定 `Security.Core.AddAmbientContext`、`TenantRouting` 配置节是否值得改 |
| T2-K4 | Framework | `Notifications.Core/Filters`、`Notifications.Settings/Filters` 中的投递过滤器不属于 MVC/Hub 管道过滤器，按 F2 移到所属功能目录 |
| T2-B1 | 模板后端 | `IUserSessionAppService`、`ITwoFactorAppService` 继承 `IAppService` |
| T2-B2 | 模板后端 | 按 R4 拆分多 DTO 文件、修正名不副实的文件与 `RoleBriefDto` |
| T2-B3 | 模板后端 | `ResourceAuthController.Me` 返回具体 DTO 并补 `Async` |
| T2-B4 | 模板后端 | 按 R1、R3 调整 `AddApplicationServices` 等注册与 Options 绑定，补注册测试；修正生命周期测试名与断言不符 |
| T2-B5 | 模板后端 | 判定疑似分层错位：`Domain/Shared/Json/JsonOptions`、`Domain/Auth/Options/OAuthOptions`、`Application/Shared/AuthenticationSchemeNames` |
| T2-F1 | 模板前端 | 按 R10 消除业务源码对 `_mock` 的依赖、`login` 对 `platform/tenant-service` 的跨特性依赖、`shared` 对 `core` 的依赖；eslint 守护 |
| T2-F2 | 模板前端 | 删除未使用的 `PlatformUserService`；`models/` 中的 DTO 移入 `dtos/` |
| T2-F3 | 模板前端 | 补缺失的服务、守卫、拦截器单测（先确认是否已被间接覆盖） |
| T2-F8 | 模板前端 | `check-i18n-keys` 随模板分发：生成项目路径适配、依赖的检查器、功能裁剪、执行入口与正反例验证；完成后恢复 `coding-frontend.md` 引用（C7） |

### 6.3 验证

按 `docs/framework/quality-assurance.md`：框架测试全集与受影响包消费验证；`scripts/test-template-matrix.ps1 -Tier pr`；数据库、认证、前端交互变化补真实检查。

## 7. 阶段三：收尾

- 复查遗留与新出现的漂移，运行全部静态闸门，完成合入前完整核对。
- `docs/assessments/` 中已交付的三份评估：有效结论上收到稳定文档后删除。
- 删除本计划。
