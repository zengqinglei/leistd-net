# {req-id} 开发自测报告

## 1. 总结

- 需求 ID：{req-id}
- Phase：3 coding
- 结论：pass / fail / blocked
- 时间：{iso-datetime}

## 2. 范围

- Plan：docs/requirements/{req-id}-plan.md
- Context：docs/requirements/context/{req-id}.md
- 本次目标：待填写

## 3. 代码变更

| 文件 | 操作 | 说明 |
| --- | --- | --- |
| 待填写 | 新增/修改/删除 | 待填写 |

## 4. 最小验证

| 验证项 | 命令/方式 | 结果 | 证据/日志 |
| --- | --- | --- | --- |
| 构建/类型检查/相关测试 | 待填写 | pass/fail/skipped | 待填写 |

## 5. 内置自查

| 检查项 | 结果 | 说明 |
| --- | --- | --- |
| Must have 覆盖 | pass/fail | 待填写 |
| 安全/权限/数据风险 | pass/fail | 待填写 |

### 5.1 规范硬规则自查（逐项，不得整体填一个 pass）

> **核对项来源 = 项目 `docs/standards/code-standard/*` 与 `api-standard.md` 里带「必须/禁止/而非」的明文硬条款**——不是本模板凭空规定的。下表行是**本模板 .NET/Angular 栈的示例条目**：本栈项目直接用；**非本栈项目应据自己 code-standard 的硬条款替换这些行**（规范未声明某条填「不适用」）。开发阶段先自查、审查阶段再复核（与 code-review 核对表同源）。仅列与本次变更相关的行。

| 硬规则 | 依据 | 自查结论 |
| --- | --- | --- |
| 动手前已找 1~2 个同类端到端参考链路并对齐 | common §5.0 | 符合 / 违反 / 不适用 |
| 守卫放对层（单聚合→领域 / 跨聚合→应用） | common §5.1 | 符合 / 违反 / 不适用 |
| 未在内层重复 DTO 验证；未误删多入口共享守卫 | common §5.2 | 符合 / 违反 / 不适用 |
| 新能力扩现有抽象，未旁开第二套实现路径 | common §5.3 | 符合 / 违反 / 不适用 |
| 依赖只向内、抽象在内层 | common §5.4 | 符合 / 违反 / 不适用 |
| 外部资源删除幂等（只吞 NotFound） | common §5.5 | 符合 / 违反 / 不适用 |
| 对外契约分类字段一律小写；枚举只在边界转串 | common §5.7 | 符合 / 违反 / 不适用 |
| 接口/实现签名同步；无正文内联全限定名 | common §5.8 | 符合 / 违反 / 不适用 |
| 连锁字段"改一路改全"（后端 DTO→…→mock 数据 六环） | frontend §5.6 | 符合 / 违反 / 不适用 |
| mock 守卫与后端逐道一致（含前端变更时） | frontend §5.4 | 符合 / 违反 / 不适用 |
| 分页方法名 `GetPagedListAsync`（非 `GetListAsync`） | api-standard §7 | 符合 / 违反 / 不适用 |
| 对外路由 `/api/v1/{resource}` 前缀 | api-standard §7 | 符合 / 违反 / 不适用 |
| 禁用 `DateTime.Now/UtcNow`，统一 `IClock`；枚举字符串持久化 | backend §3.2.1 / §3.7 | 符合 / 违反 / 不适用 |
| 审计字段不手写，由拦截器填充 | backend-develop §3.3 | 符合 / 违反 / 不适用 |
| Controller 裸返回对象、异步 `Async` 后缀、record DTO 等 | backend-develop 检查清单 | 符合 / 违反 / 不适用 |
| 前端目录/命名/拦截器落点、UI 策略（含前端变更时） | frontend-develop / ui-design-strategy | 符合 / 违反 / 不适用 |

## 6. Must have 实现对照

| Must have | 实现状态 | 证据 | 说明 |
| --- | --- | --- | --- |
| 待填写 | pass/fail/partial | 文件/测试/日志 | 待填写 |

## 7. 风险与下一步

- 待填写

## 8. 阶段交接包

```yaml
handoff:
  reqId: {req-id}
  phase: 3
  phaseName: coding
  gateStatus: pass | fail | blocked
  nextRecommendedSkill: code-review | coding | task-manager
  userConfirmationRequired: false
  artifacts:
    - docs/reports/development/{req-id}-dev-report.md
  evidence: []
  blockers: []
  assumptions: []
```
