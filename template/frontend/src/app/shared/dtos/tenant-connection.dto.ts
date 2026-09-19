/**
 * 租户的一条连接登记。
 *
 * 一个租户可以在多个服务各登记一条，连接名就是使用方 DbContext 的连接名（default / crm / foundation）。
 * **一条登记都没有，就表示该租户不单独分库**，各服务使用自己配置的数据库——没有"模式"标志位这一档。
 *
 * 连接串只写：登记与修改时整串填入，后端加密存储，任何接口都不再返回它。
 * 这里只有名字与版本——不要在此基础上加"显示连接串"或"测试连接并回显"这类功能。
 */
export interface TenantConnectionDto {
  tenantId: string;
  /** 归一化（小写）后的连接名。 */
  name: string;
  /** 并发版本：每条连接各自从 1 开始计数，每次修改递增。 */
  version: number;
}

/** 登记或更新一条连接的入参；连接名走路由，不在请求体里。 */
export interface UpsertTenantConnectionInputDto {
  /**
   * 读到的该条连接的版本；`null` 表示预期这一条尚不存在（首次登记）。
   *
   * 必须显式给出，不能用可选属性省掉：后端把"缺这个字段"当成 400，
   * 而不是静默按后写者胜出处理。
   */
  expectedVersion: number | null;
  /** 连接串（明文），只写。后端加密存储，之后任何接口都不会把它读回来。 */
  connectionString: string;
}

/** 连接名的合法形态，与后端 `TenantConnectionConfiguration.NamePattern` 同源。 */
export const TENANT_CONNECTION_NAME_PATTERN = /^[a-z0-9-]{1,64}$/;

/** 连接串长度上限，与后端入口 DTO 一致。 */
export const TENANT_CONNECTION_STRING_MAX_LENGTH = 2048;

/**
 * 按后端规则归一化连接名。
 *
 * 后端按小写存取，填 `Crm` 与 `crm` 命中同一行。前端先归一化再校验并提交，
 * 否则界面上看着是能新增的两条，写进去才发现是同一行。
 */
export function normalizeTenantConnectionName(name: string): string {
  return name.trim().toLowerCase();
}
