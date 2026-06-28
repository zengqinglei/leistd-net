# 服务器配置模板

## 1. 文档边界

本文记录环境、服务、端口、配置项和运维约束。真实密钥和敏感值应存放在密钥管理系统或环境变量中，不写入本文。

## 2. 服务器清单

| 环境 | 主机/资源 | 用途 | 负责人 |
| --- | --- | --- | --- |
| `{env}` | `{host-or-resource}` | `{usage}` | `{owner}` |

## 3. 目录结构

```text
{deploy-root}/
├── app/
├── config/
├── logs/
├── backups/
└── scripts/
```

## 4. 服务配置

| 服务 | 端口 | 配置文件 | 日志位置 | 说明 |
| --- | --- | --- | --- | --- |
| `{service}` | `{port}` | `{config-path}` | `{log-path}` | `{description}` |

## 5. 环境变量

| 变量 | 是否敏感 | 示例 | 说明 |
| --- | --- | --- | --- |
| `{ENV_NAME}` | 否 | `{value}` | `{description}` |
| `{SECRET_NAME}` | 是 | `<secret>` | `{description}` |

## 6. 数据与备份

| 对象 | 备份频率 | 保留周期 | 恢复方式 |
| --- | --- | --- | --- |
| `{database-or-files}` | `{frequency}` | `{retention}` | `{restore-command}` |

## 7. 监控与告警

- 健康检查：`{healthcheck}`
- 日志：`{log-system}`
- 指标：`{metrics-system}`
- 告警：`{alert-channel}`

## 8. 运维操作确认

以下操作必须人工确认：

- 删除或覆盖数据。
- 修改生产配置和密钥。
- 重启生产核心服务。
- 执行数据库迁移或回滚。
