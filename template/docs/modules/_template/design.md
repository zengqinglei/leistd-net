# {ModuleName} - 设计文档

## 1. 模块职责

### 1.1 核心职责

- {职责 1}
- {职责 2}

### 1.2 职责边界

- 本模块负责：{scope}
- 本模块不负责：{out-of-scope}
- 上游依赖：{upstream}
- 下游影响：{downstream}

## 2. 业务流程

```text
{actor} -> {step-1} -> {step-2} -> {result}
```

## 3. 领域模型

| 对象 | 类型 | 说明 | 关键字段 |
| --- | --- | --- | --- |
| `{Entity}` | Entity/Aggregate/ValueObject | `{说明}` | `{fields}` |

## 4. 状态流转

| 当前状态 | 事件/动作 | 下一状态 | 说明 |
| --- | --- | --- | --- |
| `{State}` | `{Event}` | `{NextState}` | `{说明}` |

## 5. 接口与事件

| 类型 | 名称 | 方向 | 说明 |
| --- | --- | --- | --- |
| API | `{api}` | inbound/outbound | `{说明}` |
| Event | `{event}` | publish/subscribe | `{说明}` |

## 6. 权限与安全

- 认证要求：{authentication}
- 授权规则：{authorization}
- 敏感数据：{sensitive-data}
- 审计要求：{audit}

## 7. 关键决策

| 决策 | 原因 | 影响 | 替代方案 |
| --- | --- | --- | --- |
| `{Decision}` | `{Reason}` | `{Impact}` | `{Alternative}` |

## 8. 风险

| 风险 | 影响 | 缓解措施 |
| --- | --- | --- |
| `{risk}` | `{impact}` | `{mitigation}` |
