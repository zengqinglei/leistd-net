# 前端多语言规范

默认语言英语，支持 en + zh-CN 运行时切换。编码规范见 [前端开发规范](./coding-frontend.md)，表单校验提示见 [前端界面规范](./frontend-ui.md#26-表单规范)。

## 1. 方案与默认语言

- **运行时库 Transloco**（`@jsverse/transloco`）：JSON 词条运行时加载，用户即时切换语言、单包部署——**不用** Angular 编译期 `$localize`（那是按 locale 出多包、无法运行时切换）。
- **默认语言英语（`en`）**，支持 `en` + `zh-CN`；回落语言 `en`。
- 全局词条 `public/i18n/{en,zh-CN}.json` 只放跨功能文案（common、validation、layout、menu、app、httpError、theme、language、impersonation）。功能词条放在 `public/i18n/<scope>/{en,zh-CN}.json`，当前 scope 为 account、settings、users、roles、permissions、tenants、openApp、operationRecords、workspace、platform、landing、forbidden。
- scope 文件直接放该命名空间的内容，不重复包一层 scope 名；例如 `users/en.json` 的 `page.title` 对应运行时键 `users.page.title`。loader 按 `{baseHref}i18n/{scope}/{lang}.json` fetch，兼容子路径部署。

## 2. 文案归属

| 类别                            | 归属                                          | 用法                                                                       |
| ------------------------------- | --------------------------------------------- | -------------------------------------------------------------------------- |
| UI 静态文案（菜单、按钮、标签） | 前端词条（权威）                              | 模板结构指令 `*transloco="let t"` 内 `{{ t('menu.users') }}`；TS 见第 4 节 |
| 业务错误消息                    | **后端资源**（权威，见 [API 规范](./api.md)） | 前端直接显示后端已本地化的 `detail`，不在前端重复维护业务错误词条          |

- 后端按 `Accept-Language` 返回本地化消息；`http-error-interceptor` 归一化错误，由发起操作的 feature 展示。仅在需要差异化 UI 行为时按业务 `code` 分支。前端词条提供网络断开、后端不可达等客户端兜底。

## 3. 关键接线（`core/`）

- `core/services/language-service.ts`：切换语言的唯一入口（`setDeviceLang` / `applyAccountLang` / `resetToDeviceLang`）。先加载全局及所有已访问 scope 的目标语言词条，成功后才激活 Transloco 并同步服务信号和 `<html lang>`；加载期间保留原语言，失败保持原语言并上报，连续切换只有最后一次生效。保留已访问 scope 是为了覆盖仍打开的弹窗，代价是后续切换也会加载离开页面的词条；不预载尚未访问的功能。失败后的再次选择会重试。返回的 Promise 等到加载落定且不 reject，启动流也等待它。设备偏好存 localStorage，账户偏好仅在内存生效。
- 同文件的 `provideLanguageFallbackStrategy()` 在官方策略扩展点抛出 `TranslationLoadError`，阻止 Transloco 8.4 的自动回落及失败后下一次 scope 成功时的自行激活；应用与测试共用这项装配。服务复用每条加载路径的 options，失败后清理 8.4 留下的回落计数，保证连续失败也能重试。`provideLanguageInitializer()` 在首帧前等待初始全局词条，失败退回英语，保留启动失败页。
- `core/i18n/translation-scopes.ts`：路由解析器从 `provideTranslocoScope` 读取完整登记（scope、alias、inline loader），组件创建前等待活动语言的词条；切换语言复用同一登记，加载期间语言变化会重新等待最终语言。alias 在合并词条前应用到 Transloco 映射，inline loader 先等待目标资源及配置的缺词回落资源，成功后通过官方 load options 传入，避免库缓存拒绝的 Promise；在途与成功资源复用，失败资源在显式重试时重新调用加载函数，不访问库的私有缓存。失败上报并取消导航，已有页面时保留原页面；首次深链没有原页面时，根组件复用启动失败卡片提供持久说明与重试，保留目标 URL（含查询与锚点），恢复后进入原目标，不创建缺词条的功能组件。重试单次导航使用 `onSameUrlNavigation: 'reload'`，使根地址 `/` 首次失败也能重新执行解析器，不改全局策略。scope 与语言切换重叠时，两边都会等待新增词条。
- `core/i18n/transloco-loader.ts`：加载全局 `{baseHref}i18n/{lang}.json` 与功能 `{baseHref}i18n/{scope}/{lang}.json`。生产 `postbuild` 递归展平整个 i18n 目录，配合 `flatten.aot`。
- `core/interceptors/accept-language-interceptor.ts`：注入 `Accept-Language` 头，**置拦截器链首位**（链定义在 `app.interceptors.ts`，应用与启动用例共用），使后端消息按当前语言返回。
- `app.config.ts`：`provideTransloco`（`defaultLang: 'en'`）+ `TranslocoHttpLoader` + `provideLanguageFallbackStrategy()`；单测装配 `core/i18n/transloco.testing.ts` 同样登记该策略。
- 语言选择器用 Spartan **dropdown-menu**（`hlmDropdownMenuTrigger` + `ng-template` 模板驱动菜单项，触发按钮用 `hlmBtn`，与铃铛/主题按钮风格一致），封装在 `shared/components/language-switcher`，挂在 `layout/components/default-header` 最右图标区（后台页在铃铛右侧）。

## 4. 新增文案

- **词条随功能归属，这是业务项目持续扩展的长期约定。** 不按词条数量选择性拆分，每个独立功能都拥有自己的 scope；通用布局和真正跨功能的文案才放全局。权限授权弹窗属于 permissions 功能，即使被其它管理页嵌入，也由 permissions scope 持有词条，由宿主路由一起预加载。
- 先分析引用归属：只有单一功能使用的文案放该功能 scope，真正跨功能的文案留在全局。新的独立懒加载功能新建 scope，两语言文件同时维护；不要把同一文案复制到多个 scope。路由登记 `providers: [provideTranslocoScope('orders')]` 和 `resolve: { translations: resolveTranslationScopes }`，注册与预加载读取同一来源；嵌套功能在自己的路由登记 scope。
- UI 文案在对应的 en、zh-CN 文件同时加键，并核对：各 scope 两种语言的键集合一致、值为非空字符串、插值与占位符一致、代码中的静态引用与动态对象前缀都有对应词条。嵌入组件的宿主关系按实际模板 selector 逐层追溯，从路由 loadComponent/component 进入，继承父路由及 loadChildren 的 scope 登记；每个入口都必须登记其整棵组件树需要的 scope。动态创建、非字面量路由或登记表达式不属于该静态判据，新用法需补充相应检查，不得绕过预加载约定。
- **模板文案用结构指令**：组件模板最外层包 `<ng-container *transloco="let t">`（组件 `imports` 引 `TranslocoDirective`），块内一律 `t('key', params)`。这是 Transloco 官方推荐的写法：一个模板只建一个订阅，`t` 带记忆化；切换语言时指令换掉 `t`、整个模板随之重绘（`reRenderOnLangChange: true`）。传给子组件的文案（如分页器的 `labels`）也在模板里用 `t` 组装。不要给模板写"返回译文的组件方法"——那需要自己读语言信号才会重绘，漏读一次整块就停在旧语言；按状态选文案时让模板写 `cond ? t('a') : t('b')`。
- 只使用一个功能命名空间的模板可写 `*transloco="let t; prefix: 'orders'"`，正文用 `t('title')`；`prefix` 是 8.4 的正式输入，`read` 已弃用。同时使用 common/validation 的模板保留完整键 `t('orders.title')` 与 `t('common.save')`。配置 `scopes.autoPrefixKeys: false`，避免 scope 注入后完整键被重复加前缀。关闭多语言时，外层裁成普通 ng-container，英文表使用与正文相同的键；使用 prefix 的模板英文表也必须用相对键。
- **TS 里随语言变化的文案**（导航菜单、筛选项、页面标题等由 `computed` / `effect` 产出、不经模板翻译的）一律用信号 API：固定的键用 `translateSignal('orders.title', {}, { scope: 'orders' })`（全局键用 `translateSignal('common.save')`）（在字段或构造函数里建，`computed` 里读）；一组键或键随数据变化（类别、动作码）用 `translateObjectSignal('orders.status', {}, { scope: 'orders' })` 取成对象再索引（显式 scope 负责加载，键仍用完整前缀），带点号的键按 `core/i18n/translation-text.ts` 的 `textAt` 逐级取。词条未到时前者是空串、后者是空对象，到达后自行更新。**不要**在 `computed` / `effect` 里「读 `activeLang()` + 同步 `translate()`」：要自己追踪语言，漏读一处就停在旧语言；首帧前、词条未到时取到的裸键还会被缓存。参数随每行数据变化的句子（`{{target}}` 之类）在 TS 里只定键与参数，由模板 `t(key, params)` 取。
- **事件发生时的一次性文案**（toast、确认框）直接 `transloco.translate()`：`LanguageService` 先加载全局与已访问 scope 再激活语言，路由在创建组件前预加载 scope，活动功能的词条总已就位，切换途中返回的请求取到的是原语言的真实文案；这类文案不需要随语言重算。要留在界面上的文案（写进 signal、随后渲染的错误说明）存键，由模板 `t` 取，否则切换语言时它停在旧语言。
- 不要直接调 `transloco.setActiveLang()` 切换语言——绕过 `LanguageService` 就没有设备偏好、`<html lang>` 与加载失败的退回。
- 业务错误文案：改后端资源（见 [API 规范](./api.md#41-异常本地化)），**不在前端加**。
- 货币/数字用 Angular `CurrencyPipe`/`DecimalPipe`。注意 **`LOCALE_ID` 是启动期注入、不随运行时语言切换自动改变**；如需格式也跟随切换，需自行传 locale 参数或重建相关视图，别假设它会自动联动。
