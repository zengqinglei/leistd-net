# Leistd.ObjectMapping.AutoMapper

> ⚠️ **已弃用（安全原因）**：AutoMapper 13.0.1 存在高危 DoS 漏洞（[GHSA-rvv3-g6hj-g44x](https://github.com/advisories/GHSA-rvv3-g6hj-g44x) / CVE-2026-32933），官方仅在改为**商业授权**的 15.1.1+ 修复、14.x 不修复。`AddAutoMapperObjectMapper` 已标 `[Obsolete]`。

该 provider 仅保留给已有 AutoMapper 集成使用，构建会输出 NU1903 安全告警（预期）。框架模板默认使用 `Leistd.ObjectMapping.Mapster`——**新的组件和模板实现请默认选择 Mapster**（同一 `IObjectMapper` 抽象，零授权成本、无该漏洞）。

## 风险接受记录（Risk Acceptance）

团队已正式**接受** GHSA-rvv3-g6hj-g44x / CVE-2026-32933 的遗留风险并保留本包，理由与边界如下（避免后续人/AI 反复重判）：

- **决策**：保留 AutoMapper 13.0.1 provider，不升级到需商业授权的 15.1.1+，不停止打包——但仅为存量消费者继续发布。
- **漏洞性质**：深层嵌套对象图导致的 DoS（`StackOverflowException`，无默认最大深度限制）。
- **本仓库的缓解**：官方模板**不使用**本 provider（默认 Mapster）；`AddAutoMapperObjectMapper` 已标 `[Obsolete]`；下游 restore/build 产生 NU1903 使风险不被静默隐藏。
- **下游消费者责任**（发布后的包无法强制这些约束，故属使用方职责）：保证交给映射器的对象图**深度有界**、对输入场景做过评估；**切勿**将 HTTP 请求 DTO 等不可信/不可控深度对象直接交给本 provider 映射。
- **风险接受的边界**：一旦本 provider 被用于处理不可信或不可控深度的对象，本风险接受**不再成立**，应改用 Mapster 或升级到已修复版本。

如需恢复主动维护，需要先升级 AutoMapper 依赖版本，并重新执行包漏洞检查。
