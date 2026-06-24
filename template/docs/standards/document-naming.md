# 文档路径命名规范

## 1. 目录命名

- 使用小写 kebab-case：`code-standard`、`quick-start`。
- 不使用数字前缀排序。
- 不使用项目名作为模板目录名。

## 2. Markdown 文件命名

- 使用小写 kebab-case：`api-standard.md`。
- 需求 Plan 使用 `{req-id}-plan.md`。
- 模块固定文档名：`design.md`、`api.md`、`plan.md`、`test-plan.md`、`status.md`。

## 3. 需求编号

```text
REQ-YYYYMMDD-NNN
```

文件名使用小写：

```text
req-yyyymmdd-nnn-plan.md
```

## 4. 禁止项

- 禁止在模板文档中写入真实客户名、真实域名、密钥或内部账号。
- 禁止把一次性报告放入长期规范目录。
- 禁止同一规则在多个文档中重复维护。

## 5. 文档归属

| 内容 | 推荐位置 |
| --- | --- |
| 长期规范 | `docs/standards/` |
| 单需求方案 | `docs/requirements/` |
| 模块设计/API/测试 | `docs/modules/{module}/` |
| 部署流程 | `docs/deploy/` |
| 审查结果 | `docs/reviews/` |
| 外部资料索引 | `docs/reference/` |
