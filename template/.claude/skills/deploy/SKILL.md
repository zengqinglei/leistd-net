---
name: deploy
description: |
  部署应用、健康检查、日志查看、服务状态检查和回滚的通用流程。

  **当以下情况时使用此 Skill**:
  (1) 用户要求部署、发布新版本或重启服务
  (2) 用户要求健康检查、查看状态、查看日志
  (3) 用户要求回滚到上一版本
  (4) 测试和代码审查通过后需要进入部署阶段

  **调用 Agent**:架构师助手(architect)
metadata:
  openclaw:
    requires: ["docker"]
    skillKey: "deploy"
user-invocable: true
disable-model-invocation: false
---

# 部署应用 (deploy)

> 生产部署是高风险动作；默认先准备方案，只有获得用户显式确认后才执行变更。

## 执行前必读

- **先读项目配置**：优先读取 `docs/standards/agent-workflow.md` 的部署索引，再读取 `docs/deploy/`。
- **显式确认**：生产部署、回滚、SSH、数据库迁移、密钥/环境变量变更必须先获得用户确认。
- **预检查**：部署前确认测试报告通过、`code-review` 无 P0、工作区状态符合项目发布规范。
- **沉淀报告**：部署完成后写入 `docs/reports/deploy/{req-id}-deploy-report.md`。
- **不猜配置**：缺少服务器、端口、环境变量或回滚方案时，只输出部署方案并等待确认。
- **不泄露密钥**：不得写入或打印密码、Token、证书私钥、SSH 私钥。

## 动作类型

| 动作 | 说明 |
|------|------|
| `deploy` | 预检查、构建/拉取镜像、备份、重启、健康检查、报告 |
| `health-check` | 只做健康检查、日志检查和核心路径验证 |
| `logs` | 查看服务日志，注意脱敏 |
| `status` | 查看容器、进程或服务状态 |
| `rollback` | 回滚到备份配置或上一版本，必须确认 |
| `config-check` | 仅验证部署配置 |

默认动作：`deploy`。

## 安全边界

**禁止**（除非获得用户明确授权）：
- `rm -rf`、`docker system prune`、`docker volume rm`、`docker compose down -v`
- `git reset --hard`、`git clean -fd`
- 修改 Nginx、Dockerfile、数据库 volume、生产端口
- 覆盖服务器源码或通过 SSH 执行远程变更命令

**必须**：
- 执行前说明目标环境、服务器、命令、影响范围和回滚方式。
- 修改 compose、Nginx 或部署配置前先备份。
- 失败时停止继续操作，报告原因；需要回滚时先确认或按已确认方案执行。

## 工作流程

1. **识别动作**：部署 / 健康检查 / 日志 / 状态 / 回滚 / 配置检查。
2. **读取配置**：`docs/standards/agent-workflow.md` → `docs/deploy/` → 项目根部署配置。
3. **预检查**：工作区状态、测试报告、代码审查、Must have 验收覆盖。
4. **确认计划**：生产变更前列出命令、目标、风险和回滚方案，等待确认。
5. **执行动作**：逐步执行，每步失败即停止并汇报。
6. **验证报告**：健康检查、日志检查、核心功能验证、验收闭环对照。

详细执行顺序、回滚和报告模板见 `references/deploy-workflow.md`。

## 配置缺失降级

如无部署配置：
- 使用 `templates/server-config-template.md` 和 `templates/docker-compose-template.yml` 准备方案。
- 提示用户创建 `docs/deploy/server-config.md`、`docker-compose.yml` 或 `compose.yml`。
- 不直接部署，等待用户确认目标环境、服务器、命令和回滚方案。

完整降级提示见 `references/fallback-strategy.md`。

## 参考资源

| 资源 | 路径 | 用途 |
|------|------|------|
| Docker Compose 模板 | `templates/docker-compose-template.yml` | 创建项目级部署配置参考 |
| 服务器配置模板 | `templates/server-config-template.md` | 创建 `docs/deploy/server-config.md` 参考 |
| 部署工作流参考 | `references/deploy-workflow.md` | 执行、回滚、报告细节 |
| 降级策略 | `references/fallback-strategy.md` | 无配置时处理 |

## 完成衔接

| 部署结果 | 下一动作 |
|----------|----------|
| 成功 + 健康检查通过 | 汇报版本、URL、验收说明 |
| 失败 + 已回滚 | 报告失败原因，等待决策 |
| 失败 + 回滚失败 | 紧急阻塞，立即通知用户人工介入 |

---

*最后更新:2026-06-25 高风险确认版 v3.0.0*
*维护:通用开发助手*

