# Agent 工作流规范 (agent-workflow.md)

> 本文件定义项目的文档结构、命名规范、工作流程等协作约定。
> 通用 Skill 启动时会读取本文件，按项目规范执行任务。

---

## 文档结构

### 需求文档

```yaml
# 需求目录路径（相对于项目根目录）
requirements:
  directory: "docs/requirements"           # 需求文档存放目录
  planFile: "{req-id}-plan.md"             # Plan.md 命名模板
  registryFile: "registry.md"              # 需求登记册文件名
  modules:
    directory: "docs/modules"              # 模块文档目录
```

### 代码规范

```yaml
code:
  # 代码目录结构
  directories:
    src: "src"                             # 源代码目录
    tests: "tests"                         # 测试代码目录
    docs: "docs"                           # 项目文档目录
  
  # 命名规范
  naming:
    modules: "kebab-case"                  # 模块名：小写 + 连字符
    classes: "PascalCase"                  # 类名：大驼峰
    functions: "camelCase"                 # 函数名：小驼峰
    constants: "UPPER_SNAKE_CASE"          # 常量：大写 + 下划线
  
  # 代码模板（可选，项目级 Skill 使用）
  templates:
    controller: "project-template-files/controller.ts"
    service: "project-template-files/service.ts"
    repository: "project-template-files/repository.ts"
```

### 技术栈

```yaml
techStack:
  frontend: "Vue 3 + TypeScript"           # 前端技术栈
  backend: "Node.js 20 + TypeScript"       # 后端技术栈
  database: "PostgreSQL 15"                # 数据库
  cache: "Redis 7"                         # 缓存
  messageQueue: "RabbitMQ"                 # 消息队列
```

---

## 工作流程

### 需求开发流程

```yaml
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
```

### Git 工作流

```yaml
git:
  branchNaming: "feature/{req-id}"         # 分支命名模板
  commitConvention: "conventional"         # Commit 规范
  worktree:
    enabled: true                          # 是否启用 worktree
    directory: ".openclaw/worktrees"       # worktree 存放目录
```

---

## 特殊规则

### 模块依赖

```yaml
moduleDependencies:
  # 定义模块间的依赖关系（可选）
  content-management:
    dependsOn: ["platform-accounts"]
  platform-accounts:
    dependsOn: []
```

### 合规要求

```yaml
compliance:
  # 特殊合规要求（可选）
  payment:
    requiresReview: true                   # 支付模块需要额外评审
    reviewers: ["security-team"]
  data-export:
    requiresApproval: true                 # 数据导出需要审批
```

---

## 默认值

**如本文件不存在，通用 Skill 使用以下默认值：**

```yaml
defaults:
  requirements:
    directory: "docs/requirements"
    planFile: "{req-id}-plan.md"
    registryFile: "registry.md"
    modules:
      directory: "docs/modules"
  
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
  
  git:
    branchNaming: "feature/{req-id}"
    commitConvention: "conventional"
    worktree:
      enabled: true
      directory: ".openclaw/worktrees"
```

---

## 文件示例

### 完整示例

```yaml
# docs/standards/agent-workflow.md

requirements:
  directory: "docs/requirements"
  planFile: "{req-id}-plan.md"
  registryFile: "registry.md"
  modules:
    directory: "docs/modules"

code:
  directories:
    src: "src"
    tests: "tests"
    docs: "docs"
  naming:
    modules: "kebab-case"
    classes: "PascalCase"
    functions: "camelCase"
  templates:
    controller: "project-template-files/controller.ts"
    service: "project-template-files/service.ts"

techStack:
  frontend: "Vue 3 + TypeScript"
  backend: "Node.js 20 + TypeScript"
  database: "PostgreSQL 15"

workflow:
  requirement:
    steps:
      - name: "需求分析"
        agent: "architect"
      - name: "需求确认"
        agent: "user"
      - name: "开发实现"
        agent: "architect"
      - name: "代码审查"
        agent: "architect"

git:
  branchNaming: "feature/{req-id}"
  commitConvention: "conventional"
  worktree:
    enabled: true
    directory: ".openclaw/worktrees"
```

---

## 维护说明

- **本文件由项目初创者或架构师维护**
- **规范变更时，更新本文件并通知团队**
- **通用 Skill 会自动读取本文件，无需额外配置**

---

*最后更新：模板版本*
*维护：通用架构规范*




