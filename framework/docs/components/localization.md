# 多语言本地化（JSON 资源）

需要按用户语言返回文案（错误消息、提示、标题）时，传统做法是把中文/英文字符串写死在代码里，无法随请求切换语言。Leistd 的本地化组件提供一个**基于嵌入 JSON 资源**的 `IStringLocalizer` 实现：代码里只写**文案键**，真正的文案放在随程序集分发的 `{culture}.json` 里，运行时按当前 culture 查表产出。

它坐在 .NET 标准的 `Microsoft.Extensions.Localization` 抽象之上——消费者拿到的是原生 `IStringLocalizer` / `IStringLocalizer<T>`，没有 Leistd 私有抽象；只是把默认的 RESX 资源源换成了更易维护、可 review 的 JSON。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| Web API 需按 `Accept-Language` 返回本地化文案 | 引用 `Leistd.Localization.AspNetCore`，`AddJsonLocalization` + `UseJsonRequestLocalization` |
| 领域/类库层只需注入 `IStringLocalizer` 查文案，不依赖 ASP.NET Core | 只引用 `Leistd.Localization.Core` |
| 业务项目要覆盖或追加框架默认文案 | 在自己的程序集放同名键的 `{culture}.json` 并登记该程序集 |
| 与 `Leistd.Exception` 配合，让错误消息本地化 | 启用本地化后，异常用 `WithLocalization("模块:键")` 挂展示键、`WithData` 传占位参数（`Message` 始终是英文诊断，见 [`exception`](./exception.md) 的三分离契约） |

> 资源为 JSON（ABP 式 `culture` + `texts`），不是 RESX。未注册本地化时，依赖 `IStringLocalizer` 的组件（如异常处理器）自动退回"直出原字符串"，行为与未引入本组件时一致。

## 安装

```bash
# 本地化抽象与 JSON localizer（领域/类库层可只引 Core）
dotnet add package Leistd.Localization.Core

# ASP.NET Core 装配（Web 宿主项目引用，已传递引用 Core）
dotnet add package Leistd.Localization.AspNetCore
```

## 配置 Provider

在 `Program.cs` 注册本地化并接入请求 culture 中间件：

```csharp
// 声明支持语言（首个为默认/回落语言），并登记承载资源的程序集
builder.Services.AddJsonLocalization(
    supportedCultures: ["en", "zh-CN"],       // 默认语言 = en（英语）
    configure: options =>
    {
        // 追加业务项目自身程序集的资源（覆盖/扩展框架默认键）
        options.ResourceAssemblies.Add(typeof(Program).Assembly);
    });

var app = builder.Build();

// 必须在任何读取当前 culture 的中间件之前
app.UseJsonRequestLocalization();
```

`AddJsonLocalization` 注册 `JsonStringLocalizerFactory` 为 `IStringLocalizerFactory`、开放 `IStringLocalizer` / `IStringLocalizer<T>` 解析，并配置 `RequestLocalizationOptions`（默认语言 + 支持语言）。`UseJsonRequestLocalization` 包装 `UseRequestLocalization`，启用 QueryString / Cookie / `Accept-Language` 三个 culture provider。框架自身程序集默认已登记，用于分发通用键（`Error:*`、`Title:*`）。

## 资源文件

资源为嵌入程序集的 JSON，按 `{ResourcesPath}/{culture}.json`（默认 `Resources/`）命名，`EmbeddedResource` 打包：

```jsonc
// Resources/en.json
{
  "culture": "en",
  "texts": {
    "Order:StockInsufficient": "Out of stock: {Sku}"
  }
}
// Resources/zh-CN.json
{
  "culture": "zh-CN",
  "texts": {
    "Order:StockInsufficient": "库存不足：{Sku}"
  }
}
```

```xml
<!-- csproj -->
<ItemGroup>
  <EmbeddedResource Include="Resources\*.json" />
</ItemGroup>
```

- **缺 `culture` 段的文件被忽略**（与 ABP 行为一致）。
- **具名占位符**：值里的 `{Name}` 由调用方参数填充（位置参数走 `IStringLocalizer["key", args]`；与异常配合时由 `WithData("Name", value)` 填充）。
- **覆盖**：同一键在多个已登记程序集出现时，**后登记者覆盖前者**，业务项目因此可覆盖框架默认文案。
- **坏文件容错**：某个资源不是合法 JSON 时，读取器**跳过该文件并告警**（`ILogger` Warning），其余程序集/键正常加载，绝不因单个坏文件拖垮整个本地化。
- **culture 声明校验**：文件内 `culture` 段与文件名解析出的 culture 不一致时**仍加载键值，仅告警**（多为复制粘贴漏改），避免因笔误整份文案丢失。
- **启动预热**：`AddJsonLocalization` 会注册一个 `IHostedService`，在启动阶段按支持语言预热资源缓存——把上述解析/告警提前到启动日志暴露，而非在生产首个请求时才隐性发生。

## 使用

注入 `IStringLocalizer` 按键取文案：

```csharp
public class OrderNotifier(IStringLocalizer localizer)
{
    public string StockWarning() => localizer["Order:StockInsufficient", "A1"]; // 位置参数 {0}
}
```

按当前 `CultureInfo.CurrentUICulture` 逐级回落（`zh-Hans-CN` → `zh-Hans` → `zh`），再回落到默认语言；**仍未命中则返回键本身**（.NET "键即默认值" 语义）。

## 接口参考

`Leistd.Localization.Core` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `JsonStringLocalizer` | `IStringLocalizer` 实现，按 culture 回落查嵌入 JSON，未命中返回键本身 |
| `JsonStringLocalizerFactory` | `IStringLocalizerFactory` 实现，`Create(Type)` / `Create(string,string)` 返回同一共享视图 |
| `JsonLocalizationResourceReader` | 读取并缓存各程序集嵌入 JSON，按登记顺序合并键 |
| `JsonLocalizationOptions.ResourceAssemblies` | 承载嵌入 JSON 的程序集集合，后登记者覆盖前者 |
| `JsonLocalizationOptions.ResourcesPath` | 嵌入资源逻辑目录，默认 `Resources` |
| `JsonLocalizationOptions.DefaultCulture` | 默认/回落语言，默认 `en` |

`Leistd.Localization.AspNetCore` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `AddJsonLocalization(supportedCultures?, configure?)` | 注册 JSON localizer 栈并配置支持语言（首个为默认/回落语言，默认 `["en","zh-CN"]`） |
| `UseJsonRequestLocalization()` | 接入请求 culture 解析中间件（QueryString / Cookie / Accept-Language） |

## 实现行为

- **资源源**：仅 JSON（嵌入程序集），不使用 RESX；替换 .NET 默认的 `ResourceManagerStringLocalizerFactory`。
- **回落链**：当前 UI culture 及其父链，末尾追加 `DefaultCulture`；每级去重。
- **合并与覆盖**：各程序集同 culture 的键合并进一张表，按 `ResourceAssemblies` 顺序后者覆盖前者；每个 culture 合并结果缓存一次。
- **未命中**：返回请求的键本身，且 `LocalizedString.ResourceNotFound` 为 `true`（调用方可据此判断是否漏配）。

## 配置项 / Options

`JsonLocalizationOptions`：

| 属性 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `ResourceAssemblies` | `IList<Assembly>` | 空（`Add*` 时登记） | 承载嵌入 JSON 的程序集；后登记者覆盖前者 |
| `ResourcesPath` | `string` | `Resources` | 嵌入资源相对程序集根的逻辑目录 |
| `DefaultCulture` | `string` | `en` | 默认/回落语言 |

## 注意事项

- 未调用 `AddJsonLocalization` 时，`IStringLocalizer` 未注册；依赖它的 `Leistd.Exception` 全局处理器会自动退回"直出原消息"，因此**是否启用本地化不影响未启用方的行为**。
- 资源 JSON 必须 `EmbeddedResource`；仅作为 `Content` 不会被读取。
- 键为**全局唯一**的文案键（如 `Order:StockInsufficient`），不按类型/目录分资源——工厂对所有 `Create` 返回同一合并视图。

## 相关

- [组件总览](./README.md)
- [业务异常与全局异常处理](./exception.md)（错误消息本地化）
- [核心基础库](./core.md)
