# 核心原语（`core`）
> Leistd 框架的零依赖基础原语：时钟抽象与通用异常基类，供其它组件复用。

## 包

| 包 | 角色 | 何时引用 |
| --- | --- | --- |
| `Leistd.Core` | 框架核心原语库，提供时间抽象（`IClock`）与通用异常基类（`CommonException`） | 需要可测试的时间获取、统一的 UTC 时间策略，或需要抛出框架级通用异常时 |

## 核心抽象

### `IClock`（命名空间 `Leistd.Timing`）

时间获取的抽象接口，目的在于可测试性（单测可 mock 时间）、统一时区策略、集中管理时间来源。

```csharp
DateTime Now { get; }
```
返回当前时间；具体取值（UTC/本地）由实现决定。

```csharp
DateTimeKind Kind { get; }
```
返回该时钟产出时间的类型（`Utc` / `Local` / `Unspecified`）。

```csharp
DateTime Normalize(DateTime dateTime);
```
将传入时间标准化到本时钟的时间类型并返回，确保时间类型一致。

### `CommonException`（命名空间 `Leistd.Exception`）

框架通用异常基类，继承自 `System.Exception`，支持携带消息与可选内部异常。

```csharp
public class CommonException(string message, System.Exception? innerException = null);
```
构造一个通用异常；`innerException` 可选，默认 `null`。

## 能力实现

### `Leistd.Core`

本包不提供 DI 注册扩展方法（源码中无 `IServiceCollection` 扩展），`IClock` 的默认实现需由使用方自行注册。

- **`UtcClockProvider`（`IClock` 默认实现）**：始终返回 UTC 时间。
  - `Now` => `DateTime.UtcNow`，`Kind` => `DateTimeKind.Utc`。
  - `Normalize`：`Unspecified` 类型被假定为 UTC 并打上 UTC 标记；`Local` 类型转换为 UTC；已是 UTC 则原样返回。
  - 设计理由（源码注释）：数据库统一存 UTC、前端按用户时区展示、规避夏令时问题。

- **`ClockExtensions`（`IClock` 静态扩展，用于时区边界计算）**：
  - `GetLocalMidnightInUtc(this IClock)`：返回"本地今日零点"对应的 UTC 时间，作为按天统计的锚点，消除直接比较本地时间/UTC 日期带来的时区漂移。示例：北京时间 `2026-05-28 00:00:00` -> `UTC 2026-05-27 16:00:00Z`。
  - `GetLocalUtcOffsetHours(this IClock)`：返回当前本地时区相对 UTC 的偏移小时数（基于 `TimeZoneInfo.Local`）。

## 最小可用示例

```csharp
using Leistd.Timing;
using Leistd.Exception;
using Microsoft.Extensions.DependencyInjection;

// 注册（本包无内置 DI 扩展，手动登记默认实现）
var services = new ServiceCollection();
services.AddSingleton<IClock, UtcClockProvider>();
var provider = services.BuildServiceProvider();

// 使用
var clock = provider.GetRequiredService<IClock>();
DateTime nowUtc = clock.Now;                          // 当前 UTC 时间
DateTime midnight = clock.GetLocalMidnightInUtc();    // 本地今日零点（UTC 锚点）
double offset = clock.GetLocalUtcOffsetHours();       // 本地时区偏移小时数

// 标准化外部传入的时间
DateTime normalized = clock.Normalize(DateTime.Now);  // Local -> UTC

// 抛出框架通用异常
throw new CommonException("业务校验失败");
```

## 依赖

无 Leistd 组件依赖（仅引用 `Microsoft.Extensions.Logging.Abstractions`）。

## 备注

- 本包**未提供** DI 注册扩展方法，`IClock` 的实现需使用方手动注册（如 `AddSingleton<IClock, UtcClockProvider>()`）。
- `UtcClockProvider.Normalize` 对 `DateTimeKind.Unspecified` 的处理是**假定为 UTC**，若上游存的是本地时间需自行先转换，避免出现 8 小时偏差。
- `ClockExtensions` 中的本地时区基于运行环境的 `TimeZoneInfo.Local`，部署环境时区需与业务自然日预期一致。
