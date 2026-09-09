import { PagedRequestDto } from '../models/paged-request.dto';

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

/** 匿名按名称解析租户的返回体（登录页租户选择用）。 */
export interface TenantLookupOutputDto {
  id: string;
  name: string;
  displayName?: string;
  isActive: boolean;
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
  databaseMode: TenantDatabaseMode;
  runtimeSecretReference?: string;
  migrationSecretReference?: string;
}

export type TenantDatabaseMode = 'sharedDatabase' | 'dedicatedDatabase';

/**
 * 域名对租户的定案结果。
 *
 * 三档必须分开，不能合成"有没有租户"两档：`host` 是域名已经定了案（本次请求按宿主处理），
 * `undecided` 是域名不表态、后续解析来源（记住的租户、请求头）仍可决定。
 * 两者都当成"没有租户"时，登录页会在宿主域上继续显示上次记住的租户，
 * 而服务端已按宿主处理——界面显示的和实际生效的不是同一个租户上下文。
 */
export type HostTenantDecision = 'undecided' | 'host' | 'tenant';

/** 按当前主机名解析租户的结果。 */
export interface TenantByHostOutputDto {
  decision: HostTenantDecision;
  /**
   * 定案到的租户，只有 `decision === 'tenant'` 时才可能有值。
   *
   * 可空是因为契约不能承诺它非空，不是"子域名写错了"那种情况——那种请求在服务端的
   * 租户解析阶段就被拒了，探测本身拿到的是 404，走的是探测失败那条分支。
   * 真的取不到时 `decision` 仍是 `tenant`：域名已经定了案，界面不该退回让用户自己挑一个。
   */
  tenant?: TenantLookupOutputDto;
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
