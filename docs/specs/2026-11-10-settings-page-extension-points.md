# 模板设置页扩展点：声明表、设置项依赖、Signal Forms 与分组图标检查

状态：实施中 · 风险档：中（重写设置分区组件、改生成项目的验证入口） · 基线：develop `ccb2c464` · 实施与端到端验收：Claude（用户 2026-11-10 指定） · 代码审查：Codex

## 1. 需求

下游三个服务比对了各自的设置页和模板 develop，发现三份 `setting-section` 与模板相差约 1150 行，三份之间也互差 500–660 行。分叉的原因是模板没有留扩展点：业务要加设置页能力，只能直接改通用组件，改完就再也同步不回模板。下游的期望是：三份设置分区改为与模板逐字一致的共享副本，各自的差异只放在声明表里。leistd 侧用户已于 2026-11-10 确认终局方案。

目标：
- 通用设置组件不出现任何具体的设置名。项目的差异全部写在 `setting-choices.ts` 声明表里，只改表，不改组件。
- 候选值只认后端定义的 `allowedValues`，前端不再另抄一份值域。
- 设置项依赖：沿用 `{前缀}.Enabled` 命名约定，另加显式依赖表，表达约定表达不了的情形（例如某个枚举取值不为 `off` 时才显示）。
- 设置分区改用 Signal Forms（Angular v22.0 起为稳定 API；模板其余表单都已在用；Spartan 有官方用法）。
- 服务端返回的字段错误回显到对应行的控件上。
- 生成项目新增分组图标检查：后端的每个系统设置分组都要在前端登记图标，登记的图标和兜底的齿轮图标都要在 `provideIcons` 里提供。

非目标：
- 不改后端设置契约（`allowedValues` 已经由框架下发）。
- 不改外部登录的专属布局。
- 不改个人偏好的内联形态：模板是单服务，内联是对的。
- 不加没有使用方的新控件类型。

验收：
- **零设置名**：Given 通用组件 `setting-section.ts` 与 `.html`；Then 文件里没有 `SETTINGS.` 设置名常量，也没有设置名字面量。时区、语言、主题的专用控件，日志级别说明，都由声明表分派。
- **候选值**：Given 后端给日志级别、主题、语言下发了 `allowedValues`；When 渲染；Then 候选项来自 `allowedValues`。文案按声明表取 `<前缀>.<值>`，缺词条时显示原值；没有登记前缀的设置直接显示原值。`LOG_LEVEL_CHOICES` 已删除。
- **按值说明**：日志级别选中后的说明取自声明表的 `<前缀>.<值>`，当前取值或继承取值都适用。
- **依赖**：
  - 命名约定：`X.Enabled` 下挂 `X.*`。
  - 显式依赖：`{ parent, shownWhen(parentValue) }`。
  - 两种来源合成同一棵树，支持多级缩进。
  - 上级不在页面上时，子项照常显示在顶层。
  - 收起时不清空值。
  - 外部登录分流后，提供方卡片不参与。
- **表单**：
  - 设置分区用 `@angular/forms/signals`。
  - Spartan 的 `hlmField`、`[formField]` 与错误显示关联正确：标签 `for`、`aria-invalid`、`aria-describedby`。
  - 保存、清除、继承回落、机密只写不回显、宿主生效徽章、窄屏布局等行为与现状一致，现有 spec 全部通过。
- **服务端错误**：Given 后端拒绝写入并返回 `errors[].field = value`；Then 错误显示在该行控件下方，并标记控件无效。业务错误码没有字段时，仍按现状显示在行内。
- **图标检查**：生成项目 `verify.ps1` 运行 `scripts/check-setting-group-icons.py`，任一条件不满足时失败；`--self-test` 至少包含缺登记、缺 `provideIcons`、缺兜底三个反例。条件裁剪后的各形态都通过。
- **形态**：本地化开、关两种代表场景，前端 spec、lint、构建、i18n 检查通过；Mock 站与真实设置页各做一次浏览器验证。

## 2. 方案

### 2.1 声明表（`setting-choices.ts`）

项目只改这一个文件。表的内容：

| 表 | 键 → 值 | 用途 |
| --- | --- | --- |
| `SETTING_VALUE_LABELS` | 设置名 → 词条前缀，或取文案函数 | 候选项文案。候选值来自 `allowedValues`；没有登记的显示原值。语言要显示各语种的本地名，用函数 |
| `SETTING_VALUE_DESCRIPTIONS` | 设置名 → 词条前缀 | 按当前取值显示的说明，取 `<前缀>.<值>` |
| `SETTING_DESCRIPTIONS` | 设置名 → 词条键 | 固定说明（沿用现状） |
| `SETTING_CONTROLS` | 设置名 → 专用控件种类 | 时区、语言、主题等专用控件；没有登记的按布尔、数字、文本、候选项的通用规则渲染 |
| `SETTING_DEPENDENCIES` | 子设置名 → `{ parent, shownWhen }` | 显式依赖，补充命名约定 |

类型集中在同一文件，供组件导入；组件不 import `SETTINGS`。前端 `SettingOutputDto` 补 `allowedValues?: readonly string[] | null`，Mock 数据同步下发。

### 2.2 依赖树

把 `nestUnderSwitches` 推广为 `buildSettingTree(rows, dependencies)`：
- 先按显式依赖挂接，再按 `{前缀}.Enabled` 约定挂接（最长前缀优先）；同一子项两种都命中时，以显式依赖为准。
- 节点带层级，模板按层级缩进。
- `isShown`：沿祖先链，每一级都要满足显示条件。约定节点的条件是上级取值为 `true`；显式节点按 `shownWhen` 判断。上级的取值看草稿，没有草稿看继承值。
- 上级不在当前页面上（被分组或权限过滤掉）时，子项挂在顶层。
- 不处理成环。声明表由开发者维护，组件只按表执行，规则写在注释里。

### 2.3 Signal Forms

每个分区一个 `form()`，模型按设置名建字段（各行的草稿值）。
- 草稿、脏标记、提交中、成功、失败等状态改由表单的字段状态和组件的少量信号表达，不再维护自管草稿表。
- 行控件绑定 `[formField]`，外层是 Spartan 的 `hlmField`，错误用 `hlmFieldError`。
- 服务端错误：保存失败时用模板已有的 `mapFormErrors` 等映射，经 `validateTree` 或提交错误机制挂到该行字段上，与对话框表单的写法一致。没有字段的业务错误，仍按现状显示在行内。
- 客户端校验保持现状的值域，例如数字范围来自后端的元数据，不新增规则。

具体 API 以 Angular 22 官方文档和模板现有的 Signal Forms 用法（对话框、`core/errors/form-errors.ts`、`core/validation/form-submission.ts`）为准，不另造一套辅助层。

### 2.4 分组图标检查

`template/scripts/check-setting-group-icons.py`（与 `check-error-codes.py` 同一形态），由 `verify.ps1` 调用：
- 解析后端 `SettingGroupPermissions` 的分组常量（经 `SettingConstant.Groups` 解析出字符串值）。
- 解析前端 `GROUP_ICONS` 与 `provideIcons`。
- 检查三条：每个分组都已登记；登记的图标都已提供；兜底图标已提供。

脚本在生成项目中运行，看到的是已裁剪的源码，所以不用处理条件块。模板仓的生成检查覆盖各形态。

## 3. 计划

分工：Claude 实施与端到端验收（按质量与 token 额度拆给子 agent）；Codex 只做代码审查，以背景、策略、规范与不过度设计等原则为口径。

规范：模板 `template/docs/standards/coding-common.md`、`coding-frontend.md`、`project-structure.md`、`testing.md`、`settings.md`；Spartan 官方用法；模板维护规范 `docs/template/development-guide.md`、`quality-assurance.md`。

步骤：
1. 按 §2 实施：声明表与 DTO → 依赖树（纯函数与 spec）→ 设置分区迁移到 Signal Forms → 服务端错误回显 → 图标检查脚本与 `verify.ps1` 接线 → 生成项目文档（`settings.md` 的前端声明表一节）。
2. 定向验证：两种代表场景（本地化开 `identity-all-features`、本地化关 `identity-capabilities-05`）跑前端 spec、lint、构建、i18n，后端不受影响；图标脚本跑自检与生成场景；PR 选择器选出的场景跑 `-GenerateOnly`。
3. 端到端：Mock 站与真实设置页的浏览器验证（保存、清除、继承、依赖展开收起、服务端错误回显、窄屏）。
4. 本地提交，交 Codex 代码审查；通过后推送、开 PR，CI 全绿即合并，回执三仓。

## 4. 结果

（实施后填写。）
