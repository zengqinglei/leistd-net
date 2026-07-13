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
| `Leistd.Localization.Core` | 本地化资源抽象：资源定义、文案键↔资源映射选项、JSON 资源读取契约 | 平台无关，不引 ASP.NET Core |
| `Leistd.Localization.AspNetCore` | `AddLeistdLocalization` / `UseLeistdRequestLocalization`：封装 `AddLocalization` + culture provider 顺序 + 支持语言配置 | 引 `Microsoft.AspNetCore.App` FrameworkReference |

要点：
- 资源用**虚拟 JSON**（键=文案键，值含 `{具名占位符}`），吸收 ABP 做法，比纯 `.resx` 更易维护、可随包分发默认中英资源。
- 组件**只提供能力**，不替宿主注册中间件——`Use*` 由模板 `Program.cs` 显式调用（符合「不替其他组件挂载」的边界）。

**改造 `Leistd.Exception`（破坏性，本方案已获准可破坏兼容性）**

核心：把 throw 处的**「成品中文」换成「文案键」**，让消息能按 culture 查表；`code` 与 `Details` 各守本职、不兼职本地化。

- `BusinessException` **新增 `Data` / `WithData(key, value)` 参数机制**（供资源里 `{key}` 具名占位）。第一构造参数由"成品消息"改为"**文案键**"（可通用如 `Error:NotFound`，可专属如 `Order:StockInsufficient`）。
- 现有 `int Code`（`WithCode`）**保持不变**：既定 HTTP 状态码，也是前端分支判断的依据——大部分错误共用默认码，需前端特判时 `.WithCode("46")`。**不新增 `errorCode` 字段**。
- 现有 `Details`（`WithDetails`）**保持现状**：仅作"给人看的补充说明"，**不承担前端判断**（前端判断走 `code`）。
- `BusinessExceptionHandler` 注入 `IStringLocalizer`（经 `Leistd.Localization.Core` 抽象），按**文案键** + `Data` 查当前 culture 文案，填进 `ProblemDetails.detail` / `message`。
- 回落：文案键查不到 → 用键本身/默认消息（.NET `IStringLocalizer` 原生"键即默认值"语义），避免整条丢消息。
- `GetProblemTitle` 的英文标题改为按 culture 本地化（保留英文为默认回落）。
- 现在写死的中文串（`throw new BadRequestException("余额不足")`、兜底串 `"系统异常，请联系管理员"` 等）**全量迁入资源文件**，throw 处改为传文案键。

**`ProblemDetails` 结构变化（对前端几乎零新增）**：

| 字段 | 变化 |
| --- | --- |
| `message` / `detail` / `title` / `errors` | **取值随 culture 变**（结构不变） |
| `code` | **不变**（仍表 HTTP 类别 + 可选细分码，前端仍按它分支） |
| `traceId` | 不变 |
| ~~`errorCode` / `data` / `reason`~~ | **不新增**（前端判断靠 `code`，无需额外标识；`data` 是渲染前原料，不外泄） |

**目标形态对照**（即"破坏性"的具体含义，集中在 throw 侧）：

| 项 | 现在 | 目标 |
| --- | --- | --- |
| throw 第一参数 | 成品中文 `"余额不足"` | 文案键 `"Order:StockInsufficient"` |
| 动态参数 | `$"用户名 '{x}' 已存在"` | `.WithData("x", x)` + 资源 `{x}` |
| 消息存放 | 散在代码字符串插值 | 集中资源文件 zh/en |
| 校验消息 | DataAnnotation 写死中文 | `ErrorMessage` 键化 |
| 前端分支 | 读 `code`（无变化） | 读 `code`（无变化） |
| 补充说明 | `WithDetails`（无变化） | `WithDetails`（无变化） |

> 版本影响：新增包 + 异常组件**破坏性公共 API 变化**（throw 第一参数语义从"消息"变"键"），按 `docs/framework/versioning.md` 为 `BREAKING CHANGE`（major）。响应结构本身对前端不新增字段，破坏性主要落在后端 throw 侧与"断言中文文案"的旧测试。需在组件文档与迁移说明同步；模板作为首个消费者同批迁移。

### 4.2 Template-backend：装配框架能力

- `Program.cs`：`builder.Services.AddLeistdLocalization(...)` 声明支持语言（如 `zh-CN`、`en-US`）与默认语言；`app.UseLeistdRequestLocalization()` **置于最前**（在任何读 culture 的中间件之前，符合官方顺序要求）。
- 校验消息：采用 `AddDataAnnotationsLocalization`（MVC 控制器）或 .NET 11 的 `AddValidationLocalization`，把 DataAnnotations 消息键化。
- 随模板携带默认 `zh-CN` / `en-US` 资源；条件裁剪（如 identity/notifications）对应模块的消息键随功能块一起裁剪。

### 4.3 Template-frontend：Transloco + PrimeNG 联动

- 安装 **Transloco**；`app.config.ts`：`provideTransloco({ availableLangs:['zh','en'], defaultLang:'zh', ... })`。
- 词条放 `public/i18n/{zh,en}.json`（运行时 fetch）；每个文件含一个 `primeng` 段供组件库使用。
- **语言切换服务**：`translocoService.setActiveLang(lang)` 同时 `translocoService.selectTranslate('primeng').subscribe(r => primeng.setTranslation(r))`，让 PrimeNG 跟随。
- **HTTP 拦截器**：每个请求注入 `Accept-Language: <活动语言>`，使后端错误消息按同一 culture 返回。
- **收敛现有写死中文**：`http-error-interceptor.ts` 的 `CODE_MESSAGES`、`global-error-handler.ts` 的 `summary/detail` 改为查 Transloco 词条；**业务错误提示优先直接显示后端已本地化的 `message`**（后端已按 `Accept-Language` 产出），前端 map 仅保留纯客户端兜底（网络断开/后端不可达）。需按错误类型做差异化 UI 行为时，前端读 **`code`** 分支（与现有 `error.error.code` 读取一致）。
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

---

## 五、实施顺序与验证（后续分批）

> 本轮不写代码。以下为建议的执行批次，每批按所属交付面的 Skill 与验证入口收口。

1. **框架能力**（`developing-leistd-framework`）：建 `Leistd.Localization.*` → 破坏性改造 `Leistd.Exception`（`BusinessException` 第一参数改文案键、加 `Data`/`WithData`、handler 接 localizer 按文案键+culture 产出 message/title；`Code`/`Details` 保持本职）→ 构建/测试/打包 `.tmp/local-feed` → 包消费检查 → 组件文档 + 迁移说明 `framework/docs/components/` 同步。
2. **模板后端**（`developing-leistd-template`）：先 `.tmp/local-feed` 打包新框架 → 模板经 NuGet 消费 → `Program.cs` 装配 → 生成项目还原/构建/测试 → 多 culture 请求验证错误消息随 `Accept-Language` 变化。
3. **模板前端**（`developing-leistd-template`）：Transloco + PrimeNG 联动 + 拦截器 → 前端构建 → 语言即时切换与错误码文案对齐验证。
4. **文档沉淀**：框架侧本地化组件契约进 `framework/docs/components/`；模板侧 i18n 工程规范进 `template/docs/standards/`（含前端运行时库选型与 `$localize` 取舍 cross-link）。

**残余风险 / 待定**：文案键命名规范（`模块:实体动作`，需成文约束避免各模块乱起）、通用键是否从 HTTP status 自动推导（让通用错误 throw 处连键都不必传）、支持语言清单（起步 zh-CN + en-US？）、用户级语言是否持久化到后端账户、校验消息走 MVC `AddDataAnnotationsLocalization` 还是 .NET 11 `AddValidationLocalization`——这些在批次 1 开工前确认。

---

## 六、参考

- Angular v21 i18n（编译期、按 locale 部署）：<https://v21.angular.dev/guide/i18n>、数据格式化 <https://v21.angular.dev/guide/i18n/format-data-locale>
- PrimeNG i18n（`providePrimeNG.translation` + 运行时 `setTranslation`）：<https://primeng.org/configuration>
- .NET 本地化原语（`IStringLocalizer`、`SharedResource`、位置参数）：<https://learn.microsoft.com/dotnet/core/extensions/localization>
- ASP.NET Core 本地化与 culture provider：<https://learn.microsoft.com/aspnet/core/fundamentals/localization>、<https://learn.microsoft.com/aspnet/core/fundamentals/localization/select-language-culture>、内容可本地化化（`AddDataAnnotationsLocalization`）<https://learn.microsoft.com/aspnet/core/fundamentals/localization/make-content-localizable>
- .NET 11 校验消息本地化（`AddValidationLocalization`）：<https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-11>
- ABP 异常本地化（两模型：`UserFriendlyException` 直传 / 错误码→资源 `MapCodeNamespace`、`WithData` 参数、虚拟 JSON、`Accept-Language` 客户端）：<https://abp.io/docs/latest/framework/fundamentals/exception-handling>、<https://abp.io/docs/latest/framework/fundamentals/localization>
- Angular i18n 库对比（Transloco vs ngx-translate，2026）：<https://intlayer.org/blog/i18n-technologies/frameworks/angular>、<https://phrase.com/blog/posts/best-libraries-for-angular-i18n/>
- 全栈本地化架构分工：<https://phrase.com/blog/posts/full-stack-javascript-i18n/>
