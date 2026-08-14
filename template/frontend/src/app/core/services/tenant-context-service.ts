//#if (TenancyEnabled)
import { Injectable, signal } from '@angular/core';

import { TenantBriefOutputDto } from '../../shared/dtos/tenant.dto';

const TENANT_STORAGE_KEY = 'app.tenant';

/** 本地持久化的租户上下文（登录页选定，拦截器读取）。 */
export interface TenantContext {
  id: string;
  name: string;
  displayName?: string;
}

/**
 * 前端租户上下文：signal + localStorage 双写。
 *
 * 仅影响匿名请求（登录等）的 X-Tenant-Id 头；已登录用户的租户
 * 由服务端 cookie claim 定案，前端上下文只是登录入口的路由提示。
 */
@Injectable({ providedIn: 'root' })
export class TenantContextService {
  private readonly _current = signal<TenantContext | null>(readFromStorage());
  /** 当前已选租户；null 表示宿主（未选租户）。 */
  public readonly current = this._current.asReadonly();

  set(tenant: TenantBriefOutputDto): void {
    const context: TenantContext = {
      id: tenant.id,
      name: tenant.name,
      displayName: tenant.displayName,
    };
    this._current.set(context);
    try {
      localStorage.setItem(TENANT_STORAGE_KEY, JSON.stringify(context));
    } catch {
      // 存储不可用（隐私模式/配额）时仅保留内存态，不影响当前会话。
    }
  }

  clear(): void {
    this._current.set(null);
    try {
      localStorage.removeItem(TENANT_STORAGE_KEY);
    } catch {
      // 同上：清除失败不阻断流程。
    }
  }
}

function readFromStorage(): TenantContext | null {
  try {
    const raw = localStorage.getItem(TENANT_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<TenantContext>;
    if (typeof parsed.id !== 'string' || typeof parsed.name !== 'string') return null;
    return { id: parsed.id, name: parsed.name, displayName: parsed.displayName };
  } catch {
    return null;
  }
}
//#endif
