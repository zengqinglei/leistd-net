# 侧栏上下文识别与提交时校验

状态：已完成 · 风险档：中 · 关联：方案对比页 [design.html](2026-11-17-sidebar-context-and-submit-validation/design.html)；前序规格 [2026-11-16-shell-navigation-system-switcher.md](2026-11-16-shell-navigation-system-switcher.md)

## 1. 需求

### 问题与背景

外壳导航（PR #287）部署到演示站后，用户走查时提出三点：

- **租户选择框辨识度低：** 侧栏租户选择框是白底输入框的样式。浅色主题下菜单选中项也是白底，两者混在一起，看不出这是"当前所在的租户"。尺寸也比页头搜索框大一号。
- **平台管理入口辨识度低：** 侧栏底部的"平台管理"和"返回 <租户>"和普通菜单一模一样。进入平台后，顶部的租户框消失，没有任何东西告诉用户"现在在平台上"。
- **报错太早：** 登录、注册、新建/编辑弹窗里，用户刚离开输入框，字段就变红报错。

### 目标

- 侧栏顶部是"上下文位"，固定回答"我在哪"；底部入口负责"去哪里"。租户用主色，平台用反色，一眼可分。
- 表单改为"晚报错、早消除"：第一次提交前不报错；点提交后全量校验并报错；提交失败后边输入边重新校验。
- 提交按钮不因表单无效而禁用，并由静态检查固化这条规则。

### 非目标

- 不改租户选择、进入平台、返回租户的交互和路由，也不改它们何时出现。
- 不改校验规则本身、服务端错误回填、首个错误聚焦。
- 不加实时的用户名可用性检查等异步校验。

### 功能要求与验收标准

**侧栏上下文**

- AC1 Given 平台人员有多个租户可进，处在租户上下文；When 查看侧栏；Then 顶部租户选择框高 32px，与页头搜索框同高。它用主色浅底、主色细边，左侧主色楼宇图标，右侧上下箭头，宽度铺满侧栏。点击后搜索和选择的行为不变。（组件 spec 量尺寸与图标）
- AC2 Given 同上；When 查看侧栏底部；Then "平台管理"入口左侧是 26px 反色图标块（前景色作底、背景色作图标，盾牌图标），右侧是向右箭头，入口高 40px。
- AC3 Given 平台人员进入平台管理；When 查看侧栏；Then 顶部上下文位显示"平台管理"标识，与租户框同尺寸、反色、带盾牌图标，不可点击，没有箭头，也不在 Tab 顺序里。底部"返回 <租户>"左侧是主色楼宇图标块，右侧是返回箭头。
- AC4 Given 侧栏收起成图标；When 查看；Then 上下文位只留对应图标块，悬停显示租户名或"平台管理"。点租户图标块先展开侧栏，再打开租户下拉。
- AC5 Given 暗色主题；When 查看以上状态；Then 颜色规则相同：反色随主题反过来，主色浅底用主题变量。
- AC6 Given 租户用户、只有一个租户的人员，或关闭多租户；When 查看侧栏；Then 是否出现选择框和平台入口与现状一致，本次只改样式。

**提交时校验**

- AC7 Given 任一表单（登录、注册、找回口令、新建/编辑弹窗、设置页）尚未提交过；When 用户聚焦后离开一个必填空字段或格式不对的字段；Then 不显示红框和错误文字。
- AC8 Given 同上；When 点提交、登录或保存；Then 全部字段一起校验，错误显示在各字段下，焦点移到第一个错误；不发请求。
- AC9 Given 提交失败过一次；When 用户修改出错字段；Then 改对后错误立即消失，改错了立即提示，不用再点提交。
- AC10 Given 弹窗关闭后再打开，或表单被 reset；When 查看；Then 回到"未提交过"的状态，不残留红框。
- AC11 Given 口令规则、字数上限等说明；When 用户输入；Then 说明照常实时显示为普通颜色，不标红。
- AC12 Given 服务端返回字段错误；When 回填；Then 立即显示在对应字段，与现状一致。
- AC13 Given 任一提交按钮；When 表单无效；Then 按钮仍可点击，只在请求进行中禁用。静态检查会拦截把 `invalid()` / `valid()` 绑定到 `[disabled]` 的写法。

### 非功能要求

- 用 Spartan 官方扩展点 `provideErrorStateMatcher`（`@spartan-ng/brain/forms`），不绕开 `BrnField`，不在每个模板里手写显示条件。
- 不新增颜色变量，用主题已有的 primary、foreground、background。
- 平台不用琥珀色：页头"模拟登录中"提示已经用了琥珀警告色。

### 已确认的决策

- 2026-11-17 用户确认：租户框与页头搜索框同高，换背景色并加租户图标；平台管理入口做同类处理，采用对比页的反色方案；表单改为提交时校验；顺带加按钮禁用规则的静态检查。

## 2. 方案

### 现状事实

- 侧栏：`template/frontend/src/app/layout/components/default-sidebar/default-sidebar.html`。租户框是 `hlm-combobox-input`，默认高 36px；底部入口是 `hlmSidebarMenuButton`。
- 页头搜索：`default-header.html` 的 `hlmBtn variant="outline" size="sm"`，高 32px。
- 报错时机：`BrnField` 读 `ErrorStateMatcher.isInvalid(control, form)`，结果写成 `data-matches-spartan-invalid`，同时控制红框和 `HlmFieldError` 显示。默认规则是"无效，并且已失焦，或 `NgForm` 已提交"。Signal Forms 没有 `NgForm`，所以实际上就是失焦即报。官方提供 `provideErrorStateMatcher(...)` 在应用级或组件级替换规则，还自带 `ShowOnDirtyErrorStateMatcher`。
- 统一提交入口：`template/frontend/src/app/core/validation/form-submission.ts` 的 `injectFormSubmission()`，包装 Signal Forms 的 `submit()`。`onInvalid` 时聚焦首个错误，服务端错误经 `mapFormErrors` 回填。
- 按钮禁用：`frontend-forms.md` 规定"invalid/pending 不预禁用"。全量检索后，现有提交按钮只绑定 `saving()` / `submitting()`，但这条规则没有自动检查。
- 部分模板直接读 `touched()` 控制显示，例如 `reauthenticate-dialog`、`tenant-edit-dialog`、`tenant-detail-dialog`。要逐个改成走统一规则。

### 设计

**侧栏上下文：** 按 design.html 实现。

- 上下文位：
  - 租户上下文：在 combobox 输入上加前置楼宇图标，高度收到 32px，配主色浅底和细边。优先用 Spartan input-group 或 combobox 的官方插槽，不要另造控件。
  - 平台上下文：同位置渲染静态标识。
- 底部入口：图标块加文字，再加右侧箭头。
- 侧栏收起：按 AC4 处理。

**提交时校验：**

- 新增一个"已尝试提交"规则，挂成应用级 `ErrorStateMatcher`。规则是：控件无效，并且所在表单已尝试提交。
- `injectFormSubmission()` 每次调用 `submit()` 时，先标记这棵字段树"已尝试提交"，onInvalid、成功、服务端错误几种情况都要标。
- "已尝试提交"的状态按表单（字段树根）存放，表单 reset 或弹窗重开时清除。
- 规则在 computed 里求值，读的是信号，所以提交后编辑会自动重新判定。
- 怎么从 matcher 收到的控件找到所属表单，由实施方按 Signal Forms 的 interop 结构选最简单的可靠做法，并在 PR 里说明。

**静态检查：** 加到现有的模板静态检查脚本里，不新建框架。规则是 `.html` 中提交按钮或 `type="submit"` 的 `[disabled]` 绑定不得引用 `invalid()` / `valid()`；带自检用例。

### 备选与取舍

- `ShowOnDirtyErrorStateMatcher`（改过才报）：提交空表单时，必填项不会报错，不满足 AC8，不采用。
- 每个模板写 `@if (submitted() && field().invalid())`：散落几十处，容易漏，不采用。

### 兼容与回滚

- 只改模板前端。生成项目升级时，表单报错时机会变化，需要在发布说明里写明。
- 回滚就是撤掉应用级 matcher 的注册，恢复 Spartan 默认规则。

## 3. 计划

1. 侧栏上下文样式：租户框、平台标识、底部入口、侧栏收起、暗色主题，附组件 spec。（AC1–AC6）
2. 已尝试提交规则与 `injectFormSubmission()` 标记；清理直接读 `touched()` 控制显示的模板；附单测与代表性组件 spec（登录、注册、一个编辑弹窗）。（AC7–AC12）
3. 静态检查加禁用规则与自检。（AC13）
4. 更新 `frontend-forms.md`（报错时机，以及"已有检查"的说明）和 `frontend-navigation.md`（上下文位与颜色规则），注意模板规范读取成本闸门。
5. 验证：受影响形态（Identity×Workspace、Identity×Impersonation、Resource×Workspace、Standalone 关多租户、关本地化）的 lint、构建和受影响 spec；有头 e2e 覆盖登录页的报错时机；1440/390 截图对照 design.html。
6. 开 PR，目标 develop。

## 4. 结果

- 侧栏按设计实现 32px 租户上下文、反色平台静态标识、40px 底部入口及 26px 图标块；折叠保留图标与 tooltip，租户图标展开并打开原组合框。主题沿用 primary / foreground / background。租户用户、单工作区人员及关闭多租户的显示条件保持原有规则。
- 应用级注册 `provideErrorStateMatcher(SubmitErrorStateMatcher)`。统一提交入口按字段树记录尝试状态，以公开 interop 错误的 `fieldTree` 关联字段；`linkedSignal` 随 reset 清除状态，不访问 interop 私有成员。清理模板与再认证描述关联中的 touched 判断；URI 子表单接入统一入口。设置行以原有 change 自动保存为提交时点，原生数值错误改对即消除。
- `HlmField.spartanInvalid()` 让说明文字和错误复用 Brain 匹配结果；无直接 FormField 绑定的组合控件复用统一状态。已有服务端字段回填与首错聚焦回归通过。提交按钮有效性禁用规则加入现有 ESLint 配置，8 个反例/正例沿用 Node 自检入口。

| 实际生成形态 | lint / 生产构建 | 受影响组件测试 |
| --- | --- | ---: |
| Identity × Workspace（含 Webhook） | 通过 | 326 |
| Identity × Impersonation | 通过 | 313 |
| Resource × Workspace | 通过 | 107 |
| Standalone，关闭多租户 | 通过 | 240 |
| Identity × Workspace，关闭本地化 | 通过 | 314 |

以上共 1,300 次组件用例执行。设置行最后调整后，五形态再次 lint、生产构建及定向复验 620 项；侧栏读屏名与盾牌实际尺寸修正后，三个 Workspace 形态再次 lint、生产构建及侧栏复验 45 项通过。均由 remote-project-runner `--target auto` 调度，最终验证任务 cleanupComplete=true。

实际生成前端配合 Mock 的有头浏览器已验证登录、新建/编辑用户弹窗的失焦、提交、即时消错与重开清空，未提交有效写请求；侧栏验证 1440 / 390、浅色 / 暗色、折叠、展开选择框和平台/返回路径。实测上下文 32px、底部入口 40px、平台盾牌 16px，390 宽页面无整体横向溢出。截图与脱敏执行回执保存在本工作树 `.tmp/ctx-validation/`。

升级后的表单首次失焦不再显示校验错误，首次提交后实时重验；新增页面应使用统一提交入口。前端行为变更不涉及框架公共 API 或后端存储。
