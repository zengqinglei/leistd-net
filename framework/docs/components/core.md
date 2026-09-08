# 核心原语：时钟与通用异常

`Leistd.Core` 是最底层的基础包，只放跨组件复用的原语：时钟抽象 `IClock` 让「现在」可注入、可测试，异常基类 `CommonException` 供上层统一识别。

## 何时使用

| 场景 | 用法 |
| --- | --- |
| 需要获取当前时间且希望单元测试可控（mock 时间） | 注入 `IClock`，不要直接用 `DateTime.UtcNow` |
| 按"自然日"做统计，需消除时区漂移 | `IClock` + `ClockExtensions.GetMidnightInUtc(timeZone)`（时区显式传入） |
| 标准化外部传入的 `DateTime`（统一为 UTC） | `IClock.Normalize(dateTime)` |
| 定义框架/业务异常的根基类型 | 派生自 `CommonException`（如异常处理组件的 `BusinessException`） |

> `Leistd.Core` 是被依赖项，通常无需直接添加——你引用的上层组件（异常处理、DDD 等）已传递引用它。

## 安装

```bash
# 核心原语（基础包，通常由上层组件传递引用，一般无需单独添加）
dotnet add package Leistd.Core
```

## 注册

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
        // 时区必须显式给出：它是业务输入（租户设置/用户偏好），不是宿主的环境属性
        var from = clock.GetMidnightInUtc(tenantTimeZone);   // 该时区今日 00:00 对应的 UTC 时刻
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
| `IClock.Normalize(dateTime)` | 归一化为 UTC：`Unspecified` 视为 UTC，`Local` 转 UTC，`Utc` 原样返回 |
| `UtcClockProvider : IClock` | 默认实现，取值委托给 `TimeProvider`（默认 `TimeProvider.System`） |
| `ClockExtensions.GetMidnightInUtc(this IClock, TimeZoneInfo)` | 扩展方法，返回**指定时区**今日零点对应的 UTC 时刻，按天统计的基准锚点 |
| `ClockExtensions.GetUtcOffsetHours(this IClock, TimeZoneInfo)` | 扩展方法，返回**指定时区**当前相对 UTC 的偏移小时数（`double`，已计入夏令时） |

`Leistd.Exceptions` 命名空间：

| 成员 | 说明 |
| --- | --- |
| `CommonException(message, innerException?)` | 框架通用异常基类，继承 `System.Exception`；上层异常体系（如 `BusinessException`）由它派生 |

## 实现行为

### Leistd.Core（UtcClockProvider）

- `Now` 取自注入的 `TimeProvider`（默认 `TimeProvider.System`），固定为 UTC。
- **不提供切换时间类型的开关**：实体主键用时间有序的 `Guid.CreateVersion7()`、审计时间线、跨服务传递的 `DateTime` 都以 UTC 为共同基准，换成本地时间会让这些保证各自失效且不报错。按用户时区展示在呈现层做。
- 建在 `TimeProvider` 之上是为了让全框架只有一个时间源：测试用 `FakeTimeProvider` 推进时间，审计时间戳、内存锁清理与定时器会一起跟着走。宿主未注册 `TimeProvider` 时 DI 会选中无参构造函数，行为等同 `TimeProvider.System`。
- `Normalize` 的规则：`Unspecified` 假定为 UTC（`SpecifyKind`）；`Local` 调用 `ToUniversalTime()` 转 UTC；`Utc` 原样返回。
- `GetMidnightInUtc(timeZone)` 按**传入时区**计算：取当前 UTC → 转该时区 → 取当日零点 → 再转回 UTC。例如时区为 `Asia/Shanghai`、当前 UTC 为 `2026-05-27T20:00:00Z` 时（该时区已是 05-28），返回 `2026-05-27T16:00:00Z`。
- 该实现无状态，以 Singleton 注册即可。

## 注意事项

- 默认 `IClock` 实现始终基于 **UTC**。两个日边界扩展方法**要求显式传入 `TimeZoneInfo`**，没有默认值。

  **为什么不读 `TimeZoneInfo.Local`**——"进程所在主机的时区"在三种常见部署下都是错的，且错得没有信号：

  | 部署形态 | 后果 |
  | --- | --- |
  | 容器（默认 UTC） | "本地今日"变成 UTC 今日，报表边界整体偏移 |
  | 多租户 SaaS | 各租户分处不同时区，"本地"根本不是单一值 |
  | 多可用区 | 各实例给出不同的日边界，统计结果随路由漂移 |

  时区是**业务输入**（租户设置、用户偏好、报表参数），不是宿主的环境属性。需要"服务器时区"语义时自行传 `TimeZoneInfo.Local`——那时它是一个写出来的决定。
- `Leistd.Core` 本身不注册任何服务；`IClock` 的注册由 `Leistd.Ddd.Infrastructure` 完成。脱离 DDD 分组单独使用时务必手动 `AddSingleton<IClock, UtcClockProvider>()`，否则注入会失败。
- `CommonException` 是一个轻量基类（仅 `message` + 可选 `innerException`），不携带错误码等元数据；语义化的业务异常请使用[异常处理](./exception-handling.md)组件的 `BusinessException` 体系。

## 相关

- [异常处理](./exception-handling.md)
