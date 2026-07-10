# 发布策略

> 本文件定义发布前门禁、发布步骤与回滚步骤，供部署阶段按需加载（从 `deploy/README.md` 拆出，作为单一职责的发布策略文件）。总体部署原则、环境、架构、服务清单见 `README.md`。

## 1. 发布前门禁

- [ ] 需求已验收或获准发布。
- [ ] 代码审查无 P0 问题。
- [ ] 测试通过或风险已确认。
- [ ] 数据迁移脚本已评审。
- [ ] 配置和密钥已准备。
- [ ] 备份已完成或确认无需备份。
- [ ] 回滚方案已确认。

## 2. 发布步骤

1. 拉取或构建版本：`{build-command}`。
2. 应用配置：`{config-step}`。
3. 执行迁移：`{migration-command}`。
4. 启动服务：`{start-command}`。
5. 健康检查：`{healthcheck-command}`。
6. 验证核心路径：`{verification-steps}`。
7. 观察日志和指标：`{observe-command}`。

## 3. 回滚步骤

1. 停止新版本：`{stop-command}`。
2. 恢复旧版本：`{rollback-command}`。
3. 回滚配置/迁移：`{rollback-config-or-db}`。
4. 验证服务：`{healthcheck-command}`。
5. 记录事故和后续修复计划。

## 4. 相关

- [部署指南](./README.md)（原则、环境、架构、服务清单、报告模板、AI 执行限制）
- [服务器配置模板](./server-config.md)
