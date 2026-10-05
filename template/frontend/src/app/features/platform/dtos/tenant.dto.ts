import { PagedRequestDto } from '../../../shared/dtos/paged-request.dto';

/**
 * 租户名的合法形态：单个 DNS 标签，与后端 `TenantConfiguration.NamePattern` 同源。
 *
 * 按子域名解析租户时名字就是主机名里的一段，不合规的名字建得出来却经子域名访问不到。
 */
export const TENANT_NAME_PATTERN = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$/;

/** 租户名长度上限（DNS 标签上限），与后端 `TenantConfiguration.MaxNameLength` 一致。 */
export const TENANT_NAME_MAX_LENGTH = 63;

export interface TenantOutputDto {
  id: string;
  /** 租户名称，唯一，作为稳定的业务标识（登录时按名称解析租户）。 */
  name: string;
  displayName?: string;
  /** 简短描述，说明该租户的用途。 */
  description?: string;
  /** 停用的租户其用户无法登录。 */
  isActive: boolean;
  creationTime: string;
}

export interface GetTenantsInputDto extends PagedRequestDto {
  keyword?: string;
}

export interface CreateTenantInputDto {
  name: string;
  displayName?: string;
  description?: string;
  /** 租户初始管理员账号。 */
  adminEmail: string;
  adminPassword: string;
  /**
   * 该租户的专属库连接，按名字登记；留空数组即不分库，各服务使用自己配置的数据库。
   *
   * **分库只能在建租户时定案。**登记先于播种，种子（含租户管理员）因此直接落进这些库。
   * 建好之后再想分库，后端会以 409 拒绝——那时数据已经在回落库里，登记连接不会把它们搬过去。
   * 库须事先建好并迁移过；这里只登记，不建库也不迁移。
   *
   * 多服务部署可以一次给多条（如 `default`、`crm`）：租户与全部连接在同一个事务里落库，
   * 不会出现"租户已建、某条连接还没登记"的中间状态。
   */
  connections?: CreateTenantConnectionInputDto[];
}

/** 创建租户时登记的一条命名连接。 */
export interface CreateTenantConnectionInputDto {
  /** 连接名，对应使用方服务的连接名；不区分大小写，后端归一化为小写。 */
  name: string;
  connectionString: string;
}

export interface UpdateTenantInputDto {
  name: string;
  displayName?: string;
  /**
   * 描述；`null` 表示清空。
   *
   * **这是整体覆盖，没有"不传即保留原值"这一档**：省略字段与显式传 `null` 效果相同，
   * 都会把库里的描述清掉（后端 DTO 上它是可空字段，缺省即 `null`，管理器按传入值覆盖）。
   * 所以表单每次提交都必须带上当前值，要保留就把原值一起传回来。
   */
  description?: string | null;
}

export interface SetTenantActivationInputDto {
  isActive: boolean;
}
