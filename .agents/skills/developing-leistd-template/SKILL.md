---
name: developing-leistd-template
description: 在 leistd-net 仓库中修改或审查 template 的后端、前端、条件参数、生成项目文档、项目 Skill、Mock 或部署资产时使用，也用于适配 Leistd 公共 API 并验证生成场景。不能替代框架组件开发；生成后的业务功能开发应使用项目内 Skill。
---

# 开发 Fullstack App Template

## 建立事实

先读取：

1. `docs/architecture/design-principles.md` 和 `docs/template/development-guide.md`。
2. `template/.template.config/template.json`、受影响模板源码和邻近条件块。
3. 涉及框架时读取当前源码、随包文档和 `.tmp/local-feed` 中实际包。
4. 修改生成项目 Skill 时同时使用官方 `skill-creator`。

模板通过 `PackageReference` 消费框架；`Api` 是组合根，Application 不依赖 Infrastructure。模板源可编辑不等于交付完成，必须验证实际生成结果。

## 工作流

1. 检查分支、工作区和用户已有改动，列出受影响参数、条件组合、替换项和排除文件。
2. 沿用最新同类模板实现，修改源码、测试和确有长期价值的文档。
3. 涉及框架契约时先打包到 `.tmp/local-feed`，再走 NuGet 消费路径。
4. 在 `.tmp/` 下生成受影响场景，检查残留占位符和条件标记。
5. 对生成项目执行后端还原、构建、测试及必要的前端构建。
6. 权限、审计、通知、实时或数据库变化时补充对应业务闭环验证。

模板维护规则写入 `docs/template/`。项目协作流程写入 `template/.agents/skills/leistd-project-workflow/`，生成项目的长期工程事实由 `template/docs/README.md` 索引；满足沉淀条件但没有同类文档时主动创建最小权威文档，只写已验证且必要的信息，不携带固定需求、报告、配置或规范模板。

## 验证入口

```powershell
pwsh scripts/validate-skills.ps1
pwsh scripts/test-template-matrix.ps1 -Scenarios default
```

参数裁剪或共享资产变化使用完整矩阵。未执行的条件场景和风险必须说明。
