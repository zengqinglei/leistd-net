# 前端表单规范

遵循[界面规范](./frontend-ui.md)与[错误处理](./coding-frontend.md#6-错误处理)。必须用 Signal Forms 的 `form()`、`[formField]`；ESLint 拦截 `FormsModule`、`ReactiveFormsModule`，不用 ngModel。[v22 已稳定](https://angular.dev/events/v22)，仍须锁版本、升级后回归全部表单。

## 提交与焦点

`injectFormSubmission()` 返回 `(fields, root, action, report?, aliases?)`。root 传 viewChild 取得的真实元素，子流程限定自己的区域，不按 id 查找。action 等待 HTTP；父级返回 Observable 并处理成功副作用，不自行订阅或吞错。入口统一处理校验、字段回填和渲染后的焦点，按可见 DOM 顺序找首个可操作错误控件（含 OTP）。

默认 report 用 `common.requestError` 作标题、剩余消息作描述。`mapFormErrors` 忽略大小写匹配已声明顶层字段；DTO 别名、嵌套/索引路径由 aliases 显式指向字段。剩余错误只反馈一次，自定义反馈也不能回显整条响应；登录凭据拒绝不定位。

原生 form 写 novalidate，submit 事件与内部 submit 按钮共用入口。状态取 submitting() 以阻止重入，spinner 保留文字。invalid/pending 不预禁用，只按在途或可解释前提（权限、冷却、能力、批量零选择）禁用。搜索 Enter 不保存；URI 不嵌套 form，Enter 只添加。

## 字段与校验

控件放在 Spartan Field 内关联标签、说明与错误。OTP 用 FormValueControl，把值、touch、disabled、关联与 focus 接到真实 input。逐行自动保存保留草稿、本行错误及无效态，不重放未知结果；新编辑会话 reset 回填。

客户端验证器不带 message；模板用 `error.message ?? t('validation.' + error.kind, error)`，绑定 validator 并以错误对象作参数。服务端原文直接显示；特有规则用自定义 kind，同字段同类只报一次，共用句子的长度/字符校验合成 pattern。

校验键是拼出来的，静态引用查不到；每种内置/自定义 kind（含默认 pattern）都要有句子。
<!--#if (IncludeLocalization)-->
词条放 `public/i18n/{en,zh-CN}.json` 的 validation 段。
<!--#else-->
词条放 `frontend/src/app/shared/utils/english-text.ts`。
<!--#endif-->
范围须带单位，如“有效期须为整数，范围为 1 分钟到 24 小时”；裸 min/max 只用于无单位字段。说明/错误用 @if 二选一，销毁说明并注销关联，不用 CSS 隐藏；重发/等待放错误或操作区。说明放 Field description，贴齐输入或设置页标签列；独立方框像另一控件且难对齐。

## 输入与键盘

标识先 trim 再校验/提交；口令、连接串、恢复码、URI 原样，保留存量标识例外。标识/邮箱/验证码关拼写与自动大写；搜索/命令须有稳定可访问名。

| 角色 | type/inputmode | autocomplete |
| --- | --- | --- |
| 登录标识/口令 | text/password | username webauthn/current-password |
| 新口令、非登录机密 | password | new-password |
| 邮箱/电话 | email/tel | 资料用 email/tel，其余 off |
| 验证码 | text/numeric | one-time-code |
| 搜索、业务名称 | text | off |

自写 Enter/Esc 宿主加 `appImeGuard`，忽略 isComposing/keyCode 229，保留候选操作。Safari 用中日输入法手测选/取消候选及 compositionend 补发 Enter，结束后再单独 Enter 才执行业务。
