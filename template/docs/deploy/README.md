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

## 5. 发布门禁 / 步骤 / 回滚

发布前门禁、发布步骤与回滚步骤见 [发布策略](./release-policy.md)（单一职责，部署阶段按需加载）。

## 6. 部署报告模板

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

## 7. AI 执行限制

AI 可以生成部署方案和检查清单，但不得在未获人工确认时执行生产部署、删除数据或修改真实密钥。
