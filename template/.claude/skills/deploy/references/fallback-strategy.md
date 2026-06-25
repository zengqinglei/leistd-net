# 部署配置缺失时的降级策略

> 当项目缺少部署配置时，优先提示用户补齐项目级文档；只有在用户确认后，才使用模板继续。

## 检测顺序

1. 读取 `docs/standards/agent-workflow.md` 中的部署文档索引。
2. 读取 `docs/deploy/` 下的部署说明。
3. 检查项目根目录的 `docker-compose.yml`、`compose.yml`、`.env.deploy`。
4. 如仍缺失，提示用户创建项目级部署配置。

## 提示模板

```text
检测到项目未配置部署文档或部署配置。
建议创建：
- docs/deploy/server-config.md
- docker-compose.yml 或 compose.yml
- .env.deploy（仅保存非敏感示例值，敏感值使用安全密钥管理）

本 Skill 提供参考模板：
- templates/server-config-template.md
- templates/docker-compose-template.yml

是否允许基于模板继续准备部署方案？
```

## 禁止

- 不得在未确认目标环境时部署。
- 不得在未确认回滚方案时部署。
- 不得把密码、Token、私钥写入仓库。
- 不得因缺少配置而自行猜测生产服务器。


