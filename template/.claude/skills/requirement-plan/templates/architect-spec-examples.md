# 规范文档示例 (agent-workflow.md)

> 本文件提供 `docs/standards/agent-workflow.md` 的完整示例，供新项目参考。

---

## 示例 1：Node.js + Vue 全栈项目

```yaml
# docs/standards/agent-workflow.md

## 文档结构

requirements:
  directory: "docs/requirements"
  planFile: "{req-id}-plan.md"
  registryFile: "registry.md"
  modules:
    directory: "docs/modules"
    statusFile: "status.md"

## 代码规范

code:
  directories:
    src: "src"
    tests: "tests"
    docs: "docs"
  
  naming:
    modules: "kebab-case"
    classes: "PascalCase"
    functions: "camelCase"
    constants: "UPPER_SNAKE_CASE"
  
  templates:
    controller: "templates/controller.ts"
    service: "templates/service.ts"
    repository: "templates/repository.ts"
    component: "templates/vue-component.vue"

## 技术栈

techStack:
  frontend: "Vue 3 + TypeScript + Vite"
  backend: "Node.js 20 + TypeScript + NestJS"
  database: "PostgreSQL 15"
  cache: "Redis 7"
  messageQueue: "RabbitMQ"

## 工作流程

workflow:
  requirement:
    steps:
      - name: "需求分析"
        agent: "architect"
        output: "Plan.md 草案"
      - name: "需求确认"
        agent: "user"
        output: "确认的 Plan.md"
      - name: "任务拆解"
        agent: "architect"
        output: "模块子文档"
      - name: "开发实现"
        agent: "architect"
        output: "代码 + 测试"
      - name: "代码审查"
        agent: "architect"
        output: "审查报告"
      - name: "部署上线"
        agent: "architect"
        output: "生产环境"

## Git 工作流

git:
  branchNaming: "feature/{req-id}"
  commitConvention: "conventional"
  worktree:
    enabled: true
    directory: ".openclaw/worktrees"

## 特殊规则

moduleDependencies:
  content-management:
    dependsOn: ["platform-accounts"]
  platform-accounts:
    dependsOn: []

compliance:
  payment:
    requiresReview: true
    reviewers: ["security-team"]
```

---

## 示例 2：.NET + Angular 企业项目

```yaml
# docs/standards/agent-workflow.md

requirements:
  directory: "docs/requirements"
  planFile: "{req-id}-plan.md"
  registryFile: "registry.md"
  modules:
    directory: "docs/modules"
    statusFile: "status.md"

code:
  directories:
    src: "src"
    tests: "tests"
    docs: "docs"
  
  naming:
    modules: "kebab-case"
    classes: "PascalCase"
    functions: "camelCase"
    constants: "UPPER_SNAKE_CASE"
  
  templates:
    controller: "templates/Controller.cs"
    service: "templates/Service.cs"
    component: "templates/angular-component.ts"

techStack:
  frontend: "Angular 17 + TypeScript"
  backend: ".NET 8 + C#"
  database: "SQL Server 2022"
  cache: "Redis 7"
  messageQueue: "Azure Service Bus"

workflow:
  requirement:
    steps:
      - name: "需求分析"
        agent: "architect"
      - name: "架构评审"
        agent: "architect"
      - name: "需求确认"
        agent: "user"
      - name: "开发实现"
        agent: "architect"
      - name: "代码审查"
        agent: "architect"
      - name: "集成测试"
        agent: "architect"
      - name: "部署上线"
        agent: "architect"

git:
  branchNaming: "feature/{req-id}"
  commitConvention: "conventional"
  worktree:
    enabled: true
    directory: ".openclaw/worktrees"
```

---

## 示例 3：Python 数据科学项目

```yaml
# docs/standards/agent-workflow.md

requirements:
  directory: "docs/requirements"
  planFile: "{req-id}-plan.md"
  registryFile: "registry.md"
  modules:
    directory: "docs/modules"
    statusFile: "status.md"

code:
  directories:
    src: "src"
    tests: "tests"
    docs: "docs"
  
  naming:
    modules: "snake_case"
    classes: "PascalCase"
    functions: "snake_case"
    constants: "UPPER_SNAKE_CASE"
  
  templates:
    pipeline: "templates/pipeline.py"
    model: "templates/model.py"

techStack:
  language: "Python 3.11"
  dataProcessing: "Pandas + Polars"
  machineLearning: "Scikit-learn + XGBoost"
  database: "PostgreSQL 15"
  visualization: "Plotly + Dash"

workflow:
  requirement:
    steps:
      - name: "需求分析"
        agent: "architect"
      - name: "数据探索"
        agent: "architect"
      - name: "模型开发"
        agent: "architect"
      - name: "模型验证"
        agent: "architect"
      - name: "部署上线"
        agent: "architect"

git:
  branchNaming: "feature/{req-id}"
  commitConvention: "conventional"
  worktree:
    enabled: false
```

---

## 示例 4：最小化配置（仅核心字段）

```yaml
# docs/standards/agent-workflow.md

# 仅定义核心字段，其他使用默认值

requirements:
  directory: "docs/requirements"
  planFile: "{req-id}-plan.md"

code:
  naming:
    modules: "kebab-case"
    classes: "PascalCase"

techStack:
  frontend: "React 18"
  backend: "Node.js 20"
```

---

## 使用建议

### 新项目

1. **复制示例 1 或示例 2**（根据技术栈）
2. **调整技术栈字段**
3. **按需添加特殊规则**
4. **提交到版本控制**

### 现有项目

1. **从最小化配置开始**（示例 4）
2. **逐步补充规范**
3. **确保团队共识**

### 多项目组织

- **每个项目独立规范**：不要共享 `agent-workflow.md`
- **规范即文档**：新人通过规范了解项目结构
- **规范即配置**：通用 Skill 自动适配

---

## 验证规范

```bash
# 检查规范文档是否存在
test -f docs/standards/agent-workflow.md && echo "✅ 规范文档存在" || echo "❌ 规范文档缺失"

# 验证 YAML 语法
node -e "require('yaml').parse(require('fs').readFileSync('docs/standards/agent-workflow.md', 'utf8'))" && echo "✅ YAML 语法正确"
```

---

*最后更新：2026-06-07*
*维护：通用架构规范*
