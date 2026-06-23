# 核心原语：时钟与通用异常

`Leistd.Core` 是整个框架最底层的零依赖基础包，只提供两类被反复复用的原语：**时钟抽象**（`IClock`）与**通用异常基类**（`CommonException`）。它不引入任何业务概念，也几乎不引入第三方依赖（仅引用 `Microsoft.Extensions.Logging.Abstractions`），因此可以被其它所有 Leistd 组件安全地共同引用而不会带来依赖膨胀。

为什么需要它：直接调用 `DateTime.UtcNow` 会让代码与系统时钟强耦合，单元测试无法控制"现在"，按天统计还容易踩到时区漂移的坑；而散落在各处、各自继承 `System.Exception` 的异常类型则无法被框架统一识别和处理。`Leistd.Core` 把这两件事收敛为可注入、可替换的抽象：时间统一通过 `IClock` 获取，框架级异常统一从 `CommonException` 派生，使上层组件（异常处理、DDD 审计等）能据此做统一的可测试与可拦截设计。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 需要获取当前时间且希望单元测试可控（mock 时间） | 注入 `IClock`，不要直接用 `DateTime.UtcNow` |
| 按"自然日"做统计，需消除时区漂移 | `IClock` + `ClockExtensions.GetLocalMidnightInUtc()` |
| 标准化外部传入的 `DateTime`（统一为 UTC） | `IClock.Normalize(dateTime)` |
| 定义框架/业务异常的根基类型 | 派生自 `CommonException`（如异常处理组件的 `BusinessException`） |

> `Leistd.Core` 是被依赖项，通常无需直接添加——你引用的上层组件（异常处理、DDD 等）已传递引用它。

## 安装

```bash
# 核心原语（基础包，通常由上层组件传递引用，一般无需单独添加）
dotnet add package Leistd.Core
```

> 本仓库的模板项目通过中央包管理（CPM）统一版本，添加时无需写版本号。

## 配置 Provider

`Leistd.Core` 自身**不提供** DI 扩展方法。`IClock` 的默认实现 `UtcClockProvider` 由上层的 DDD 基础设施包注册（参见 `Leistd.Ddd.Infrastructure`）：

```csharp
// 在基础设施层注册（已由 Leistd.Ddd.Infrastructure 完成）
services.AddSingleton<IClock, UtcClockProvider>();
```

若你的项目未引用 DDD 分组而需要单独使用 `IClock`，按上面这行手动注册即可。`CommonException` 无需注册，按需 `throw` 或派生使用。

## 使用

注入 `IClock` 获取当前时间，避免直接依赖系统时钟，从而让逻辑可测试：

```csharp
public class DailyReportService(IClock clock)
{
    public DateTime NowUtc() => clock.Now; // 默认实现始终返回 UTC

    // 统计"今天"的数据：用本地自然日零点的 UTC 锚点做范围下界，避免时区漂移
    public (DateTime from, DateTime to) TodayRangeUtc()
    {
        var from = clock.GetLocalMidnightInUtc();   // 本地今日 00:00 对应的 UTC 时刻
        return (from, clock.Now);
    }

    // 标准化外部传入时间：Unspecified 视为 UTC，Local 转 UTC
    public DateTime NormalizeInput(DateTime input) => clock.Normalize(input);
}
```

定义框架/业务异常时派生自 `CommonException`，上层异常处理组件可据此统一识别：

```csharp
public class InsufficientStockException(string sku)
    : CommonException($"库存不足: {sku}");
```

## 接口参考

`Leistd.Timing` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `IClock` | 时钟抽象接口，统一时间获取入口，便于测试 mock 与时区策略统一 |
| `IClock.Now` | 当前时间（`DateTime`）；默认实现返回 UTC |
| `IClock.Kind` | 时间类型（`DateTimeKind`）；默认实现为 `Utc` |
| `IClock.Normalize(dateTime)` | 标准化时间，确保 `Kind` 一致；默认实现：`Unspecified` 视为 UTC，`Local` 转 UTC，`Utc` 原样返回 |
| `UtcClockProvider : IClock` | 默认实现，`Now` 返回 `DateTime.UtcNow`，`Kind` 为 `Utc` |
| `ClockExtensions.GetLocalMidnightInUtc(this IClock)` | 扩展方法，返回"系统本地今日零点"对应的 UTC 时刻，按天统计的基准锚点 |
| `ClockExtensions.GetLocalUtcOffsetHours(this IClock)` | 扩展方法，返回当前时区相对 UTC 的偏移小时数（`double`） |

`Leistd.Exception` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `CommonException(message, innerException?)` | 框架通用异常基类，继承 `System.Exception`；上层异常体系（如 `BusinessException`）由它派生 |

## 实现行为

### Leistd.Core（UtcClockProvider）

- `Now` 直接返回 `DateTime.UtcNow`，`Kind` 固定为 `DateTimeKind.Utc`；推荐数据库统一以 UTC 存储、展示时再按用户时区转换，可规避夏令时问题。
- `Normalize` 的规则：`Unspecified` 假定为 UTC（`SpecifyKind`）；`Local` 调用 `ToUniversalTime()` 转 UTC；`Utc` 原样返回。
- `GetLocalMidnightInUtc` 基于 `TimeZoneInfo.Local` 计算：取当前 UTC → 转本地 → 取本地日期零点 → 再转回 UTC。例如北京时间 `2026-05-28 00:00:00` 返回 UTC `2026-05-27 16:00:00Z`。
- 该实现无状态，以 Singleton 注册即可。

## 配置项 / Options

当前无配置项。`UtcClockProvider` 与 `CommonException` 均无 Options 类，时区行为由系统 `TimeZoneInfo.Local` 决定。

## 注意事项

- 默认 `IClock` 实现始终基于 **UTC**；`GetLocalMidnightInUtc` / `GetLocalUtcOffsetHours` 依赖**进程所在主机的本地时区**（`TimeZoneInfo.Local`），容器化部署时需确认容器时区设置是否符合预期。
- `Leistd.Core` 本身不注册任何服务；`IClock` 的注册由 `Leistd.Ddd.Infrastructure` 完成。脱离 DDD 分组单独使用时务必手动 `AddSingleton<IClock, UtcClockProvider>()`，否则注入会失败。
- `CommonException` 是一个轻量基类（仅 `message` + 可选 `innerException`），不携带错误码等元数据；语义化的业务异常请使用[异常处理](./exception.md)组件的 `BusinessException` 体系。

## 相关

- [组件总览](./README.md)
- [异常处理](./exception.md)
