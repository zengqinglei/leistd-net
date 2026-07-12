# Leistd.Exception.Web

ASP.NET Core全局异常处理组件，提供统一的异常处理和响应格式。

## 功能特性

- 统一的异常处理和响应格式
- 自动将各类异常转换为业务异常
- 支持验证异常的特殊处理
- 可配置的详细信息显示（开发/生产环境）
- 支持排除特定路径的异常处理
- 完整的日志记录

## 使用方法

### 1. 安装依赖

在您的ASP.NET Core项目中引用此项目：

```xml
<ProjectReference Include="path\to\Leistd.Exception.Web\Leistd.Exception.Web.csproj" />
```

### 2. 配置服务

在 `Program.cs` 或 `Startup.cs` 中注册服务：

```csharp
// 方式1: 从配置文件读取
builder.Services.AddGlobalExceptionHandler(builder.Configuration);

// 方式2: 使用委托配置
builder.Services.AddGlobalExceptionHandler(options =>
{
    options.Enable = true;
    options.IsShowDetails = null; // null表示开发环境显示，生产环境不显示
    options.ExcludePatterns.Add("/health");
    options.ExcludePatterns.Add("/api/health/**");
});
```

### 3. 使用中间件

在 `Program.cs` 中添加中间件（应该尽早添加）：

```csharp
var app = builder.Build();

// 在其他中间件之前添加
app.UseGlobalExceptionHandler();

// 其他中间件...
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
```

### 4. 配置文件设置（可选）

在 `appsettings.json` 中配置：

```json
{
  "Leistd": {
    "GlobalException": {
      "Enable": true,
      "IsShowDetails": null,
      "ExcludePatterns": [
        "/health",
        "/api/health/**"
      ]
    }
  }
}
```

## 配置说明

### GlobalExceptionOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| Enable | bool | false | 是否启用全局异常处理 |
| ExcludePatterns | HashSet<string> | 空 | 排除的URI模式，支持通配符 |
| IsShowDetails | bool? | null | 是否显示详情，null表示根据环境自动决定 |

### 通配符模式

- `*` - 匹配单个路径段中的任意字符（不包括 `/`）
- `**` - 匹配任意路径（包括子路径）

示例：
- `/api/health` - 精确匹配
- `/api/*/test` - 匹配 `/api/v1/test`, `/api/v2/test` 等
- `/api/health/**` - 匹配 `/api/health` 下的所有路径

## 响应格式

异常会被转换为 RFC 9457 `ProblemDetails`：

```json
{
  "type": "urn:leistd:error:40001",
  "title": "Bad Request",
  "status": 400,
  "detail": "Username already exists.",
  "instance": "/api/users",
  "code": 40001,
  "message": "Username already exists.",
  "traceId": "00-abc...-01"
}
```

## HTTP状态码映射

异常错误码的前三位映射为 HTTP 状态码。默认异常使用 `40000` 这类「状态码 + 00」形式；标准业务码使用「状态码 + 两位业务序号」的五位格式，例如 `.WithCode("01")` 生成 `40001`。
默认码的 `ProblemDetails.type` 使用对应 HTTP 规范 URI；业务码使用 `urn:leistd:error:{code}`，例如 `urn:leistd:error:40001`，作为语言无关的问题类型标识。

## 本地化响应

Core 层通过 `WithLocalization` 附加资源键和参数，ASP.NET Core 宿主通过 `IExceptionResponseLocalizer` 在请求边界翻译响应。未注册本地化器或资源不存在时自动回退原消息。

```csharp
public sealed class AppExceptionResponseLocalizer : IExceptionResponseLocalizer
{
    public ExceptionResponseLocalization Localize(BusinessException exception, int statusCode)
    {
        // 使用当前请求文化从 .resx、数据库或其他资源源解析。
        return new ExceptionResponseLocalization(message: null, title: null);
    }
}

builder.Services.AddSingleton<IExceptionResponseLocalizer, AppExceptionResponseLocalizer>();
```

## 示例

```csharp
// Controller中抛出异常
[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    [HttpGet("{id}")]
    public IActionResult GetUser(int id)
    {
        if (id <= 0)
        {
            throw new BadRequestException("User ID must be greater than zero.")
                .WithCode("01")
                .WithLocalization("Users.InvalidId");
        }

        var user = FindUser(id);
        if (user == null)
        {
            throw new NotFoundException($"User {id} was not found.");
        }

        return Ok(user);
    }
}

// 客户端收到 HTTP 400 ProblemDetails，code 为 40001。
```
