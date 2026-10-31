# 前端表单规范

遵循[界面规范](./frontend-ui.md)与[错误处理](./coding-frontend.md#6-错误处理)。必须用 Signal Forms 的 `form()`、`[formField]`；ESLint 拦截 `FormsModule`、`ReactiveFormsModule`，不用 ngModel。[v22 已稳定](https://angular.dev/events/v22)，仍须锁版本、升级后回归全部表单。

## 提交与焦点

`injectFormSubmission()` 返回 `(fields, root, action, report?, aliases?)`。`root` 是提交时查找首个错误的表单根元素，由 `viewChild` 取得；子流程传自己的区域，不按 id 查找。入口负责校验、服务端字段回填，并在渲染后按可见 DOM 顺序聚焦首个可操作错误控件（含 OTP）。`action` 等待 HTTP；父级返回 Observable 并处理成功副作用，不自行订阅或吞错。

默认 `report` 用 `common.requestError` 作标题、剩余消息作描述。`mapFormErrors` 忽略大小写匹配已声明顶层字段；DTO 别名、嵌套/索引路径由 `aliases` 显式指向字段。剩余错误只反馈一次，自定义反馈也不能回显整条响应；登录凭据拒绝不定位。

原生 form 写 novalidate，submit 事件与内部 submit 按钮共用入口。状态取 submitting() 以阻止重入，spinner 保留文字。invalid/pending 不预禁用，只按在途或可解释前提（权限、冷却、能力、批量零选择）禁用。搜索 Enter 不保存；URI 不嵌套 form，Enter 只添加。

## 字段与校验

字段用 `hlm-field` 容器关联 `hlmFieldLabel` 标签和 `hlm-field-error`，配合 `hlmInput` / `hlm-select`；校验态由 Brain `BrnField` 读取，与表单引擎解耦。OTP 用 FormValueControl，把值、touch、disabled、关联与 focus 接到真实 input。逐行自动保存保留草稿、本行错误及无效态，不重放未知结果；新编辑会话 reset 回填。

客户端验证器不带 message；模板用 `error.message ?? t('validation.' + error.kind, error)`，绑定 `[validator]="error.kind"`，错误对象整个作参数。内置错误的参数就是错误对象上的字段，占位符照它命名。服务端原文直接显示；字段特有规则用自定义类型：内置验证器通过 `error` 选项指定，如 `pattern(path.username, /^[a-zA-Z0-9_]{3,64}$/, { error: { kind: 'usernamePattern' } })`；跨字段规则由 `validate()` 返回 `{ kind: 'passwordMismatch' }`。同字段同类只报一次，长度与字符校验共用句子时合成 pattern。

校验键是拼出来的，静态引用查不到；每种内置/自定义 kind（含默认 pattern）都要有句子。
<!--#if (IncludeLocalization)-->
词条放 `public/i18n/{en,zh-CN}.json` 的 validation 段。
<!--#else-->
词条放 `frontend/src/app/shared/utils/english-text.ts`。
<!--#endif-->
范围须带单位，如“有效期须为整数，范围为 1 分钟到 24 小时”；裸 min/max 只用于无单位字段。说明/错误用 @if 二选一，销毁说明并注销关联，不用 CSS 隐藏；重发/等待放错误或操作区。说明放 Field description，贴齐输入或设置页标签列；独立方框像另一控件且难对齐。

## 输入与键盘

标识先 trim 再校验/提交；口令、连接串、恢复码、URI 原样。租户名称例外：编辑时与原名逐字相同就不 trim、不按新规则重验，原样提交。标识、邮箱、验证码输入框须用 `spellcheck="false"` 和 `autocapitalize="off"` 关闭拼写检查与自动大写。搜索和命令输入须有稳定可访问名。

| 角色 | type/inputmode | autocomplete |
| --- | --- | --- |
| 登录标识/口令 | text/password | username webauthn/current-password |
| 新口令、非登录机密 | password | new-password |
| 邮箱/电话 | email/tel | 资料用 email/tel，其余 off |
| 验证码 | text/numeric | one-time-code |
| 搜索、业务名称 | text | off |

自写 Enter/Esc 宿主加 `appImeGuard`，忽略 isComposing/keyCode 229，保留候选操作。Safari 用中日输入法手测选/取消候选及 compositionend 补发 Enter，结束后再单独 Enter 才执行业务。
