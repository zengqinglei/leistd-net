import { Injectable, signal } from '@angular/core';

//#if (LocalIdentity)
const TENANT_STORAGE_KEY = 'app.tenant';

//#endif
//#if (LocalIdentity)
/** 本地持久化的租户上下文（登录页选定，拦截器读取）。 */
//#else
/** 认证后的租户上下文（来源是已验证 Access Token 中的 tenant_id）。 */
//#endif
export interface TenantContext {
  /** 放进 X-Tenant-Id 头的值：本地身份形态是租户名，资源服务形态是令牌里的租户 id。 */
  key: string;
}

//#if (LocalIdentity)
/**
 * 前端租户上下文：signal + localStorage 双写。
 *
 * 仅影响匿名请求（登录等）的 X-Tenant-Id 头；已登录用户的租户
 * 由服务端 cookie claim 定案，前端上下文只是登录入口的路由提示。
 */
//#else
/**
 * 前端租户上下文：仅内存 signal。
 *
 * 唯一来源是已验证 Access Token 中的 tenant_id；不做本地持久化，
 * 也不接受任何本地线索改写已认证的租户。
 */
//#endif
@Injectable({ providedIn: 'root' })
export class TenantContextService {
  //#if (LocalIdentity)
  private readonly _current = signal<TenantContext | null>(readFromStorage());
  //#else
  private readonly _current = signal<TenantContext | null>(null);
  //#endif
  //#if (LocalIdentity)
  /**
   * 登录入口选定的租户。
   *
   * **不能用它判断已认证会话在哪一侧。** 它只是登录页写下的路由提示（localStorage，同站点各标签页
   * 共享），已登录用户的租户由服务端 cookie claim 定案；两者可以不一致——宿主超管在另一个标签页的
   * 登录页确认过一个租户名，这里就有值了，而他的会话仍然是宿主。反过来从子域名入口登录的租户用户，
   * 这里可能是空的。
   *
   * 需要按侧别分支时：**按能力判**（服务端下发的权限列表已按侧别过滤，例如宿主专属的
   * `App.Tenants` 不会出现在租户用户的列表里），或由拥有那个页面的业务端点下发结论
   * （"你能选哪些库"、"这个租户叫什么"）。见 docs/standards/coding-frontend.md §8。
   */
  //#else
  /** 当前租户；null 表示宿主。来源是已验证 Access Token 中的 tenant_id。 */
  //#endif
  public readonly current = this._current.asReadonly();

  //#if (LocalIdentity)
  set(tenantName: string): void {
    const context: TenantContext = { key: tenantName };
    this._current.set(context);
    try {
      localStorage.setItem(TENANT_STORAGE_KEY, JSON.stringify(context));
    } catch {
      // 存储不可用（隐私模式/配额）时仅保留内存态，不影响当前会话。
    }
  }
  //#else
  /** Resource 只接受 OIDC 库已验证 Access Token 中的 tenant_id。 */
  setAuthenticatedTenant(tenantId: string): void {
    this._current.set({ key: tenantId });
  }
  //#endif

  clear(): void {
    this._current.set(null);
    //#if (LocalIdentity)
    try {
      localStorage.removeItem(TENANT_STORAGE_KEY);
    } catch {
      // 同上：清除失败不阻断流程。
    }
    //#endif
  }
}
//#if (LocalIdentity)
function readFromStorage(): TenantContext | null {
  try {
    const raw = localStorage.getItem(TENANT_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<TenantContext>;
    // 旧版本存过 {id,name,displayName}：那形态没有 key，会在这里被判为无效而清掉，
    // 用户回到登录页重选一次租户即可，不需要迁移代码
    if (typeof parsed.key !== 'string' || parsed.key.length === 0) return null;
    return { key: parsed.key };
  } catch {
    return null;
  }
}
//#endif
