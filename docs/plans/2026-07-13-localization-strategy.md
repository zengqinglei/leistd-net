# 多语言（i18n）最佳实践方案

> 目标：为 leistd-net 三层交付面（framework / template-backend / template-frontend）设计一套端到端、可运行时切换的多语言方案。基于 Angular v21、PrimeNG v21、ASP.NET Core 10/11 与 Volo.ABP 的官方实践，以及业界全栈本地化的主流架构（2026-07-13 调研）。**本文是方案，不是最终代码或规范正文；框架/模板代码改造留待后续按本方案分批实施。**

---

## 一、结论先行

| 维度 | 选型 | 一句话理由 |
| --- | --- | --- |
| 前端加载方式 | **运行时库 Transloco** | JSON 词条运行时 fetch，用户即时切换、单包部署；比编译期 `$localize` 更适合管理后台 |
| 前端 UI 组件 | **PrimeNG `setTranslation` 联动** | `providePrimeNG({translation})` 初始化 + 运行时 `PrimeNG.setTranslation()` 跟随语言切换 |
| 后端消息本地化 | **错误码 → 资源映射（仿 ABP）** | 复用现有 `BusinessException.Code`（40000/42200…）作为本地化键，`throw` 处零 localizer 依赖 |
| 语言协商 | **`Accept-Language` 头 + Cookie 覆盖** | 前端 HTTP 拦截器带上活动语言，后端 `UseRequestLocalization` 统一解析 |
| 契约职责 | **静态 UI 文案在前端、系统/错误消息在后端** | 业界共识：静态 UI 用前端词条，跨端一致的系统消息由后端按 culture 产出 |

**核心洞察**：leistd 后端**已经**在 `ProblemDetails.Extensions["code"]` 输出稳定数字错误码，前端 `http-error-interceptor.ts` 也**已经**读 `error.error.code`。这个错误码天然就是本地化键——方案不发明新契约，只是把「码→文案」的映射从写死中文，改为按 culture 查表。

---

## 二、为什么这样选（调研依据）

### 2.1 前端：运行时库 vs Angular 内置 `$localize`

Angular v21 内置 i18n（`@angular/localize` + `i18n` 标记）是**编译期**方案：`ng build --localize` 为每个 locale 产出**独立 bundle**，部署在不同路径（`/en/`、`/zh/`），**运行时无法在同一页面即时切换语言**——只能整页跳转到另一 locale 的部署。官方文档明确其流程是「提取 → 翻译 → 合并构建 → 按 locale 部署多份」。

对于本模板的定位（带登录、菜单、表单的管理后台，用户期望点一下就切语言），编译期方案体验不达标。业界对「需要运行时切换」的一致建议是用运行时库（Transloco / ngx-translate）：JSON 词条在浏览器加载并热替换，无需重建。

**Transloco vs ngx-translate**：两者都是运行时、都支持即时切换。选 **Transloco** 因为它更契合本模板技术栈——TypeScript 强类型、懒加载词条、对 Angular signal/zoneless 更友好、维护活跃；ngx-translate 更老牌但曾长期停更。核心包 ~8kB，按需加 MessageFormat 插件。

> 取舍：若未来某业务项目是**面向公众、重 SEO 的内容站**，编译期 `$localize` 的静态多 locale 部署反而更优。本方案面向模板默认形态（管理后台）定运行时库；模板文档应保留这一 cross-link，让下游按项目性质取舍。

### 2.2 PrimeNG v21 的官方 i18n 模式

PrimeNG 组件内部文案（分页器、日历、确认框的「接受/拒绝」等）由 PrimeNG 自己的 translation 配置驱动，官方模式为两段：

- **初始化**：`providePrimeNG({ translation: { accept:'…', reject:'…', … } })`
- **运行时切换**：把语言词条里的 `primeng` 段喂给 `PrimeNG.setTranslation(res)`，官方示例正是用 ngx-translate/Transloco 的 `get('primeng')` 订阅驱动，**无需刷新页面**。

因此 PrimeNG 的语言必须与 Transloco 的活动语言**联动**，不能各管各的。词条文件里为 PrimeNG 单独保留一个 `primeng` 命名段。

### 2.3 后端：ABP 的「错误码 → 资源」本地化

ASP.NET Core 提供 `IStringLocalizer<T>` + `AddLocalization` + `UseRequestLocalization`（三个默认 culture provider：QueryString、Cookie、Accept-Language 头，按序命中）。.NET 11 更为 Minimal API / 校验新增 `AddValidation().AddValidationLocalization<T>()`，用 RESX 键定位校验消息。

ABP 在此之上的关键实践是**把消息本地化与异常抛出解耦**：

- 配置 `code-namespace → 本地化资源` 映射（`AbpExceptionLocalizationOptions.MapCodeNamespace`）；
- 抛异常时只带**错误码**，全局异常处理按码查当前 culture 的资源，产出本地化 `message`；
- 资源用**虚拟 JSON 文件**（`AddVirtualJson`）而非只有 `.resx`，键即错误码，值支持 `{UserName}` 占位；
- 客户端（Axios/HTTP client）自动带 `Accept-Language`，后端据此本地化 message、校验和标题。

这条路径与 leistd 现状高度契合：`BusinessException` 已有 `int Code`（如 400→40000、422→42200），`WithDetails`/`WithCode` 已在；只需把「码→文案」查表化，`throw new BadRequestException(...)` 的调用点无需注入 localizer。

### 2.4 业界全栈契约共识

主流全栈本地化架构的分工是：

- **静态 UI 文案**（菜单、按钮、标签）→ 前端词条文件，前端自治；
- **系统 / 错误 / 校验消息**→ 后端按 culture 产出，或后端只回稳定码、前端查本地词条；
- **用户生成内容**（业务数据里的多语言字段）→ 数据库多语言列/表，不在本方案范围（本方案只解决 UI 与消息 i18n，不含内容型 i18n）。
- 语言协商统一走 `Accept-Language`，并允许用户显式选择（Cookie/持久化）覆盖浏览器默认。

---

## 三、端到端架构

```
┌────────────────────────── 前端 (Angular 21 + PrimeNG 21) ──────────────────────────┐
│  语言选择器 ──设置──▶ TranslocoService.setActiveLang('en')                          │
│       │                        │                                                    │
│       │                        ├─▶ 模板文案   {{ 'menu.users' | transloco }}         │
│       │                        ├─▶ PrimeNG    PrimeNG.setTranslation(res.primeng)   │
│       │                        └─▶ 词条 fetch  public/i18n/{zh,en}.json (运行时)      │
│       └── 持久化到 Cookie/localStorage                                               │
│  HTTP 拦截器 ── 每个请求注入 ──▶ Accept-Language: <活动语言>                          │
└───────────────────────────────────────────┬─────────────────────────────────────┘
                                             │  HTTP  (Accept-Language)
┌───────────────────────────────────────────▼─────────────────────────────────────┐
│  后端 (ASP.NET Core 10)                                                            │
│  UseRequestLocalization  ──解析 culture──▶ CurrentUICulture                        │
│       (QueryString / Cookie / Accept-Language 三 provider)                         │
│                                                                                    │
│  throw new BadRequestException("...")   ──携带 Code=40000──┐                        │
│                                                            ▼                        │
│  BusinessExceptionHandler ── 按 Code 查资源(当前 culture) ──▶ 本地化 message + title │
│       └─▶ ProblemDetails { code, message(本地化), title(本地化), traceId, ... }     │
│  资源: 虚拟 JSON / RESX，键=错误码，值支持占位符                                     │
└────────────────────────────────────────────────────────────────────────────────┘
```

---

## 四、分层落地方案

### 4.1 Framework：新增 `Leistd.Localization` 组件 + 改造 `Leistd.Exception`

遵循框架命名与依赖方向约定（`*.Core` 平台无关、`components` 不依赖 `ddd-struct`、宿主显式组合）。

**新增能力组（`framework/components/localization/`）**

| 包 | 职责 | 依赖边界 |
| --- | --- | --- |
| `Leistd.Localization.Core` | 本地化资源抽象：资源定义、错误码↔资源映射选项、JSON 资源读取契约 | 平台无关，不引 ASP.NET Core |
| `Leistd.Localization.AspNetCore` | `AddLeistdLocalization` / `UseLeistdRequestLocalization`：封装 `AddLocalization` + culture provider 顺序 + 支持语言配置 | 引 `Microsoft.AspNetCore.App` FrameworkReference |

要点：
- 资源用**虚拟 JSON**（键=错误码/消息键，值含 `{占位符}`），对齐 ABP，比纯 `.resx` 更易维护、可随包分发默认中英资源。
- 组件**只提供能力**，不替宿主注册中间件——`Use*` 由模板 `Program.cs` 显式调用（符合「不替其他组件挂载」的边界）。

**改造 `Leistd.Exception.AspNetCore`**（公共行为变化，属兼容性评估项）

- `BusinessExceptionHandler` 注入 `IStringLocalizer`（或经 `Leistd.Localization.Core` 的抽象），按 `bizException.Code` 查当前 culture 的本地化 message；查不到回落到异常自带 `Message`（保持向后兼容）。
- `GetProblemTitle` 的英文标题改为按 culture 本地化（保留英文为默认回落）。
- 现在写死的中文兜底串（`"系统异常，请联系管理员"` 等）迁入资源文件，按码取。
- **契约不破坏**：`ProblemDetails` 字段结构（`code`/`message`/`title`/`traceId`/`detail`/`errors`）不变，只是 `message`/`title` 的**取值**随 culture 变化——属新增能力、非破坏性变更。

> 版本影响：新增包 + 异常组件公共运行时语义变化，按 `docs/framework/versioning.md` 至少 `feat:`（minor）。异常组件需评估「注入新依赖」对既有消费者的还原影响，并在组件文档同步。

### 4.2 Template-backend：装配框架能力

- `Program.cs`：`builder.Services.AddLeistdLocalization(...)` 声明支持语言（如 `zh-CN`、`en-US`）与默认语言；`app.UseLeistdRequestLocalization()` **置于最前**（在任何读 culture 的中间件之前，符合官方顺序要求）。
- 校验消息：采用 `AddDataAnnotationsLocalization`（MVC 控制器）或 .NET 11 的 `AddValidationLocalization`，把 DataAnnotations 消息键化。
- 随模板携带默认 `zh-CN` / `en-US` 资源；条件裁剪（如 identity/notifications）对应模块的消息键随功能块一起裁剪。

### 4.3 Template-frontend：Transloco + PrimeNG 联动

- 安装 **Transloco**；`app.config.ts`：`provideTransloco({ availableLangs:['zh','en'], defaultLang:'zh', ... })`。
- 词条放 `public/i18n/{zh,en}.json`（运行时 fetch）；每个文件含一个 `primeng` 段供组件库使用。
- **语言切换服务**：`translocoService.setActiveLang(lang)` 同时 `translocoService.selectTranslate('primeng').subscribe(r => primeng.setTranslation(r))`，让 PrimeNG 跟随。
- **HTTP 拦截器**：每个请求注入 `Accept-Language: <活动语言>`，使后端错误消息按同一 culture 返回。
- **收敛现有写死中文**：`http-error-interceptor.ts` 的 `CODE_MESSAGES`、`global-error-handler.ts` 的 `summary/detail` 改为查 Transloco 词条；HTTP 状态/错误码文案键与后端错误码对齐（同一码两端一致）。
- 数据格式（日期/货币/数字）用 Angular 内置 `DatePipe`/`CurrencyPipe`/`DecimalPipe`，按需 `registerLocaleData` + 绑定活动 `LOCALE_ID`（运行时数据格式本地化，与 Transloco 文案本地化正交）。

### 4.4 键命名与职责边界（跨端一致）

| 类别 | 归属 | 键形态 | 示例 |
| --- | --- | --- | --- |
| UI 静态文案 | 前端词条 | 语义命名空间 | `menu.users`、`btn.save` |
| 系统/错误消息 | 后端资源（权威）+ 前端可选覆盖 | **错误码** | `40000`、`42200` |
| PrimeNG 组件文案 | 前端词条 `primeng` 段 | PrimeNG 约定键 | `primeng.accept` |
| 校验消息 | 后端校验资源 | DataAnnotations 键 | `Validation:Required` |

原则：**同一事实一处权威**。错误消息以后端错误码资源为准；前端只在需要纯客户端兜底（如网络断开）时保留少量本地文案。

---

## 五、实施顺序与验证（后续分批）

> 本轮不写代码。以下为建议的执行批次，每批按所属交付面的 Skill 与验证入口收口。

1. **框架能力**（`developing-leistd-framework`）：建 `Leistd.Localization.*` → 改 `Leistd.Exception.AspNetCore` → 构建/测试/打包 `.tmp/local-feed` → 包消费检查 → 组件文档 `framework/docs/components/` 同步。
2. **模板后端**（`developing-leistd-template`）：先 `.tmp/local-feed` 打包新框架 → 模板经 NuGet 消费 → `Program.cs` 装配 → 生成项目还原/构建/测试 → 多 culture 请求验证错误消息随 `Accept-Language` 变化。
3. **模板前端**（`developing-leistd-template`）：Transloco + PrimeNG 联动 + 拦截器 → 前端构建 → 语言即时切换与错误码文案对齐验证。
4. **文档沉淀**：框架侧本地化组件契约进 `framework/docs/components/`；模板侧 i18n 工程规范进 `template/docs/standards/`（含前端运行时库选型与 `$localize` 取舍 cross-link）。

**残余风险 / 待定**：支持语言清单（起步 zh-CN + en-US？）、是否需要用户级语言持久化到后端账户、校验消息走 MVC `AddDataAnnotationsLocalization` 还是 .NET 11 `AddValidationLocalization`（取决于目标运行时与 Minimal API 采用度）——这些在批次 1 开工前确认。

---

## 六、参考

- Angular v21 i18n（编译期、按 locale 部署）：<https://v21.angular.dev/guide/i18n>、数据格式化 <https://v21.angular.dev/guide/i18n/format-data-locale>
- PrimeNG i18n（`providePrimeNG.translation` + 运行时 `setTranslation`）：<https://primeng.org/configuration>
- ASP.NET Core 本地化与 culture provider：<https://learn.microsoft.com/aspnet/core/fundamentals/localization>、<https://learn.microsoft.com/aspnet/core/fundamentals/localization/select-language-culture>
- .NET 11 校验消息本地化（`AddValidationLocalization`）：<https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-11>
- ABP 异常本地化（错误码→资源、虚拟 JSON、`Accept-Language` 客户端）：<https://abp.io/docs/latest/framework/fundamentals/exception-handling>、<https://abp.io/docs/latest/framework/fundamentals/localization>
- Angular i18n 库对比（Transloco vs ngx-translate，2026）：<https://intlayer.org/blog/i18n-technologies/frameworks/angular>、<https://phrase.com/blog/posts/best-libraries-for-angular-i18n/>
- 全栈本地化架构分工：<https://phrase.com/blog/posts/full-stack-javascript-i18n/>
