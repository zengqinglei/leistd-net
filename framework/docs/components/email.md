# 邮件发送

统一的 `IEmailSender` 抽象与 SMTP 实现。发送失败一律抛异常；没有可用 SMTP 的环境显式注册空发送器，不存在"配置不对就悄悄不发"的回落。

## 何时使用

| 场景 | 推荐实现 |
| --- | --- |
| 生产环境经 SMTP 发信（验证码、密码重置、系统通知） | `Leistd.Email.Smtp` |
| 本地开发或演示，没有可用 SMTP，且不需要看邮件内容 | `Leistd.Email.Core` 的 `AddNullEmailSender()` |
| 本地要看到邮件内容（验证码、重置链接） | 仍用 `Leistd.Email.Smtp`，指向本机邮件捕获器：`docker run -d -p 1025:1025 -p 8025:8025 axllent/mailpit` |
| 只写发信调用、不关心投递介质 | 只引用 `Leistd.Email.Core` 中的接口 |
| 需要队列、重试、退避 | 本组件不提供。宿主自己排队，或由调用方决定补偿 |
| 需要按模板渲染正文 | 本组件不提供。正文由调用方给出字符串 |

## 安装

```bash
dotnet add package Leistd.Email.Core

dotnet add package Leistd.Email.Smtp
```

## 注册

在 `Program.cs` 注册**其中一种**实现：

```csharp
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddNullEmailSender();
}
else
{
    builder.Services.AddSmtpEmailSender(builder.Configuration);
}
```

两者都绑定 `IEmailSender`。`AddSmtpEmailSender` 另有委托重载，并把配置校验挂到 `ValidateOnStart`——配置非法时宿主起不来，而不是等到第一次发信。实现类型只注册一次，接口是别名转发，重复调用不会产生两个实例。

## 使用

注入 `IEmailSender`，构造 `EmailMessage` 发送：

```csharp
public class RegistrationService(IEmailSender emailSender)
{
    public async Task SendCodeAsync(string email, string code, CancellationToken ct)
    {
        await emailSender.SendAsync(new EmailMessage
        {
            To = email,
            Subject = "Account Registration Verification Code",
            Body = $"<p>Your verification code is <b>{code}</b>.</p>",
        }, ct);
    }
}
```

**发送失败会抛异常**，调用方据此决定补偿。占用了外部资源的流程必须在失败路径上撤回：

```csharp
await cache.SetStringAsync(rateKey, "1", rateOptions, ct);
try
{
    await cache.SetStringAsync(challengeKey, payload, challengeOptions, ct);
    await emailSender.SendAsync(message, ct);
}
catch
{
    // 发不出去时既不能留下用不了的挑战，也不能占着限流槽位
    await Task.WhenAll(cache.RemoveAsync(challengeKey), cache.RemoveAsync(rateKey));
    throw;
}
```

不指定发件人时使用配置里的默认发件身份；需要按业务线区分发件人时给出 `FromAddress`：

```csharp
await emailSender.SendAsync(new EmailMessage
{
    To = customer.Email,
    Subject = "Invoice",
    Body = html,
    FromAddress = "billing@acme.com",
    FromName = "Acme Billing",
}, ct);
```

## 接口参考

| 成员 | 说明 |
| --- | --- |
| `IEmailSender` | 发信入口，业务层只依赖它 |
| `IEmailSender.SendAsync(message, ct)` | 发送一封邮件；**任何失败都抛异常**，不返回成功/失败标志 |
| `EmailMessage.To` | 收件人地址（必填） |
| `EmailMessage.Subject` | 主题（必填） |
| `EmailMessage.Body` | 正文（必填） |
| `EmailMessage.IsBodyHtml` | 正文是否为 HTML，默认 `true` |
| `EmailMessage.FromAddress` | 发件地址；`null` 时用 `DefaultFromAddress` |
| `EmailMessage.FromName` | 发件显示名；语义与 `FromAddress` 成对，见「注意事项」 |
| `NullEmailSender` | 不投递、只按 Warning 记录的实现，由 `AddNullEmailSender()` 注册 |
| `SmtpEmailSender` | SMTP 实现，由 `AddSmtpEmailSender()` 注册 |

`EmailMessage` 用对象而非多个 `SendAsync` 重载承载参数：日后追加抄送、附件等维度只是新增 `init` 属性，对既有调用方非破坏。

## 配置项（`Leistd:Email:Smtp`）

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `Host` | 空 | SMTP 主机。**必填**，留空时宿主启动失败 |
| `Port` | `587` | SMTP 端口，取值 1–65535 |
| `Username` | 空 | 登录用户名。留空表示匿名投递 |
| `Password` | 空 | 登录口令。**不要写进随代码分发的配置文件**，用环境变量、用户机密或密钥管理服务注入 |
| `EnableSsl` | `true` | 是否启用传输层加密。465 用连接即 TLS，其余端口用 STARTTLS |
| `DefaultFromAddress` | 空 | 默认发件地址。**必填**，`EmailMessage.FromAddress` 未指定时使用 |
| `DefaultFromName` | 空 | 默认发件显示名 |

```json
{
  "Leistd": {
    "Email": {
      "Smtp": {
        "Host": "smtp.acme.com",
        "Port": 587,
        "EnableSsl": true,
        "DefaultFromAddress": "noreply@acme.com",
        "DefaultFromName": "Acme Notifications"
      }
    }
  }
}
```

`Host`、`Port`、`DefaultFromAddress` 与「`Username`/`Password` 成对」四项在启动期校验，任一不过阻止宿主启动。地址是否合法交给 MimeKit 判定，与发送路径同一套解析——不会出现"配置过了却在发送时解析不出来"。

## 实现行为

### Leistd.Email.Core（`NullEmailSender`）

- 不连接任何服务器，不投递，返回成功。
- 按 **Warning** 级别记录收件人与主题，**不记录正文**。级别是"本环境不会真的发信"的唯一信号，因此不用 Debug——降级后，误把它注册进生产的部署会完全静默地丢掉每一封信。
- 不记正文是有意的：验证码、重置链接进应用日志等于把凭据留在日志里。要看内容请用 SMTP 指向本机邮件捕获器。
- 必须由宿主主动注册；它**不是**发送失败时的兜底。

### Leistd.Email.Smtp（`SmtpEmailSender`）

- 每次发送新建一个 `SmtpClient`，发完 `QUIT` 断开，不复用连接。
- `EnableSsl` 为 `true` 时按端口选握手方式：465 用隐式 TLS，其余用 STARTTLS。两者不可互换——对 465 用 STARTTLS 会卡在等待明文问候，对 587 用隐式 TLS 会握手失败。
- 仅在 `Username` 非空时认证。用户名与口令由启动期校验保证成对，取到一半时当场抛，而不是跳过认证继续发送。
- 连接、认证、投递的任何失败原样上抛，不包装、不记为已发送。

## 注意事项

- **发送失败一定抛异常**，不存在返回值形式的失败信号。占用了限流槽位、挑战缓存或数据库行的流程必须在 `catch` 里撤回，否则用户会拿到一个永远收不到码的挑战。
- **`NullEmailSender` 只能显式注册。** 任何"配置看起来不对就跳过发送"的回落都会让调用方误以为信已发出，因此本组件不提供这种行为——包括对占位主机名之类的特殊值。
- **发件地址与显示名成对取用。** 给出 `FromAddress` 而不给 `FromName` 时，显示名为空，不会贴上 `DefaultFromName`；否则会发出"自定义地址 + 系统署名"这种没人想要的组合，而且不报错。
- `IsBodyHtml` 取错不会报错，只会让收件人看到转义后的 HTML 源码或没有排版的信。
- 组件不排队、不重试、不退避。一次 `SendAsync` 就是一次同步投递尝试，耗时受 SMTP 往返影响；放在请求路径上时需要考虑它对响应时间的贡献。
- `Password` 属于凭据。它不该出现在随代码分发的配置文件里，也不该经由任何面向界面的设置接口读写。

## 相关

- [通知](./notifications.md)
- [异常处理](./exception-handling.md)
