# 部署指南

## 1. 部署原则

- 部署前必须明确版本、变更范围、风险和回滚方案。
- 生产环境操作需要人工确认。
- 配置和密钥不写入仓库。
- 部署后必须执行健康检查和核心路径验证。

## 2. 环境

| 环境 | 用途 | 地址 | 负责人 |
| --- | --- | --- | --- |
| local | 本地开发 | `{local-url}` | `{Owner}` |
| staging | 预发验证 | `{staging-url}` | `{Owner}` |
| production | 生产 | `{production-url}` | `{Owner}` |

## 3. 部署架构

```text
Client -> Reverse Proxy -> App Service -> Database/Cache/External Services
```

## 4. 服务清单

| 服务 | 说明 | 端口/入口 | 健康检查 | 日志 |
| --- | --- | --- | --- | --- |
| `{service}` | `{description}` | `{port}` | `{healthcheck}` | `{log-path}` |

## 5. 部署前门禁

- [ ] 需求已验收或获准发布。
- [ ] 代码审查无 P0 问题。
- [ ] 测试通过或风险已确认。
- [ ] 数据迁移脚本已评审。
- [ ] 配置和密钥已准备。
- [ ] 备份已完成或确认无需备份。
- [ ] 回滚方案已确认。

## 6. 发布步骤

1. 拉取或构建版本：`{build-command}`。
2. 应用配置：`{config-step}`。
3. 执行迁移：`{migration-command}`。
4. 启动服务：`{start-command}`。
5. 健康检查：`{healthcheck-command}`。
6. 验证核心路径：`{verification-steps}`。
7. 观察日志和指标：`{observe-command}`。

## 7. 回滚步骤

1. 停止新版本：`{stop-command}`。
2. 恢复旧版本：`{rollback-command}`。
3. 回滚配置/迁移：`{rollback-config-or-db}`。
4. 验证服务：`{healthcheck-command}`。
5. 记录事故和后续修复计划。

## 8. 部署报告模板

```markdown
# 部署报告 - {version}

## 1. 变更范围
## 2. 部署时间
## 3. 执行人/确认人
## 4. 部署步骤与结果
## 5. 健康检查
## 6. 验收结果
## 7. 风险与遗留问题
```

## 9. AI 执行限制

AI 可以生成部署方案和检查清单，但不得在未获人工确认时执行生产部署、删除数据或修改真实密钥。
