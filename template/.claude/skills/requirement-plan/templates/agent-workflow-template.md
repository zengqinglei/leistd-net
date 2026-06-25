# Agent 工作流规范 (agent-workflow.md)

> 本文件定义项目的 Agent 协作工作流、文档结构、命名规范等约定。
> 通用 Skill 启动时会读取本文件，按项目规范执行任务。

**位置**：`docs/standards/agent-workflow.md`

**设计原则**：
- ✅ 符合参考项目结构（参考项目的 `docs/standards/` 目录）
- ✅ 与 README.md 分离：README 面向人类，本文件面向 Agent
- ✅ 纳入版本控制，团队共同维护

---

## 项目文档索引

> 通用 Skill 先读取本文件，再按索引读取当前任务需要的项目级文档；不要默认读取全部文档。

### 需求文档

```yaml
# 需求目录路径（相对于项目根目录）
requirements:
  directory: "docs/requirements"           # 需求文档存放目录
  planFile: "{req-id}-plan.md"             # Plan.md 命名模板
  registryFile: "registry.md"              # 需求登记册文件名
  contextDirectory: "docs/requirements/context" # 任务上下文目录
  modules:
    directory: "docs/modules"              # 模块文档目录
```

### 规范文档

```yaml
standards:
  techStack: "docs/standards/tech-stack.md"
  backendCode: "docs/standards/code-standard/backend-develop.md"
  frontendCode: "docs/standards/code-standard/frontend-develop.md"
  test: "docs/standards/test.md"
  documentNaming: "docs/standards/document-naming.md"
```

### 报告文档

```yaml
reports:
  development: "docs/reports/development/{req-id}-dev-report.md"
  codeReview: "docs/reports/code-review/{req-id}-code-review.md"
  tests: "docs/reports/tests/{req-id}-test-report.md"
  deploy: "docs/reports/deploy/{req-id}-deploy-report.md"
  acceptance: "docs/reports/acceptance/{req-id}-acceptance.md"
```

### 技术栈

```yaml
techStack:
  frontend: "按项目填写"                   # 前端技术栈
  backend: "按项目填写"                    # 后端技术栈
  database: "按项目填写"                   # 数据库
  cache: "按项目填写"                      # 缓存
  messageQueue: "按项目填写"               # 消息队列
  commands:
    build: "按项目填写"
    lint: "按项目填写"
    test: "按项目填写"
```

### 部署文档

```yaml
deploy:
  directory: "docs/deploy"
  serverConfig: "docs/deploy/server-config.md"
  releasePolicy: "docs/deploy/release-policy.md"
```

---

## 工作流程

### 阶段状态机

| Phase | 阶段 | 主 Skill | 输入 | 必须产物 | 完成条件 | 下一阶段 |
|---|---|---|---|---|---|---|
| 0 | 需求进入 | `requirement-plan` | 用户想法/背景/需求 | `docs/requirements/{req-id}-plan.md` | Plan 草案生成并等待用户确认 | 1 |
| 1 | 需求登记 | `task-manager` | 确认后的 Plan.md | `registry.md` + `context/{req-id}.md` | 任务 ID、registry、context 已创建或更新 | 2 |
| 2 | 任务拆解 | `task-manager` | Plan.md + context | 子任务清单 + Must have 映射 | 子任务清单已确认或简单任务自动通过 | 3 |
| 3 | 开发自测 | `coding` | Plan.md + 子任务清单 | `docs/reports/development/{req-id}-dev-report.md` | 代码完成，最小验证通过或明确阻塞 | 4 |
| 4 | 代码审查 | `code-review` | 代码变更 + Plan.md + dev report | `docs/reports/code-review/{req-id}-code-review.md` | 无 P0 问题 | 5 |
| 5 | 测试验证 | `test-runner` | Plan.md + 测试配置 + review report | `docs/reports/tests/{req-id}-test-report.md` | 测试通过，Must have 覆盖已标记 | 6 |
| 6 | 部署 | `deploy` | 部署配置 + test report + review report | `docs/reports/deploy/{req-id}-deploy-report.md` | 健康检查通过，部署报告生成 | 7 |
| 7 | 验收收口 | `task-manager` | Plan.md + 所有阶段报告 | `docs/reports/acceptance/{req-id}-acceptance.md` | registry/context 更新为 completed 或 blocked | Done |

### 需求开发流程

```yaml
workflow:
  requirement:
    steps:
      - name: "需求分析"
        agent: "architect"
        skill: "requirement-plan"
        output: "Plan.md 草案"
      - name: "需求确认"
        agent: "user"
        output: "确认的 Plan.md"
      - name: "任务拆解"
        agent: "architect"
        skill: "task-manager"
        output: "registry + context + 子任务清单"
      - name: "开发实现"
        agent: "architect"
        skill: "coding"
        output: "代码 + dev report"
      - name: "代码审查"
        agent: "architect"
        skill: "code-review"
        output: "审查报告"
      - name: "测试验证"
        agent: "architect"
        skill: "test-runner"
        output: "测试报告"
      - name: "部署上线"
        agent: "architect"
        skill: "deploy"
      - name: "验收收口"
        agent: "architect"
        skill: "task-manager"
        output: "部署报告"
      - name: "验收收口"
        agent: "architect"
        skill: "task-manager"
        output: "验收报告 + registry/context 更新"
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
    contextDirectory: "docs/requirements/context"
  reports:
    development: "docs/reports/development/{req-id}-dev-report.md"
    codeReview: "docs/reports/code-review/{req-id}-code-review.md"
    tests: "docs/reports/tests/{req-id}-test-report.md"
    deploy: "docs/reports/deploy/{req-id}-deploy-report.md"
    acceptance: "docs/reports/acceptance/{req-id}-acceptance.md"
    modules:
      directory: "docs/modules"
  
  standards:
    techStack: "docs/standards/tech-stack.md"
    backendCode: "docs/standards/code-standard/backend-develop.md"
    frontendCode: "docs/standards/code-standard/frontend-develop.md"
    test: "docs/standards/test.md"
  
  git:
    branchNaming: "feature/{req-id}"
    commitConvention: "conventional"
    worktree:
      enabled: true
      directory: ".openclaw/worktrees"
```

---

## Skill 清单

| Skill 名称 | 职责 | 调用时机 |
|-----------|------|---------|
| `requirement-plan` | 需求分析 → Plan.md | 用户提出新需求 |
| `task-manager` | 任务管理、进度跟踪 | 查询进度、更新状态 |
| `coding` | 代码开发、最小验证 | Plan.md 确认后 |
| `code-review` | 代码规范检查 | 代码提交前 |
| `deploy` | 部署上线 | 测试通过且用户确认后 |
| `test-runner` | 运行测试套件 | 代码开发完成后 |

---

## 文件示例

### 完整示例

```yaml
# docs/standards/agent-workflow.md

requirements:
  directory: "docs/requirements"
  planFile: "{req-id}-plan.md"
  registryFile: "registry.md"
  contextDirectory: "docs/requirements/context"
  modules:
    directory: "docs/modules"

reports:
  development: "docs/reports/development/{req-id}-dev-report.md"
  codeReview: "docs/reports/code-review/{req-id}-code-review.md"
  tests: "docs/reports/tests/{req-id}-test-report.md"
  deploy: "docs/reports/deploy/{req-id}-deploy-report.md"
  acceptance: "docs/reports/acceptance/{req-id}-acceptance.md"

standards:
  techStack: "docs/standards/tech-stack.md"
  backendCode: "docs/standards/code-standard/backend-develop.md"
  frontendCode: "docs/standards/code-standard/frontend-develop.md"
  test: "docs/standards/test.md"

techStack:
  frontend: "按项目填写"
  backend: "按项目填写"
  database: "按项目填写"
  commands:
    build: "按项目填写"
    lint: "按项目填写"
    test: "按项目填写"

deploy:
  directory: "docs/deploy"
  serverConfig: "docs/deploy/server-config.md"
  releasePolicy: "docs/deploy/release-policy.md"

workflow:
  requirement:
    steps:
      - name: "需求分析"
        agent: "architect"
        skill: "requirement-plan"
      - name: "需求确认"
        agent: "user"
      - name: "开发实现"
        agent: "architect"
        skill: "coding"
      - name: "代码审查"
        agent: "architect"
        skill: "code-review"
      - name: "测试验证"
        agent: "architect"
        skill: "test-runner"
      - name: "部署上线"
        agent: "architect"
        skill: "deploy"

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
- **本文件应纳入版本控制**

---

*最后更新：模板版本*
*维护：通用架构规范*

