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
/**
 * 匿名响应里的租户：只有名字。
 *
 * 刻意不含标识与启用状态——未认证者不该读出租户主键，也不该分辨出某个租户是否存在、是否启用。
 * 名字放进 `X-Tenant-Id` 头即可，服务端按名字同样能解析。
 */
export interface AnonymousTenantOutputDto {
  name: string;
}

export interface TenantByHostOutputDto {
  decision: HostTenantDecision;
  /**
   * 定案到的租户，只有 `decision === 'tenant'` 时才可能有值。
   *
   * 可空是因为契约不能承诺它非空，不是"子域名写错了"那种情况——那种请求在服务端的
   * 租户解析阶段就被拒了，探测本身拿到的是 404，走的是探测失败那条分支。
   * 真的取不到时 `decision` 仍是 `tenant`：域名已经定了案，界面不该退回让用户自己挑一个。
   */
  tenant?: AnonymousTenantOutputDto;
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
