/**
 * 域名对租户的定案结果。
 *
 * 三档必须分开，不能合成"有没有租户"两档：`host` 是域名已经定了案（本次请求按宿主处理），
 * `undecided` 是域名不表态、后续解析来源（记住的租户、请求头）仍可决定。
 * 两者都当成"没有租户"时，登录页会在宿主域上继续显示上次记住的租户，
 * 而服务端已按宿主处理——界面显示的和实际生效的不是同一个租户上下文。
 */
export type HostTenantDecision = 'undecided' | 'host' | 'tenant';

/**
 * 匿名响应里的租户：只有名字。
 *
 * 刻意不含标识与启用状态——未认证者不该读出租户主键，也不该分辨出某个租户是否存在、是否启用。
 * 名字放进租户提示头（`TENANT_HEADER`）即可，服务端按名字同样能解析。
 */
export interface AnonymousTenantOutputDto {
  name: string;
}

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
  tenant?: AnonymousTenantOutputDto;
}
