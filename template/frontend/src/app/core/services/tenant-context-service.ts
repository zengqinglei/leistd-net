import { Injectable, signal } from '@angular/core';

//#if (LocalIdentity)
const TENANT_STORAGE_KEY = 'app.tenant';

//#endif
//#if (LocalIdentity)
/** 本地持久化的租户上下文（登录页选定，拦截器读取）。 */
//#else
/** 认证后的租户上下文（来源是后端已验证会话中的 tenant_id）。 */
//#endif
export interface TenantContext {
  /** 租户键：本地身份形态是登录入口选定的租户名（放进租户提示头），资源服务形态是后端会话里的租户 id。 */
  key: string;
}

//#if (LocalIdentity)
/**
 * 前端租户上下文：内存 signal，另在 localStorage 记住上次成功进入的租户。只影响匿名请求（登录等）的租户提示头；
 * 已登录用户的租户由服务端 cookie claim 定案。
 *
 * 选定与清除只改内存；凭据被接受后才 {@link remember} 持久化，一次选错且登录失败不改变下次的预选。
 */
//#else
/** 前端租户上下文：仅内存 signal，唯一来源是后端已验证会话中的 tenant_id，不接受本地线索改写。 */
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
   * 登录入口选定的租户，只是登录页写下的路由提示（localStorage，各标签页共享），不能用来判断
   * 已认证会话在哪一侧；需要按侧别分支时按能力判（服务端下发的权限已按侧别过滤），或由业务端点下发结论。
   */
  //#else
  /** 当前租户；null 表示宿主。来源是后端已验证会话中的 tenant_id。 */
  //#endif
  public readonly current = this._current.asReadonly();

  //#if (LocalIdentity)
  /** 切到该租户（只改内存）。 */
  set(tenantName: string): void {
    this._current.set({ key: tenantName });
  }

  /** 凭据被接受后调用：把当前上下文记为下次的预选；当前是宿主时清掉记住的租户。 */
  remember(): void {
    const context = this._current();
    try {
      if (context) {
        localStorage.setItem(TENANT_STORAGE_KEY, JSON.stringify(context));
      } else {
        localStorage.removeItem(TENANT_STORAGE_KEY);
      }
    } catch {
      // 存储不可用（隐私模式/配额）时仅保留内存态，不影响当前会话。
    }
  }

  /** 租户已不可用（不存在、已停用、域名指向宿主）：内存与记住的一起清掉。 */
  forget(): void {
    this._current.set(null);
    try {
      localStorage.removeItem(TENANT_STORAGE_KEY);
    } catch {
      // 同上：清除失败不阻断流程。
    }
  }
  //#else
  /** Resource 只接受 OIDC 库后端已验证会话中的租户声明；null 表示宿主用户。 */
  setAuthenticatedTenant(tenantId: string | null): void {
    this._current.set(tenantId ? { key: tenantId } : null);
  }
  //#endif

  //#if (LocalIdentity)
  /** 回到宿主（只改内存）。 */
  //#endif
  clear(): void {
    this._current.set(null);
  }
}
//#if (LocalIdentity)
function readFromStorage(): TenantContext | null {
  try {
    const raw = localStorage.getItem(TENANT_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<TenantContext>;
    // 不含 key 的旧形态存档判为无效并清掉，用户回到登录页重选即可。
    if (typeof parsed.key !== 'string' || parsed.key.length === 0) return null;
    return { key: parsed.key };
  } catch {
    return null;
  }
}
//#endif
