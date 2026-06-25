# 重构迁移指南

> 从「项目级 Skill 为主」迁移到「通用 Skill + 规范文档」架构

---

## 🎯 重构目标

**旧架构**：
- 通用 Skill 定义流程
- 项目级 Skill 实现具体逻辑
- 每个项目需要维护项目级 Skill 代码

**新架构**：
- 通用 Skill 定义流程 + 实现逻辑
- 项目通过 `docs/standards/agent-workflow.md` 声明规范
- 通用 Skill 自动读取规范并适配
- 项目级 Skill 仅用于特殊扩展点

---

## 📋 迁移步骤

### 步骤 1：创建规范文档

**推荐位置**：`docs/standards/agent-workflow.md`

```bash
# 创建文档目录（如不存在）
mkdir -p docs/standards

# 复制模板
cp ~/.openclaw/shared-skills/templates/agent-workflow-template.md docs/standards/agent-workflow.md
```

**备选位置**：`docs/README.md`

**说明**：
- ✅ 优先使用 `docs/standards/agent-workflow.md`（符合项目规范体系）
- ✅ 备选使用 `docs/README.md`（如果项目已有此文件）
- ❌ 不再使用旧的分散项目级 Skill 作为唯一规范来源

**编辑 `docs/standards/agent-workflow.md`**：
1. 填写技术栈
2. 确认目录结构
3. 定义命名规范
4. （可选）添加特殊规则

---

### 步骤 2：验证规范文档

```bash
# 检查文件存在
test -f docs/standards/agent-workflow.md && echo "✅ 规范文档已创建"

# 验证 YAML 语法（需要 js-yaml）
node -e "require('js-yaml').load(require('fs').readFileSync('docs/standards/agent-workflow.md', 'utf8'))"
```

---

### 步骤 3：更新环境变量

**旧配置**：
```bash
export BAIYING_PROJECT_ROOT=/root/ai-workspace/projects/baiying
```

**新配置**：
```bash
export PROJECT_ROOT=/root/ai-workspace/projects/baiying
```

**说明**：
- 使用通用环境变量 `PROJECT_ROOT`
- 不再使用项目特定前缀（如 `BAIYING_`）

---

### 步骤 4：迁移项目级 Skill（如需要）

#### 场景 A：项目级 Skill 仅定义代码模板

**旧方式**：
```
projects/baiying/skills/project-codegen/
├── SKILL.md
└── templates/
    ├── controller.ts
    └── service.ts
```

**新方式**：
1. 将模板移到项目根目录：
   ```bash
   mv skills/project-codegen/templates/ templates/
   ```

2. 在 `agent-workflow.md` 中声明：
   ```yaml
   code:
     templates:
       controller: "project-template-files/controller.ts"
       service: "project-template-files/service.ts"
   ```

3. 删除项目级 Skill：
   ```bash
   rm -rf skills/project-codegen
   ```

#### 场景 B：项目级 Skill 有自定义逻辑

**保留条件**（满足任一即保留）：
- 调用项目特有的内部 API
- 自定义合规/安全验证
- 复杂工作流编排（多系统联动）

**迁移方式**：
1. 将自定义逻辑封装为独立脚本
2. 在 `agent-workflow.md` 中声明调用方式
3. 通用 Skill 通过 `exec` 调用脚本

---

### 步骤 5：更新任务上下文

**旧任务上下文**：
```markdown
# Task Context: REQ-XXX

- **项目路径**: /root/ai-workspace/projects/baiying
- **项目名称**: baiying
```

**新任务上下文**（格式不变）：
```markdown
# Task Context: REQ-XXX

- **项目路径**: /root/ai-workspace/projects/baiying
- **项目名称**: baiying
```

**说明**：任务上下文格式不变，但通用 Skill 会：
1. 从任务上下文读取 `projectRoot`
2. 加载 `{projectRoot}/docs/standards/agent-workflow.md`
3. 按规范执行任务

---

### 步骤 6：验证迁移

#### 测试需求解析

```bash
# 旧方式（可能失败）
node ~/.openclaw/shared-skills/requirement-parser/scripts/parse.js "测试需求"

# 新方式（推荐）
node ~/.openclaw/shared-skills/requirement-parser/scripts/parse.js \
  '{"message":"测试需求","projectRoot":"/root/ai-workspace/projects/baiying"}'
```

**预期结果**：
- ✅ Plan.md 按规范路径生成
- ✅ 规范文档被正确加载
- ✅ 无项目级 Skill 调用错误

#### 测试代码生成

```bash
# 触发代码生成
# （通过 Agent 对话或命令行）

# 检查生成的文件结构
tree src/
tree tests/
```

**预期结果**：
- ✅ 目录结构符合规范
- ✅ 文件命名符合规范
- ✅ 使用了项目模板（如有配置）

---

## 🔍 常见问题

### Q1: 规范文档应该包含哪些内容？

**A**: 核心字段：
```yaml
# docs/standards/agent-workflow.md
requirements:
  directory: "docs/requirements"
  planFile: "{req-id}-plan.md"

code:
  naming:
    modules: "kebab-case"
    classes: "PascalCase"

techStack:
  frontend: "Vue 3"
  backend: "Node.js 20"
```

其他字段可选，使用默认值。

---

### Q2: 规范文档位置在哪里？

**A**: 
- ✅ **推荐**：`docs/standards/agent-workflow.md`
- ✅ **备选**：`docs/README.md`
- ❌ **不再使用**：旧的分散项目级 Skill 作为唯一规范来源

---

### Q3: 没有规范文档会怎样？

**A**: 通用 Skill 使用内置默认值：
```yaml
defaults:
  requirements:
    directory: "docs/requirements"
    planFile: "{req-id}-plan.md"
  
  code:
    naming:
      modules: "kebab-case"
      classes: "PascalCase"
```

**建议**：即使使用默认值，也创建规范文档作为项目文档。

---

### Q3: 项目级 Skill 必须全部删除吗？

**A**: 否。以下情况保留：
- 调用内部 API/工具
- 自定义合规验证
- 复杂工作流编排

**原则**：能声明的进文档，不能声明的进代码。

---

### Q4: 多项目如何管理？

**A**: 每个项目独立规范：
```
projects/
├── project-a/
│   └── docs/standards/agent-workflow.md  # 项目 A 规范
├── project-b/
│   └── docs/standards/agent-workflow.md  # 项目 B 规范
└── project-c/
    └── docs/standards/agent-workflow.md  # 项目 C 规范
```

通用 Skill 通过任务上下文区分项目。

---

### Q5: 规范变更如何同步团队？

**A**: 
1. 更新 `docs/standards/agent-workflow.md`
2. Git commit 并推送
3. 通知团队成员
4. 通用 Skill 自动适配新规范

---

## 📊 迁移检查清单

- [ ] 创建 `docs/standards/agent-workflow.md`
- [ ] 验证 YAML 语法
- [ ] 更新环境变量为 `PROJECT_ROOT`
- [ ] 迁移项目级 Skill 模板（如有）
- [ ] 测试需求解析
- [ ] 测试代码生成
- [ ] 更新团队文档
- [ ] 删除旧项目级 Skill（如适用）

---

## 📚 相关文档

- [AGENT-WORKFLOW-TEMPLATE.md](baiying:shared-skills/template-files/AGENT-WORKFLOW-TEMPLATE.md) - 规范文档模板
- [AGENT-WORKFLOW-EXAMPLES.md](baiying:shared-skills/template-files/AGENT-WORKFLOW-EXAMPLES.md) - 规范文档示例
- [SPEC-LOADER.md](baiying:shared-skills/lib/spec-loader.js) - 规范文档加载器

---

*最后更新：2026-06-07*
*维护：通用架构规范*







