# 前端表单规范

## 适用范围

业务输入必须用 Signal Forms 与 Spartan Field；搜索豁免。OTP 实现 FormValueControl，转发 value、blur/touch、disabled、标签/错误与 focus。自动保存保留草稿、错误/无效态，不重放未知结果；会话 reset 回填。

## 提交与错误焦点

统一经 `submit(form,{onInvalid,action})`。action await HTTP，父级返回 Observable、不吞错。submitting 阻重入，spinner 保留文字。不因 invalid/pending 预禁用，按在途或可解释的业务前提（权限、冷却、能力、批量零选择）禁用。

`<form novalidate (submit)="$event.preventDefault(); save()">` 与 submit 按钮共用入口；外置按钮设 form，测试真实 Enter。URI 不嵌套 form，Enter 只添加。

onInvalid 渲染后聚焦；action 返回 `{kind:'server',message,fieldTree}`，await submit 后渲染聚焦。共用 focusFirstInvalid 按 DOM 顺序聚焦可见可操作首错（含 OTP）。

mapFormErrors 匹配顶层字段（忽略大小写），别名/嵌套/索引显式映射；剩余错误反馈一次，登录拒绝不定位。

## 校验与展示

客户端验证器不带 message。模板 `error.message ?? t('validation.'+error.kind,error)`：服务端/凭据拒绝用原文，其余按 kind 取词条并绑定 validator，参数用错误对象；字段特有规则用自定义 kind。同字段同类只报一次，共用提示的长度/字符集合成 pattern。范围含单位。
<!--#if (IncludeLocalization)-->
客户端 kind 均在 `public/i18n/{en,zh-CN}.json` validation 段登记。
<!--#else-->
kind 在 `frontend/src/app/shared/utils/english-text.ts` 登记。
<!--#endif-->
说明/错误 @if 互斥，注销 aria-describedby；重发/等待放错误或操作区。Field description 对齐字段，不画方框。锁定版本，升级回归。

## 输入语义与规范化

标识先 trim 再校验/提交；口令、连接串、恢复码、URI 原样，既有标识兼容例外保留。

标识、邮箱、验证码均 spellcheck=false/autocapitalize=off。

| 角色 | type（inputmode） | autocomplete |
| --- | --- | --- |
| 登录标识/口令 | text/password | username webauthn/current-password |
| 新口令、非登录机密 | password | new-password |
| 邮箱/电话 | email/tel | 自助资料 email/tel，其余 off |
| 验证码 | text/numeric | one-time-code |
| 搜索、业务标识/名称 | text | off |

搜索/命令有稳定名称。

## 键盘与输入法

自写 Enter/Esc 忽略 isComposing/keyCode 229，保留候选操作。Safari 用中日输入法手测：Enter/Esc 选/取消候选不执行业务；结束后再 Enter 才提交，记录 compositionend 补发 Enter。
