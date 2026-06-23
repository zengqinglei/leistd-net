---
name: deploy
description: |
  部署应用到生产环境 - 通用部署流程。

  **当以下情况时使用此 Skill**:
  (1) 用户要求"部署到生产环境"
  (2) PR 合并后自动部署
  (3) 用户要求"发布新版本"
  (4) 用户要求"重启服务"
  (5) 用户要求"健康检查" / "查看服务状态"
  (6) 用户要求"查看日志" / "回滚到上一版本"

  **调用 Agent**:架构师助手(architect)
metadata:
  openclaw:
    requires: ["docker"]
    skillKey: "deploy"
user-invocable: true
disable-model-invocation: false
---

# 部署应用 (deploy)

> 部署应用到生产环境

## 🚨 执行前必读

- ✅ **直接操作文件**:使用 `read/write/exec` 工具,不调用脚本
- ✅ **读取项目配置**:优先读取项目内的部署配置文件
- ⚠️ **无配置用默认**:无配置文件时使用标准 Docker Compose 部署
- ⚠️ **降级提示**:当项目无部署配置时,**必须提示用户**建议创建配置文件
- ✅ **预检查**:部署前必须通过测试和代码规范检查
- ✅ **健康检查**:部署后必须验证服务可用性

---

## 动作类型

| 动作 | 说明 |
|---|---|
| `deploy` | 标准部署:预检查、构建镜像、备份配置、重启、健康检查 |
| `health-check` | 只做健康检查和日志检查 |
| `logs` | 查看服务日志 |
| `status` | 查看容器和服务状态 |
| `rollback` | 回滚到备份配置或上一镜像 |
| `config-check` | 仅验证 Docker Compose 配置 |

默认动作:`deploy`

---

## 安全边界

**禁止**(除非获得用户明确授权):
- 写入密码、Token、证书私钥、SSH 私钥
- `rm -rf`、`docker system prune`、`docker volume rm`、`docker compose down -v`
- `git reset --hard`、`git clean -fd`
- 修改 Nginx、Dockerfile、数据库 volume、生产端口
- 未确认情况下覆盖服务器源码

**必须**:
- 修改 compose 或 Nginx 前先备份
- 生产部署前取得用户确认
- 更新镜像时使用 commit tag
- 失败时停止继续操作并汇报

---

## 📋 工作流程

```
1. 预检查
   → git status(工作区干净)
   → 确认 Phase 4 测试报告通过(读取 test-runner 报告)
   → 代码规范通过(调用 lint)
   → 验收闭环 Must have 已验证(如有 Plan.md)

2. 读取项目配置
   → 扫描 `docs/deploy/` 目录,读取所有部署相关文档
   → 如无配置,使用默认配置

3. 构建
   → 构建 Docker 镜像
   → 标记版本(git tag)

4. 部署
   → 备份配置
   → 更新配置
   → 重启服务

5. 验证
   → 健康检查
   → 日志检查
   → 功能验证

6. 报告
   → 成功:通知团队
   → 失败:回滚 + 通知
```

---

## 🎯 核心能力

### 1. 预检查

**检查项**:
```markdown
1. git status
   - 工作区必须干净
   - 无未提交变更

2. 测试通过
   - 调用 test-runner Skill
   - 所有测试必须通过

3. 代码规范
   - 调用 lint Skill
   - 无严重规范问题
```

**失败处理**:
```markdown
如预检查失败:
- 提示用户:"❌ 预检查失败,无法部署"
- 说明原因:测试失败/工作区不干净
- 建议修复后再部署
```

---

### 2. 读取项目配置

**扫描 `docs/deploy/` 目录**，读取所有部署相关文档。常见配置文件：

| 文件 | 用途 |
|------|------|
| `docker-compose.yml` | 服务定义、端口映射、环境变量、数据卷 |
| `server-config.md` | 服务器地址、SSH 用户、健康检查地址 |
| `.env.deploy` | 环境变量 |

**提取信息**：
```markdown
从配置文件中提取：
- 服务定义
- 端口映射
- 环境变量
- 数据卷
- 服务器地址（如有）
- SSH 用户（如有）
```

**降级处理**:
```markdown
如无配置文件:
1. 使用标准 Docker Compose 配置
2. **必须提示用户**:
   "⚠️ 检测到项目未配置部署文件,已使用标准 Docker Compose 配置。
   建议创建 docs/deploy/docker-compose.yml 以声明项目特定的部署配置。
   参考模板:templates/docker-compose-template.yml(本 Skill 提供)"
3. 继续执行,使用默认配置部署
```

---

### 3. 构建

**构建命令**:
```bash
# Docker 镜像构建
docker build -t {project-name}:{version} .

# 标记版本
git tag -a v{version} -m "Release {version}"
git push origin v{version}
```

**版本号生成**:
```markdown
格式:vYYYYMMDD.NN
示例:v20260607.01

生成规则:
- YYYYMMDD:当前日期
- NN:当日第 N 次发布
```

---

### 4. 部署

**Docker Compose 部署**:
```bash
# 备份现有配置
cp docker-compose.yml docker-compose.yml.bak

# 更新配置(如有)
# ...

# 重启服务
docker compose down
docker compose up -d
```

**SSH 部署**(如有服务器配置):
```bash
# 上传配置
scp docker-compose.yml user@server:/path/to/project/

# 远程执行
ssh user@server "cd /path/to/project && docker compose up -d"
```

---

### 5. 验证

**健康检查**:
```markdown
1. HTTP 检查
   - URL: http://localhost:{port}/health
   - 期望:200 OK
   - 超时:30 秒

2. 日志检查
   - 命令:docker compose logs --tail=100
   - 检查:无 ERROR 日志

3. 功能验证
   - 访问主页面
   - 验证核心功能
```

**失败处理**:
```markdown
如验证失败:
1. 查看日志:docker compose logs
2. 分析原因
3. 尝试修复
4. 如无法修复,回滚
```

---

### 6. 回滚

**回滚条件**:
- 健康检查失败
- 核心功能不可用
- 用户要求回滚

**回滚命令**:
```bash
# 恢复备份配置
mv docker-compose.yml.bak docker-compose.yml

# 重启服务
docker compose down
docker compose up -d
```

---

### 7. 报告

**成功报告**:
```markdown
✅ 部署成功

- 版本:v20260607.01
- 时间:2026-06-07 11:00:00
- 服务器:production.example.com
- 健康检查:通过
- 日志检查:通过

访问地址:https://production.example.com

## 验收闭环对照(如有 Plan.md)

| 验收项 | 状态 | 验证方式 | 证据 |
|--------|------|---------|------|
| 验收项 1 | ✅ | 健康检查 | HTTP 200 |
```

**失败报告**:
```markdown
❌ 部署失败,已回滚

- 版本:v20260607.01
- 时间:2026-06-07 11:00:00
- 失败原因:健康检查失败
- 回滚时间:2026-06-07 11:05:00

建议:
1. 查看日志:docker compose logs
2. 修复问题后重新部署
```

---

## 📚 参考模板(本 Skill 提供)

**相对路径引用**(从本 Skill 目录):

| 模板 | 路径 | 用途 |
|------|------|------|
| **Docker Compose 模板** | `templates/docker-compose-template.yml` | 标准部署配置 |
| **服务器配置模板** | `templates/server-config-template.md` | 服务器信息配置 |
| **降级策略** | `templates/fallback-strategy.md` | 无配置时的降级处理 |

---

## ⛔ 禁止事项

- ❌ 不要跳过预检查(测试、代码规范)
- ❌ 不要在工作区不干净时部署
- ❌ 不要跳过健康检查
- ❌ 不要忽略失败回滚
- ❌ 不要部署未经验证的代码
- ❌ 不要假设服务器配置(优先读取项目配置)

---

## ✅ 最佳实践

### 部署前

1. **预检查必须通过**:测试、代码规范
2. **工作区必须干净**:无未提交变更
3. **版本号明确**:使用语义化版本

### 部署中

1. **备份配置**:部署前备份现有配置
2. **逐步执行**:每一步验证后再继续
3. **记录日志**:详细记录部署过程

### 部署后

1. **健康检查**:验证服务可用性
2. **日志检查**:查看是否有 ERROR
3. **功能验证**:验证核心功能
4. **失败回滚**:如验证失败立即回滚

---

## 📊 质量指标

| 指标 | 目标值 | 说明 |
|------|--------|------|
| 预检查通过率 | 100% | 所有预检查必须通过 |
| 部署成功率 | ≥95% | 部署成功次数/总次数 |
| 回滚率 | ≤5% | 回滚次数/总部署次数 |
| 健康检查通过率 | 100% | 部署后必须通过 |
| 日志 ERROR 数 | 0 | 部署后无严重错误 |

---

## 🔗 完成衔接

**部署完成后,自动通知派发方(小架)。**

小架作为执行者,部署任务,完成后通过 completion event 自动回报小架。小架根据结果决定下一步:

| 部署结果 | 小架决策 | 下一动作 |
|---------|---------|----------|
| ✅ 部署成功 + 健康检查通过 | 流程完成 | 向用户汇报:部署成功 + URL + 验收说明 |
| ❌ 部署失败 + 已回滚 | 阻塞 | 向用户报告失败原因,等待决策 |
| ❌ 部署失败 + 回滚失败 | 紧急阻塞 | 立即通知用户,需人工介入 |

**部署成功后,小架应向用户发送验收通知**,包含:
- 部署版本/commit
- 访问 URL
- 验收标准(来自 Plan.md)

---

*最后更新:2026-06-14 文档驱动版 v2.1.0*
*维护:通用开发助手*
