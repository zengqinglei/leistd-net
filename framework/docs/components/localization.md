# 多语言本地化（JSON 资源）

本地化组件从程序集内嵌的 `{culture}.json` 加载文案，并将未登记的强类型资源委派给 .NET RESX 工厂。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| Web API 需按 Accept-Language 请求头返回本地化文案 | 引用 `Leistd.Localization.AspNetCore`，`AddJsonLocalization` + `UseJsonRequestLocalization` |
| 领域/类库层只需注入 `IStringLocalizer` 查文案，不依赖 ASP.NET Core | 只引用 `Leistd.Localization.Core` |
| 业务项目要覆盖或追加框架默认文案 | 在自己的程序集放同名键的 `{culture}.json` 并登记该程序集 |
| 与 `Leistd.ExceptionHandling` 配合，让错误消息本地化 | 异常用 `WithCode("模块:键")` 给出错误码（它同时是词条键）、`WithData` 传占位参数（`Message` 始终是英文诊断，见 [`exception-handling`](./exception-handling.md)） |

未注册本地化时，框架异常处理器会直接使用原消息。

## 安装

```bash
dotnet add package Leistd.Localization.Core
dotnet add package Leistd.Localization.AspNetCore
```

## 注册

在 `Program.cs` 注册本地化并接入请求 culture 中间件：

```csharp
builder.Services.AddJsonLocalization(
    supportedCultures: ["en", "zh-CN"],
    configure: options =>
    {
        options.ResourceAssemblies.Add(typeof(Program).Assembly);
        options.JsonResourceTypes.Add(typeof(MyResourceMarker));
    });

var app = builder.Build();

app.UseJsonRequestLocalization();
```

`supportedCultures` 的首项是默认语言。无参 `IStringLocalizer` 读取 JSON；`IStringLocalizer<T>` 只有在 `T` 已加入 `JsonResourceTypes` 时读取 JSON，否则使用 `ResourceManagerStringLocalizerFactory`。`UseJsonRequestLocalization` 应放在所有读取当前 culture 的中间件之前。

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

- 缺少 `culture` 的文件会被忽略。
- 同一 culture 的键按 `ResourceAssemblies` 顺序合并，后登记的程序集覆盖前者。
- 无效 JSON 会被跳过并记录 Warning；文件名与 `culture` 不一致时仍加载，但记录 Warning。
- 启动服务按支持语言预热资源缓存。

## 使用

注入 `IStringLocalizer` 按键取文案：

```csharp
public class OrderNotifier(IStringLocalizer localizer)
{
    public string StockWarning() => localizer["Order:StockInsufficient", "A1"];
}
```

按当前 `CultureInfo.CurrentUICulture` 逐级回落（`zh-Hans-CN` → `zh-Hans` → `zh`），再回落到默认语言；**仍未命中则返回键本身**（.NET "键即默认值" 语义）。

## 接口参考

| 成员 | 说明 |
| --- | --- |
| `JsonStringLocalizer` | `IStringLocalizer` 实现，按 culture 回落查嵌入 JSON，未命中返回键本身 |
| `JsonStringLocalizerFactory` | `IStringLocalizerFactory` 实现，`Create(Type)` / `Create(string,string)` 返回同一共享视图 |
| `JsonLocalizationResourceReader` | 读取并缓存各程序集嵌入 JSON，按登记顺序合并键 |
| `JsonLocalizationOptions.ResourceAssemblies` | 承载嵌入 JSON 的程序集集合，后登记者覆盖前者 |
| `JsonLocalizationOptions.ResourcesPath` | 嵌入资源逻辑目录，默认 `Resources` |
| `JsonLocalizationOptions.DefaultCulture` | 默认/回落语言，默认 `en` |

| `AddJsonLocalization(supportedCultures?, configure?)` | 注册 JSON localizer 栈并配置支持语言（首个为默认/回落语言，默认 `["en","zh-CN"]`） |
| `UseJsonRequestLocalization()` | 接入请求 culture 解析中间件（查询参数、Cookie、Accept-Language 请求头） |

## 配置项

`JsonLocalizationOptions`：

| 属性 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `ResourceAssemblies` | `IList<Assembly>` | 空（`Add*` 时登记） | **加载**：承载嵌入 JSON 的程序集；后登记者覆盖前者 |
| `JsonResourceTypes` | `ISet<Type>` | 空 | **路由**：显式登记走 JSON 的 typed 资源标记类型；未登记的 `IStringLocalizer<T>` 走官方 RESX |
| `ResourcesPath` | `string` | `Resources` | 嵌入资源相对程序集根的逻辑目录 |
| `DefaultCulture` | `string` | `en` | 默认/回落语言 |

## 注意事项

- 未调用 `AddJsonLocalization` 时，异常处理器直接使用原消息。
- 资源 JSON 必须 `EmbeddedResource`；仅作为 `Content` 不会被读取。
- 文案键必须全局唯一；JSON 工厂对所有已登记类型提供同一合并视图。

## 相关

- [业务异常与全局异常处理](./exception-handling.md)（错误消息本地化）
- [核心基础库](./core.md)
