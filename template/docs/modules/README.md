# 模块文档说明

每个业务模块应在 `docs/modules/{module-name}/` 下维护固定文档集合。

## 1. 标准结构

```text
docs/modules/{module-name}/
├── design.md
├── api.md
├── plan.md
├── test-plan.md
└── status.md
```

## 2. 创建新模块

复制模板目录：

```text
docs/modules/_template/
```

并替换：

- `{ModuleName}`
- `{ModuleKey}`
- `{Owner}`
- `{RelatedRequirement}`

## 3. 文档边界

| 文件 | 内容 |
| --- | --- |
| `design.md` | 模块职责、边界、模型、流程、关键决策 |
| `api.md` | API 契约、请求响应、错误码、权限 |
| `plan.md` | 模块阶段计划、依赖、里程碑 |
| `test-plan.md` | 单元、集成、E2E、手工验证计划 |
| `status.md` | 当前状态、进度、阻塞、下一步 |
