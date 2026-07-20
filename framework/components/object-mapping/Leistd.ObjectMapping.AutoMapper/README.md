# Leistd.ObjectMapping.AutoMapper

> ⚠️ **已弃用（安全原因）**：AutoMapper 13.0.1 存在高危 DoS 漏洞（[GHSA-rvv3-g6hj-g44x](https://github.com/advisories/GHSA-rvv3-g6hj-g44x) / CVE-2026-32933），官方仅在改为**商业授权**的 15.1.1+ 修复、14.x 不修复。`AddAutoMapperObjectMapper` 已标 `[Obsolete]`。

该 provider 仅保留给已有 AutoMapper 集成使用，构建会输出 NU1903 安全告警（预期）。框架模板默认使用 `Leistd.ObjectMapping.Mapster`——**新的组件和模板实现请默认选择 Mapster**（同一 `IObjectMapper` 抽象，零授权成本、无该漏洞）。

## 风险接受记录（Risk Acceptance）

团队已正式**接受** GHSA-rvv3-g6hj-g44x / CVE-2026-32933 的遗留风险并保留本包，理由与边界如下（避免后续人/AI 反复重判）：

- **决策**：保留 AutoMapper 13.0.1 provider，不升级到需商业授权的 15.1.1+，不停止打包。
- **依据**：漏洞为深层嵌套对象图导致的 DoS（`StackOverflowException`）；本 provider 仅用于内部 DTO/实体映射，映射深度可控、不接受不可信的任意深度输入，实际可利用面极低。
- **缓解**：`AddAutoMapperObjectMapper` 已标 `[Obsolete]`；模板默认 Mapster；下游 restore/build 产生 NU1903 使风险不被静默隐藏。
- **重新评估条件**：若出现可用补丁的免费版本、或本 provider 被用于处理不可信深度输入的场景，则应重新评估升级/停用。

如需恢复主动维护，需要先升级 AutoMapper 依赖版本，并重新执行包漏洞检查。
