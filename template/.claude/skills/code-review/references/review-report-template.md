# 代码审查报告模板

> 仅在生成审查报告时读取。写入 `docs/reports/code-review/{req-id}-code-review.md`。
> 「规范硬规则核对」表**逐项如实勾选**——不得整体填一个「pass」了事；每条要给出核对结论（符合 / 违反 / 不适用），违反的记为 P1 并进 findings。

# {req-id} 代码审查报告

## 1. 总结

- 需求 ID：{req-id}
- Phase：4 code-review
- 结论：pass / fail（有 P0 即 fail）
- 审查基线：diff / 文件列表 / commit range
- 时间：{iso-datetime}

## 2. 需求对齐

| Must have | 是否实现 | 证据（文件:行） | 备注 |
| --- | --- | --- | --- |
| 待填写 | 是/否/部分 | path:line | — |

## 3. 规范硬规则核对（逐项，违反即 P1）

> **核对项来源 = 项目 `code-standard/{backend,frontend,common}-develop.md` 与 `api-standard.md` 里带「必须/禁止/而非」的明文硬条款**——不是本模板凭空规定的。下面三张表的行是**本模板 .NET/Angular 栈的示例条目**：本栈项目直接用；**非本栈项目应据自己 code-standard 的硬条款替换**（规范未声明某条填「不适用」并提示补规范）。违反明文硬条款 = P1。

### 通用工程铁律（common §5，跨前后端，优先核对）
| 硬规则 | 依据 | 核对结论 |
| --- | --- | --- |
| 开发前找了同类端到端参考链路并对齐（看 dev-report 是否说明） | common §5.0 | 符合 / 违反(P1) / 不适用 |
| 守卫放对层（单聚合→领域 / 跨聚合→应用） | common §5.1 | 符合 / 违反(P1) / 不适用 |
| 未在内层重复 DTO 验证；未误删多入口共享守卫 | common §5.2 | 符合 / 违反(P1) / 不适用 |
| 新能力扩现有抽象，未旁开第二套实现路径 | common §5.3 | 符合 / 违反(P1) / 不适用 |
| 依赖只向内、抽象在内层 | common §5.4 | 符合 / 违反(P1) / 不适用 |
| 外部资源删除幂等（只吞 NotFound） | common §5.5 | 符合 / 违反(P1) / 不适用 |
| 对外契约分类字段一律小写；枚举只在边界转串 | common §5.7 | 符合 / 违反(P1) / 不适用 |
| 接口/实现签名同步；无正文内联全限定名 | common §5.8 | 符合 / 违反(P1) / 不适用 |

### 后端（若本次含后端变更）
| 硬规则 | 依据 | 核对结论 |
| --- | --- | --- |
| 分页方法名 `GetPagedListAsync`（非 `GetListAsync`） | api-standard §7 | 符合 / 违反(P1) / 不适用 |
| 对外路由 `/api/v1/{resource}` 前缀 | api-standard §7 | 符合 / 违反(P1) / 不适用 |
| 分页参数用 `offset/limit`，输入 DTO 继承 `PagedRequestDto` | api-standard §6 | 符合 / 违反(P1) / 不适用 |
| 禁用 `DateTime.Now/UtcNow`，统一 `IClock` | backend-develop §3.2.1 | 符合 / 违反(P1) / 不适用 |
| 审计字段（`CreationTime` 等）不手写，由拦截器填充 | backend-develop §3.3 | 符合 / 违反(P1) / 不适用 |
| Controller 裸返回对象，不手动包 `Ok(...)` 信封 | api-standard §2 | 符合 / 违反(P1) / 不适用 |
| 异步方法 `Async` 后缀、record DTO、充血模型等 checklist | backend-develop 检查清单 | 符合 / 违反(P1) / 不适用 |

### 前端（若本次含前端变更）
| 硬规则 | 依据 | 核对结论 |
| --- | --- | --- |
| 目录/命名/拦截器落点符合规范 | frontend-develop | 符合 / 违反(P1) / 不适用 |
| 新端点已补 mock 三件套，且守卫与后端逐道一致 | frontend §5.4 | 符合 / 违反(P1) / 不适用 |
| 连锁字段"改一路改全"（六环无遗漏） | frontend §5.6 | 符合 / 违反(P1) / 不适用 |
| 状态/分类标签用有限语义色，分类用中性色 | frontend §4.3 | 符合 / 违反(P1) / 不适用 |
| 界面变更符合 UI 策略 | ui-design-strategy.md | 符合 / 违反(P1) / 不适用 |

## 4. 行为风险

边界条件、错误处理、权限、安全、数据一致性、兼容性——逐项结论。

## 5. 自动化检查

| 检查 | 命令 | 结果 |
| --- | --- | --- |
| lint / format / build / typecheck | 待填写（项目声明的命令） | pass/fail/未执行(原因) |

## 6. Findings（按 P0 → P1 → P2）

| 等级 | 文件:行 | 问题 | 影响 | 建议 |
| --- | --- | --- | --- | --- |
| P0/P1/P2 | path:line | 待填写 | 待填写 | 待填写 |

> 无发现时明确写「未发现 P0/P1/P2 问题」，并列残余风险或未执行的检查。

## 7. 阶段交接包

```yaml
handoff:
  reqId: {req-id}
  phase: 4
  phaseName: code-review
  gateStatus: pass | fail
  nextRecommendedSkill: test-runner | coding
  userConfirmationRequired: false   # 因 P0 回退累计 >= 2 次时置 true
  retryCount: 0
  artifacts:
    - docs/reports/code-review/{req-id}-code-review.md
  verification:
    executed: []      # 实际运行的自动化检查
    degraded: false   # 有未执行检查时置 true
    note: ""
  blockers: []
  assumptions: []
```
