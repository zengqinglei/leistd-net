import { TenantDatabaseMode } from './tenant.dto';

/**
 * 租户连接配置。
 *
 * 两个 `*SecretReference` 是**密钥引用名**（如 `dedicated-runtime`），真正的连接串留在
 * 宿主的 `TenantSecrets__*` 配置里，接口从不返回。展示引用名是安全的；不要在此基础上
 * 加"显示连接串"或"测试连接并回显"这类功能。
 */
export interface TenantConnectionOutputDto {
  tenantId: string;
  databaseMode: TenantDatabaseMode;
  runtimeSecretReference?: string;
  migrationSecretReference?: string;
  /** 并发版本：启停、改名与连接配置变更共享它。 */
  version: number;
}
