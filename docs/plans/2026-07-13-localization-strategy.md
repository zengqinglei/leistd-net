# 多语言（i18n）最佳实践方案

> 目标：为 leistd-net 三层交付面（framework / template-backend / template-frontend）设计一套端到端、可运行时切换的多语言方案。基于 Angular v21、PrimeNG v21、ASP.NET Core 10/11 与 Volo.ABP 的官方实践，以及业界全栈本地化的主流架构（2026-07-13 调研）。**本文是方案，不是最终代码或规范正文；框架/模板代码改造留待后续按本方案分批实施。**

---

## 一、结论先行

| 维度 | 选型 | 一句话理由 |
| --- | --- | --- |
| 前端加载方式 | **运行时库 Transloco** | JSON 词条运行时 fetch，用户即时切换、单包部署；比编译期 `$localize` 更适合管理后台 |
| 前端 UI 组件 | **PrimeNG `setTranslation` 联动** | `providePrimeNG({translation})` 初始化 + 运行时 `PrimeNG.setTranslation()` 跟随语言切换 |
| 后端消息本地化 | **文案键 → 资源映射（吸收 ABP 模型 2，用 .NET 原语实现）** | `throw` 处传**文案键 + 参数**（可通用如 `Error:NotFound`，也可专属如 `Order:StockInsufficient` + `WithData`），文案按 culture 查表出 `message`，throw 处零 localizer 依赖 |
| 前端分支判断 | **沿用现有数字 `code`** | 大部分错误共用默认码；需前端特判时 `.WithCode("46")` 指定；前端 `switch(code)`，不引入额外标识字段 |
| 语言协商 | **`Accept-Language` 头 + Cookie 覆盖** | 前端 HTTP 拦截器带上活动语言，后端 `UseRequestLocalization` 统一解析 |
| 契约职责 | **静态 UI 文案在前端、系统/错误消息在后端** | 业界共识：静态 UI 用前端词条，跨端一致的系统消息由后端按 culture 产出 |

**核心洞察**：后端要本地化，根因不是错误码粒度粗，而是当前 `throw new BadRequestException("余额不足")` 把**「人读的成品中文」直接写进了 throw 处**——没法按 culture 查表。修正只需一件事：**throw 处传"文案键"而非"成品中文"，文案落到资源文件按 culture 产出**。

**职责分离（本方案定论）**——三个需求各由一个既有机制承担，互不兼职：

| 职责 | 承担者 | throw 处 |
| --- | --- | --- |
| 多语言文案 | **文案键**（第一参数） | `"Error:NotFound"` / `"Order:StockInsufficient"` |
| 前端分支判断 | **数字 `code`** | 默认码；特殊才 `.WithCode("46")` |
| 补充说明（给人看） | **`Details`** | `.WithDetails(...)`，保持现状不动 |

> **设计演进说明**：本节结论经多轮收敛而来。早期草案曾设想①「复用数字 `Code` 兼作本地化键」（否决：`Code` 按 HTTP 类别粗分，同类 400 全是 40000，无法区分多条业务错误）、②「新增语义字符串键 `errorCode` + `data`/`reason` 字段」（否决：前端判断已有 `code` 承担，独立文案与前端判断是同一批少数错误、无需两套键，且额外字段增加前端认知与泄露面）。**最终定论**：`code` 保留本职（HTTP 状态 + 前端分支），本地化仅靠**文案键**，`Details` 保持"补充说明"本职，**不新增任何面向前端的字段**。详见 §4.1。

**默认语言与既有代码**：支持 `en` + `zh-CN`，**默认/回落语言 = 英语（`en`）**。模板参数 `--include-localization` 关闭时开发方式与现状零差异（§4.4a）；**启用时既有模板代码需同步改造**——后端 throw 的中文串→文案键、前端 `CODE_MESSAGES`/菜单/按钮等写死文案→Transloco 词条，且默认资源以 `en` 为准（`en.json` 必填，`zh-CN.json` 补充）。

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

### 2.3 微软 .NET 官方：本地化原语（底座）

微软官方只提供**本地化原语**，不涉及「异常怎么本地化」这一层：

- `IStringLocalizer<T>` / `IStringLocalizerFactory` + `AddLocalization`，资源用 `.resx`（或自定义源如 JSON）。
- 取值：`localizer["GreetingMessage"]`（键→文案）、`localizer["DinnerPriceFormat", date, price]`（键 + 位置参数 `{0}{1}`）。
- **键即默认值**：查不到翻译就返回键本身，因此键可直接写英文原文。
- `SharedResource` 标记类：把跨组件公共消息集中到一个资源类。
- `AddDataAnnotationsLocalization`：校验消息（`[Required]` 等）走同一套 `IStringLocalizer`，把 `ErrorMessage` 当键。
- culture 协商：`UseRequestLocalization` + 三 provider（QueryString / Cookie / Accept-Language，按序命中）。.NET 11 另为 Minimal API / 校验新增 `AddValidation().AddValidationLocalization<T>()`。

**关键点**：微软给的是「砖」（`IStringLocalizer`），"异常如何用它本地化"要上层自己搭——ABP 的模型正是补在这块砖之上的那一层。

### 2.4 ABP：异常本地化的两套并存模型

ABP 在 `IStringLocalizer` 之上提供**两套并存**模型，按场景分工：

**模型 1 —— `UserFriendlyException`（message 直传）**

```csharp
throw new UserFriendlyException("余额不足，请充值");
```

`Message` / `Details` 原样发给客户端，**不经本地化**。用于"就想直接给用户看这句话"的临时场景；要本地化需自己先注入 localizer 查好再抛。

**模型 2 —— 错误码 → 资源（ABP 推荐，消息与抛出解耦）**

```csharp
// throw 处：只声明语义键 + 参数，无任何人读文案
throw new BusinessException("Shop:BalanceInsufficient")
    .WithData("Balance", 50)
    .WithData("Required", 100);
```

```json
// en.json                                          // zh.json
{ "Shop:BalanceInsufficient":                       { "Shop:BalanceInsufficient":
    "Insufficient balance: {Balance}, ..." }            "余额不足：{Balance}，需 {Required}。" }
```

```csharp
options.MapCodeNamespace("Shop", typeof(ShopResource));   // 配置一次映射
```

模型 2 的四个设计要点（本方案**部分吸收**，差异见下）：

1. **文案键是字符串**（`Shop:BalanceInsufficient`），自解释、按模块组织——本方案吸收此点作**文案键**（第一参数），资源按它查表。
2. **throw 处零 localizer 依赖**——静态上下文、领域层深处都能抛。本方案完全吸收。
3. **参数用 `.WithData(key, value)` 注入**，资源里用 `{key}` 具名占位。本方案完全吸收。
4. **回落明确**：键查不到 → 发默认消息/键本身。本方案吸收（用 .NET `IStringLocalizer` 原生"键即默认值"，回落到键或异常 Message）。

> **本方案与 ABP 的差异**：ABP 用这个字符串键**同时**承担"本地化 + 客户端识别"，并配 `MapCodeNamespace` 做码空间→资源映射。本方案不这么做——**文案键只用于后端资源查表、不返给前端**；前端识别/分支沿用**现有数字 `code`**（`WithCode` 特殊化），不引入返给前端的字符串键。即：借 ABP 的"throw 传键不传文案 + WithData 参数"，但不借它的"字符串键兼做客户端识别"。见 §4.1、§4.4。

### 2.5 业界全栈契约共识

主流全栈本地化架构的分工是：

- **静态 UI 文案**（菜单、按钮、标签）→ 前端词条文件，前端自治；
- **系统 / 错误 / 校验消息**→ 后端按 culture 产出，或后端只回稳定码、前端查本地词条；
- **用户生成内容**（业务数据里的多语言字段）→ 数据库多语言列/表，不在本方案范围（本方案只解决 UI 与消息 i18n，不含内容型 i18n）。
- 语言协商统一走 `Accept-Language`，并允许用户显式选择（Cookie/持久化）覆盖浏览器默认。

### 2.6 后端资源底层：为什么自写 JSON localizer

核实三个框架/方案的实际做法后定选型：

| 方案 | 实际做法 | 对 leistd 的适配 |
| --- | --- | --- |
| **.NET 内置** `ResourceManagerStringLocalizer` | 只认 **RESX**（XML）；JSON 需自实现 `IStringLocalizer` | ✗ 与方案"选 JSON"相左；RESX 手改/review 不友好 |
| **ABP** | **完全自研**：JSON（`culture`+`texts`）+ 标记类资源 + 虚拟文件系统嵌入 + 自有 `IStringLocalizer` 栈（支持继承/覆盖） | 思路对，但**整套 VFS + 模块系统过重**，不照搬 |
| **第三方** `My.Extensions.Localization.Json` | 按类名/目录定位 JSON（同 RESX 规则） | 单人维护的非微软包 = 供应链点；覆盖能力弱 |

**定论：自写轻量 JSON localizer**——取 ABP 的思路（JSON `culture`+`texts`、嵌入分发、按键查表 + culture 回落），坐在**微软原生 `IStringLocalizer` 抽象**上（实现 `IStringLocalizerFactory` / `IStringLocalizer` 两个标准接口），**零第三方依赖**、约 150 行、可完全 review。消费者面对的仍是标准 `IStringLocalizer`，无 leistd 私有抽象泄露；符合框架"避免无收益新抽象、优先原生 API、可控可打包"的原则。

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
│  throw new BadRequestException("Order:StockInsufficient")                          │
│        .WithData("Sku", "A1").WithCode("46")  ── 文案键(+参数)(+特殊码) ──┐          │
│                                                                          ▼          │
│  BusinessExceptionHandler ── 按 文案键 + Data 查资源(culture) ──▶ 本地化 message     │
│       └─▶ ProblemDetails { code(HTTP+可选细分), message(本地化), title, traceId }   │
│  资源: 虚拟 JSON，键=文案键 Order:StockInsufficient，值含 {Sku} 具名占位             │
│  职责分离: 文案键→多语言; 数字 code→HTTP+前端分支; Details→补充说明。无新增前端字段  │
└────────────────────────────────────────────────────────────────────────────────┘
```

---

## 四、分层落地方案

### 4.1 Framework：新增 `Leistd.Localization` 组件 + 改造 `Leistd.Exception`

遵循框架命名与依赖方向约定（`*.Core` 平台无关、`components` 不依赖 `ddd-struct`、宿主显式组合）。

**新增能力组（`framework/components/localization/`）**

| 包 | 职责 | 依赖边界 |
| --- | --- | --- |
| `Leistd.Localization.Core` | **自写轻量 JSON localizer**：`JsonStringLocalizerFactory : IStringLocalizerFactory` + `JsonStringLocalizer : IStringLocalizer`，读嵌入 JSON、按键查表、culture 回落 + 内存缓存 | 平台无关，仅依赖 `Microsoft.Extensions.Localization.Abstractions`，不引 ASP.NET Core、零第三方 |
| `Leistd.Localization.AspNetCore` | `AddLeistdLocalization`（注册上面的 factory + `AddLocalization`）/ `UseLeistdRequestLocalization`（culture provider 顺序 + 支持语言配置） | 引 `Microsoft.AspNetCore.App` FrameworkReference |

**资源底层选型 —— 自写 JSON localizer（见 §2.6 论证）**：

- 资源用 **JSON**（ABP 式 `culture` + `texts`，键=文案键，值含 `{具名占位符}`），比 `.resx`（XML、依赖工具编辑、review 不友好）更易维护、可随包分发默认中英。
- **不用** .NET 内置 `ResourceManagerStringLocalizer`（只支持 RESX）、**不引**第三方 JSON 包（单人维护的供应链点）、**不照搬** ABP 整套虚拟文件系统（过重）——只自写约 150 行，坐在**微软原生 `IStringLocalizer` 抽象**上，对消费者是标准 .NET 契约，无 leistd 私有抽象泄露。
- 默认 `en`/`zh-CN` 资源以 `EmbeddedResource` 随包分发（**默认语言 en**，即回落语言）；业务项目放自己的 `{culture}.json` 即可**覆盖/追加**。
- 组件**只提供能力**，不替宿主注册中间件——`Use*` 由模板 `Program.cs` 显式调用（符合「不替其他组件挂载」的边界）。

**改造 `Leistd.Exception`（源码兼容，不破坏现有签名）**

核心洞察（呼应 §4.4a「本地化开关」）：`BusinessException` 第一参数**保持 `string` 类型不变**，只是它的**运行时解释**从"成品消息"升级为"**先当键查表、查不到就原样用**"（.NET `IStringLocalizer` 原生"键即默认值"语义）。于是：

- **未启用本地化**（没注册 localizer）：`throw new BadRequestException("余额不足")` → 查不到键"余额不足" → 原样返回 = **与现状完全一致**。满足「关闭时开发方式无区别」。
- **启用本地化**：`throw new BadRequestException("Order:StockInsufficient").WithData("Sku", sku)` → 命中键 → 按 culture 产出文案；漏配键 → 返回键本身（可见的缺失信号）。

具体改动：

- `BusinessException` **新增 `Data` / `WithData(key, value)` 参数机制**（供资源里 `{key}` 具名占位）。**第一参数签名不变**（仍是 `string message`），语义变为"消息或文案键"。
- 现有 `int Code`（`WithCode`）**保持不变**：既定 HTTP 状态码，也是前端分支判断的依据——大部分错误共用默认码，需前端特判时 `.WithCode("46")`。**不新增 `errorCode` 字段**。
- 现有 `Details`（`WithDetails`）**保持现状**：仅作"给人看的补充说明"，**不承担前端判断**（前端判断走 `code`）。
- `BusinessExceptionHandler` 注入**可选** `IStringLocalizer`（经 `Leistd.Localization.Core` 抽象）：localizer 缺省时按现状直出 message；存在时按第一参数（键）+ `Data` 查当前 culture 文案，填进 `ProblemDetails.detail` / `message`。
- `GetProblemTitle` 的英文标题在启用本地化时按 culture 产出（默认英文回落）。
- 模板**启用本地化时**才把写死中文串迁入资源、throw 处改传键；**关闭时** throw 处保留中文原文（无迁移动作）。

**`ProblemDetails` 结构变化（对前端零新增字段）**：

| 字段 | 变化 |
| --- | --- |
| `message` / `detail` / `title` / `errors` | 启用本地化时**取值随 culture 变**；关闭时同现状（结构始终不变） |
| `code` | **不变**（仍表 HTTP 类别 + 可选细分码，前端仍按它分支） |
| `traceId` | 不变 |
| ~~`errorCode` / `data` / `reason`~~ | **不新增**（前端判断靠 `code`；`data` 是渲染前原料，不外泄） |

> 版本影响：新增 `Leistd.Localization.*` 包 + 异常组件**新增能力（`WithData`、可选 localizer）但不改现有签名**，按 `docs/framework/versioning.md` 为 `feat:`（minor），**非破坏性**。原"major"判断因"第一参数保持 string、旧写法照常工作"而下调。组件文档同步；模板作为首个消费者按开关分两条路生成（见 §4.4a）。

### 4.2 Template-backend：装配框架能力

- `Program.cs`：`builder.Services.AddLeistdLocalization(...)` 声明支持语言（`en`、`zh-CN`）与**默认语言 `en`（英语）**；`app.UseLeistdRequestLocalization()` **置于最前**（在任何读 culture 的中间件之前，符合官方顺序要求）。
- 校验消息：采用 `AddDataAnnotationsLocalization`（MVC 控制器）或 .NET 11 的 `AddValidationLocalization`，把 DataAnnotations 消息键化。
- 随模板携带默认 `zh-CN` / `en-US` 资源；条件裁剪（如 identity/notifications）对应模块的消息键随功能块一起裁剪。

### 4.3 Template-frontend：Transloco + PrimeNG 联动（基于实测现状）

现状事实（探查确认）：Angular 21 zoneless、PrimeNG 21（Aura 预设，`.dark` 暗色）、Tailwind v4 + **`tailwindcss-primeui` 已装**；静态资源用 **`public/`**（无 `src/assets/`）；`ThemeService` 在 **`core/services/theme-service.ts`**（signal + localStorage + `isPlatformBrowser` 守卫）；header 在 `layout/components/default-header/`，主题切换按钮在 "Right Actions" 区；HTTP 拦截器 `withInterceptors([urlFormatInterceptor, httpErrorInterceptor])`。

- **装 Transloco**（Angular 21 → `@jsverse/transloco` v7+，standalone `provideTransloco` API；当前未装）。`app.config.ts` 顶层加 `provideTransloco({ config:{ availableLangs:['en','zh-CN'], defaultLang:'en', fallbackLang:'en', ... }, loader })`——**默认英语**（依赖已有的 `provideHttpClient`，无与 PrimeNG 的顺序约束）。
- 词条放 **`public/i18n/{zh-CN,en}.json`**（运行时 fetch `/i18n/{lang}.json`）；每个文件含一个 `primeng` 段供组件库使用。
- **`LanguageService`（`core/services/language.service.ts`，镜像 `ThemeService` 形态）**：signal 存活动语言、`effect` 持久化到 localStorage、`isPlatformBrowser` 守卫（SSR 安全）；`setActiveLang(lang)` 内 `transloco.setActiveLang(lang)` 并 `transloco.selectTranslate('primeng').subscribe(r => primeng.setTranslation(r))` 让 PrimeNG 跟随。
- **语言选择器**：用 **PrimeNG `p-select`**（官方语言切换模式，支持旗帜/名称模板）放进 `default-header` 的 "Right Actions" 区、主题切换按钮旁；`OnPush` + `inject()`（符合前端规范 §4.1 组件库优先、§4.4/§5.1）。
- **`accept-language.interceptor.ts`（新，functional）**：注入 `Accept-Language: <活动语言>`，**置于拦截器数组首位**（在 `urlFormatInterceptor` 前，确保 header 先挂上）。使后端错误消息按同一 culture 返回。
- **收敛现有写死中文**：`http-error-interceptor.ts` 的 `CODE_MESSAGES`、`global-error-handler.ts` 的 `summary/detail`——**业务错误提示优先直接显示后端已本地化的 `message`**（后端已按 `Accept-Language` 产出），前端 map 仅保留纯客户端兜底（网络断开/后端不可达，查 Transloco 词条）。需按错误类型做差异化 UI 行为时，前端读 **`code`** 分支（与现有 `error.error.code` 读取一致）。
- 数据格式（日期/货币/数字）用 Angular 内置 `DatePipe`/`CurrencyPipe`/`DecimalPipe`，按需 `registerLocaleData` + 绑定活动 `LOCALE_ID`（运行时数据格式本地化，与 Transloco 文案本地化正交）。

### 4.4 键命名与职责边界（跨端一致）

| 类别 | 归属 | 键形态 | 示例 |
| --- | --- | --- | --- |
| UI 静态文案 | 前端词条（权威） | 语义命名空间 | `menu.users`、`btn.save` |
| 系统/错误消息文案 | 后端资源（权威） | **文案键** | `Order:StockInsufficient` |
| 错误的前端分支标识 | 后端 `code`（前端读取） | 数字码 | `40000`、`40046` |
| PrimeNG 组件文案 | 前端词条 `primeng` 段 | PrimeNG 约定键 | `primeng.accept` |
| 校验消息 | 后端校验资源 | DataAnnotations 键 | `Validation:Required` |

**职责分离（本方案定论，非"前后端共用同一键"）**：

- **错误文案**：权威在**后端**资源（按文案键 + culture 产出），前端**直接显示后端 `message`**，不在前端重复维护一份业务错误词条。
- **前端分支**：靠**后端 `code`**（前端可维护一份 `code` 常量枚举做 `switch`，但那是"码→行为"映射，不是"码→文案"）。
- **文案键不外泄给前端**：文案键是后端资源查表用的内部键，不进 `ProblemDetails`；前端拿到的是已本地化的 `message` + 用于分支的 `code`。

```
后端 throw 文案键  ──查表──▶  后端资源(zh/en)  ──产出──▶  ProblemDetails.message(已本地化)
                                                          ProblemDetails.code(前端分支)
                                                                │
前端 ◀── 显示 message；必要时按 code 分支 ──────────────────────┘
```

原则：**同一事实一处权威**。业务错误文案的唯一权威是后端资源；前端只维护自己的 UI 文案与纯客户端兜底（网络断开、后端不可达）。

### 4.4a 本地化开关：模板参数 `--include-localization`

新增模板参数（沿用现有 `IncludeIdentity` 等的 `symbols` + `sources.modifiers` + 文件内 `#if` 机制）：

```jsonc
// template.json symbols
"IncludeLocalization": {
  "type": "parameter", "datatype": "bool",
  "description": "是否包含多语言（i18n）支持（前端运行时切换 + 后端按 culture 本地化）",
  "defaultValue": "false"
}
```

**关闭时（默认）= 与现状零差异**——这是能做到的前提正是 §4.1 的"第一参数仍是 string、键即默认值"设计：

| 面 | `IncludeLocalization=false`（默认） | `IncludeLocalization=true` |
| --- | --- | --- |
| 后端 throw | `throw new BadRequestException("余额不足")`，原样返回 | `throw ...("Order:StockInsufficient").WithData(..)`，按 culture 查表 |
| 后端 `Program.cs` | 不注册 localizer / 不 `UseRequestLocalization` | `#if` 块内 `AddLeistdLocalization` + `UseLeistdRequestLocalization` |
| 后端资源文件 | 不生成 `Resources/*.json` | 生成 zh-CN/en-US |
| 前端 | 无 Transloco、无语言切换、文案写死（现状） | Transloco + 语言选择器 + 拦截器 |
| 框架包引用 | `Leistd.Exception.*` 照旧（不引 `Leistd.Localization`） | `#if` 引 `Leistd.Localization.AspNetCore` |

裁剪落点：

- **文件级排除**（`sources.modifiers`，`condition: "!IncludeLocalization"`）：`frontend/public/i18n/**`、`frontend/src/app/core/i18n/**`、`accept-language.interceptor.ts`、`backend/.../Resources/**`、语言切换 widget。
- **文件内 `#if (IncludeLocalization)`**：`Program.cs` 的注册行、`app.config.ts` 的 `provideTransloco`、`.csproj` 的包引用、header 里的语言选择器标签、`http-error-interceptor.ts` 里"查词条 vs 直显"的分支。
- **关键约束**：框架侧 `Leistd.Exception` 的改造是**无条件**的（`WithData` + 可选 localizer 对关闭方零影响，因 localizer 缺省即现状行为）；**只有模板生成层**按开关裁剪。这样框架只维护一份代码，模板按需组合。

> 验证要求：`scripts/test-template-matrix.ps1` 增加 `IncludeLocalization` 的 on/off 两个场景，各自生成后端还原/构建、前端构建通过，且**关闭场景与新增前的产物在 i18n 相关文件上无残留标记**。

---

## 五、多语言开发端到端步骤

分两类：**一次性基建**（框架/模板搭好，只做一次）与**日常开发**（此后每加一条需多语言的错误/文案时走的动作）。

### 5.1 一次性基建（框架 + 模板搭建者做一次）

| # | 层 | 步骤 |
| --- | --- | --- |
| 1 | 框架 | 建 `Leistd.Localization.Core`：写 `JsonStringLocalizer` / `JsonStringLocalizerFactory`（读嵌入 JSON、culture 回落、缓存） |
| 2 | 框架 | 建 `Leistd.Localization.AspNetCore`：`AddLeistdLocalization` / `UseLeistdRequestLocalization` |
| 3 | 框架 | 改 `Leistd.Exception`：`BusinessException` 加 `Data`/`WithData`（**第一参数签名不变**）；handler 注入**可选** `IStringLocalizer`（缺省=现状、存在=按键+culture）；`Code`/`Details` 不动 |
| 4 | 框架 | 写默认资源 `Resources/{en,zh-CN}.json`（框架级通用键 `Error:*`，**默认/回落 en**），随包 `EmbeddedResource` |
| 5 | 模板参数 | `template.json` 加 `IncludeLocalization`（默认 `false`）+ `modifiers` 文件级裁剪 |
| 6 | 模板后端 | `Program.cs` `#if(IncludeLocalization)`：`AddLeistdLocalization(默认 en，支持 en+zh-CN)` + `UseLeistdRequestLocalization()`（置于读 culture 的中间件之前）+ `AddDataAnnotationsLocalization`；生成默认资源 |
| 7 | 模板前端 | `#if` 装 `@jsverse/transloco`（`provideTransloco`）；建 `public/i18n/{zh-CN,en}.json`（含 `primeng` 段）；`LanguageService` 联动 `PrimeNG.setTranslation` |
| 8 | 模板前端 | `#if` Accept-Language 拦截器（置首）；`p-select` 语言选择器入 `default-header`；错误提示优先显示后端 `message`、按 `code` 分支 |

> 关闭 `IncludeLocalization` 时步骤 5–8 的 `#if`/`[gate]` 内容不生成，开发方式与现状零差异（见 §4.4a）。

### 5.2 日常开发：新增一条「需多语言的业务错误」

以"下单时库存不足"为例，端到端六步：

```
① 后端 throw       throw new BadRequestException("Order:StockInsufficient")
   （传文案键，不写中文）      .WithData("Sku", sku);
                              // 需前端特判时再 .WithCode("46")

② 后端补资源       en.json    → "Order:StockInsufficient": "Out of stock: {Sku}"  （默认语言，必填）
   （两种语言各一条）  zh-CN.json → "Order:StockInsufficient": "库存不足：{Sku}"

③ 请求带 culture   前端拦截器按活动语言注入 Accept-Language（默认 en；用户切到 zh-CN 则 zh-CN）

④ 后端产出         handler 按 Accept-Language 查资源（默认 en）→ message="Out of stock: A1"
   （自动，无需编码）  → ProblemDetails { code:400, message:"Out of stock: A1", traceId }

⑤ 前端显示         拦截器直接 toast(err.error.message)   // 已是本地化好的文案
   （默认路径，零额外代码）

⑥ 前端特判(可选)   若②里加了 .WithCode，前端 switch(err.error.code){ case 40046: ... }
```

**关键**：日常只做 ①②——**throw 传键 + 资源补两条（en 必填、zh-CN 补充）**。③④⑤是基建就绪后的自动行为，⑥仅在需要特殊 UI 行为时才做。纯 UI 文案（菜单/按钮）则只在前端 `{en,zh-CN}.json` 加键、模板用 `{{ 'key' | transloco }}`，不经后端。

### 5.3 日常开发：切换语言（终端用户 / 前端）

**默认语言 = 英语（en）**。首次进入应用即英文；用户切到中文：

```
用户点语言选择器 → languageService.setActiveLang('zh-CN')   // 默认为 'en'
                    ├─ UI 文案即时重渲染（{{ | transloco }}）
                    ├─ PrimeNG.setTranslation(zh-CN.primeng) 组件文案跟随
                    ├─ 持久化到 localStorage（下次进入沿用）
                    └─ 此后请求 Accept-Language: zh-CN → 后端错误消息也转中文
```
全程**不刷新页面、不重新构建**（运行时库特性）。

---

## 六、改动目录树

`[新]` 新增、`[改]` 修改、`[删]` 删除写死中文。

```
leistd-net/
├── framework/
│   ├── components/
│   │   ├── localization/                                    [新] 新能力组
│   │   │   ├── Leistd.Localization.Core/
│   │   │   │   ├── Leistd.Localization.Core.csproj          [新] 仅引 M.E.Localization.Abstractions
│   │   │   │   ├── Json/
│   │   │   │   │   ├── JsonStringLocalizer.cs               [新] IStringLocalizer 实现
│   │   │   │   │   ├── JsonStringLocalizerFactory.cs        [新] IStringLocalizerFactory 实现
│   │   │   │   │   └── JsonLocalizationResourceReader.cs    [新] 读嵌入 JSON + culture 回落 + 缓存
│   │   │   │   └── Options/
│   │   │   │       └── LeistdLocalizationOptions.cs         [新] 资源程序集/路径/默认语言
│   │   │   └── Leistd.Localization.AspNetCore/
│   │   │       ├── Leistd.Localization.AspNetCore.csproj    [新] FrameworkReference AspNetCore.App
│   │   │       └── DependencyInjection.cs                   [新] Add/UseLeistdLocalization
│   │   └── exception/
│   │       ├── Leistd.Exception.Core/
│   │       │   ├── BusinessException.cs                     [改] 加 Data/WithData（第一参数仍是 string，语义=消息或键）
│   │       │   ├── BadRequestException.cs 等 8 个           [不改] 签名不变（第一参数语义升级，源码兼容）
│   │       │   └── UnprocessableEntityException.cs          [改] errors 支持键化（签名不变）
│   │       └── Leistd.Exception.AspNetCore/
│   │           ├── Leistd.Exception.AspNetCore.csproj       [改] 引 Leistd.Localization.Core
│   │           └── Handlers/
│   │               └── BusinessExceptionHandler.cs          [改] 注入可选 IStringLocalizer；缺省=现状、存在=按键+culture
│   ├── Directory.Packages.props                             [改] 登记 M.E.Localization.Abstractions 版本
│   ├── Leistd.Framework.slnx                                [改] 加入两个新项目
│   └── docs/components/
│       └── localization.md                                  [新] 组件使用文档（随包分发）
│
├── template/
│   ├── .template.config/template.json                      [改] 加 IncludeLocalization 参数 + modifiers 排除
│   ├── backend/src/CompanyName.ProjectName.Api/
│   │   ├── Program.cs                                       [改] #if(IncludeLocalization) Add/UseLeistdLocalization + DataAnnotations
│   │   ├── CompanyName.ProjectName.Api.csproj              [改] #if 引 Leistd.Localization.AspNetCore
│   │   └── Resources/                                       [新][gate] 仅 IncludeLocalization 生成
│   │       ├── en.json                                     [新][gate] 默认业务键资源（默认语言）
│   │       └── zh-CN.json                                   [新][gate]
│   ├── frontend/
│   │   ├── package.json                                     [改] #if + @jsverse/transloco（tailwindcss-primeui 已装，无需加）
│   │   ├── src/
│   │   │   ├── styles.css                                   [不改] Tailwind v4 + primeui 插件已就位
│   │   │   └── app/
│   │   │       ├── app.config.ts                            [改] #if provideTransloco + Accept-Language 拦截器置首
│   │   │       ├── core/
│   │   │       │   ├── services/
│   │   │       │   │   └── language.service.ts              [新][gate] 镜像 theme-service：signal+localStorage+PrimeNG 联动
│   │   │       │   ├── i18n/transloco-loader.ts             [新][gate] 运行时 fetch /i18n/{lang}.json
│   │   │       │   ├── interceptors/
│   │   │       │   │   ├── accept-language.interceptor.ts   [新][gate] 注入 Accept-Language（数组首位）
│   │   │       │   │   └── http-error-interceptor.ts        [改] #if 分支：查词条 vs 直显后端 message
│   │   │       │   └── handlers/global-error-handler.ts     [改] #if 分支：transloco vs 写死中文
│   │   │       └── layout/components/default-header/
│   │   │           ├── default-header.html                  [改] #if 加 p-select 语言选择器（主题按钮旁）
│   │   │           └── default-header.ts                    [改] #if inject LanguageService
│   │   └── public/i18n/                                     [新][gate] 前端 UI 词条（public 约定，无 src/assets）
│   │       ├── en.json                                     [新][gate] 含 primeng 段（默认语言）
│   │       └── zh-CN.json                                   [新][gate] 含 primeng 段
│   └── docs/standards/
│       ├── api.md                                           [改] 异常响应=本地化后 message（已提交部分）
│       └── i18n.md                                          [新] 模板 i18n 工程规范
│
├── scripts/test-template-matrix.ps1                        [改] 加 IncludeLocalization on/off 两场景
│
└── docs/plans/
    └── 2026-07-13-localization-strategy.md                  [本文]
```

> 图例：`[gate]` = 仅 `IncludeLocalization=true` 生成/保留（文件级 `modifiers` 排除）；`#if(IncludeLocalization)` = 文件内条件块，关闭时该段不生成。框架侧（上半部）**无 gate**——无条件改造，对关闭方零影响。

---

## 七、实施顺序与验证（后续分批）

> 以下为执行批次，每批按所属交付面的 Skill 与验证入口收口。

1. **框架能力**（`developing-leistd-framework`）：建 `Leistd.Localization.*` → 源码兼容改造 `Leistd.Exception`（`BusinessException` 加 `Data`/`WithData`、handler 接**可选** localizer；第一参数签名不变、`Code`/`Details` 保持本职）→ 构建/测试/打包 `.tmp/local-feed` → 包消费检查 → 组件文档 `framework/docs/components/localization.md`。**验证含"未启用 localizer 时行为 = 现状"的回归。**
2. **模板参数与后端**（`developing-leistd-template`）：加 `IncludeLocalization` 参数 + `modifiers` 裁剪 → 先 `.tmp/local-feed` 打包新框架 → 模板经 NuGet 消费 → `Program.cs`/`.csproj` 加 `#if` 装配 + 默认资源 → **on/off 两场景**各自生成、还原、构建、测试 → on 场景多 culture 请求验证错误消息随 `Accept-Language` 变化；**off 场景验证与现状无差异**。
3. **模板前端**（`developing-leistd-template`）：`@jsverse/transloco` + `LanguageService`（镜像 theme-service）+ `p-select` 语言选择器 + Accept-Language 拦截器（置首）→ 前端构建 → 语言即时切换、PrimeNG 联动、错误文案对齐验证。
4. **文档沉淀**：框架侧本地化组件契约进 `framework/docs/components/localization.md`；模板侧 i18n 工程规范进 `template/docs/standards/i18n.md`（含前端运行时库选型、`$localize` 取舍、`IncludeLocalization` 开关说明）；顺带修正 `coding-frontend.md` §2 目录树里失真的 `assets/i18n`（实际用 `public/i18n`）。

**已定**：支持语言 `en` + `zh-CN`，**默认语言 = `en`（英语）**；启用本地化时**现有模板代码同步改造**（throw 中文→键、前端 `CODE_MESSAGES`/菜单/按钮文案→词条），不只是加基建。

**残余风险 / 待定**：文案键命名规范（`模块:实体动作`，需成文约束避免各模块乱起）、通用键是否从 HTTP status 自动推导（让通用错误 throw 处连键都不必传）、用户级语言是否持久化到后端账户、校验消息走 MVC `AddDataAnnotationsLocalization` 还是 .NET 11 `AddValidationLocalization`——这些在实施中确认。

---

## 八、参考

- Angular v21 i18n（编译期、按 locale 部署）：<https://v21.angular.dev/guide/i18n>、数据格式化 <https://v21.angular.dev/guide/i18n/format-data-locale>
- PrimeNG i18n（`providePrimeNG.translation` + 运行时 `setTranslation`）：<https://primeng.org/configuration>
- .NET 本地化原语（`IStringLocalizer`、`SharedResource`、位置参数）：<https://learn.microsoft.com/dotnet/core/extensions/localization>
- ASP.NET Core 本地化与 culture provider：<https://learn.microsoft.com/aspnet/core/fundamentals/localization>、<https://learn.microsoft.com/aspnet/core/fundamentals/localization/select-language-culture>、内容可本地化化（`AddDataAnnotationsLocalization`）<https://learn.microsoft.com/aspnet/core/fundamentals/localization/make-content-localizable>
- .NET 11 校验消息本地化（`AddValidationLocalization`）：<https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-11>
- ABP 异常本地化（两模型：`UserFriendlyException` 直传 / 错误码→资源 `MapCodeNamespace`、`WithData` 参数、虚拟 JSON、`Accept-Language` 客户端）：<https://abp.io/docs/latest/framework/fundamentals/exception-handling>、<https://abp.io/docs/latest/framework/fundamentals/localization>
- Angular i18n 库对比（Transloco vs ngx-translate，2026）：<https://intlayer.org/blog/i18n-technologies/frameworks/angular>、<https://phrase.com/blog/posts/best-libraries-for-angular-i18n/>
- 全栈本地化架构分工：<https://phrase.com/blog/posts/full-stack-javascript-i18n/>
