//#if (IdentityService)
import { HttpClient } from '@angular/common/http';
//#endif
// prettier-ignore
import {
  Injectable,
  inject,
  signal,
} from '@angular/core';
//#if (IdentityService)
import { firstValueFrom } from 'rxjs';
//#endif

import { AuthService } from './auth-service';
//#if (LocalAuthorization)
import { AuthorizationService } from './authorization-service';
//#endif
//#if (IdentityService)
import { TenantContextService } from './tenant-context-service';
import { TenantBriefOutputDto } from '../../shared/dtos/tenant.dto';
//#endif
import { ApplicationHttpError } from '../errors/application-http-error';

export type StartupStatus = 'loading' | 'success' | 'failed';

@Injectable({ providedIn: 'root' })
export class StartupService {
  private authService = inject(AuthService);
  //#if (LocalAuthorization)
  private authorizationService = inject(AuthorizationService);
  //#endif
  //#if (IdentityService)
  private readonly http = inject(HttpClient);
  private readonly tenantContext = inject(TenantContextService);
  //#endif
  private _status = signal<StartupStatus>('loading');
  private _error = signal<unknown | null>(null);

  public readonly status = this._status.asReadonly();
  public readonly error = this._error.asReadonly();

  async load(): Promise<void> {
    this._status.set('loading');
    this._error.set(null);

    //#if (IdentityService)
    // 本地存有租户时先校验其仍然存在且启用；404/停用 → 清除。
    // 必须阻塞在认证初始化之前：后续启动请求都会携带 X-Tenant-Id，
    // 失效租户的头会让它们全部被 403 拒绝，启动误入故障分支。
    await this.validateTenantContext();
    //#endif
    const pathname = window.location.pathname;
    const hash = window.location.hash;
    //#if (ResourceService)
    const isOidcCallback = pathname.includes('/auth/callback') || hash.includes('/auth/callback');
    //#endif

    //#if (IdentityService)
    if (pathname.includes('/auth/login') || hash.includes('/auth/login')) {
      this.authService.clearAuthData();
      //#if (LocalAuthorization)
      this.authorizationService.clear();
      //#endif
      this._status.set('success');
      return;
    }

    //#endif
    //#if (IdentityService)
    if (
      //#if (IdentityService)
      pathname.includes('/auth/callback') ||
      //#endif
      pathname.includes('/auth/external-callback') ||
      //#if (IdentityService)
      hash.includes('/auth/callback') ||
      //#endif
      hash.includes('/auth/external-callback')
    ) {
      this._status.set('success');
      return;
    }

    //#endif
    //#if (ResourceService)
    if (!this.isProtectedRoute(pathname, hash) && !isOidcCallback) {
    //#else
    if (!this.isProtectedRoute(pathname, hash)) {
    //#endif
      this._status.set('success');
      return;
    }

    try {
      await this.authService.initializeAuth();
      //#if (LocalAuthorization)
      // 权限与当前用户在同一次启动中就位：Guard 与菜单据此裁剪，
      // 未加载完成前一律按无权限处理，避免受保护入口闪现。
      await this.authorizationService.initialize();
      //#endif
      this._status.set('success');
    } catch (err: unknown) {
      if (err instanceof ApplicationHttpError && err.status === 401) {
        this.authService.clearAuthData();
        //#if (LocalAuthorization)
        this.authorizationService.clear();
        //#endif
        this._status.set('success');
      } else {
        this._error.set(err);
        this._status.set('failed');
      }
    }
  }

  async retry(): Promise<void> {
    // Signals 会自动处理UI更新，我们不再需要手动延时
    await this.load();
  }
  private isProtectedRoute(pathname: string, hash: string): boolean {
    const route = hash.startsWith('#/') ? hash.slice(1) : pathname;
    return route.startsWith('/workspace') || route.startsWith('/platform');
  }
  //#if (IdentityService)

  /** 校验本地租户上下文：不存在（404）或已停用则清除；其他故障保留，避免误清。 */
  private async validateTenantContext(): Promise<void> {
    const tenant = this.tenantContext.current();
    if (!tenant) {
      return;
    }

    try {
      const latest = await firstValueFrom(
        this.http.get<TenantBriefOutputDto>(
          `/api/v1/tenants/by-name/${encodeURIComponent(tenant.name)}`,
        ),
      );
      if (!latest.isActive) {
        this.tenantContext.clear();
        return;
      }
      // 顺带刷新显示名等元信息。
      this.tenantContext.set(latest);
    } catch (err: unknown) {
      if (err instanceof ApplicationHttpError && err.status === 404) {
        this.tenantContext.clear();
      }
      // 网络/服务故障不清除本地上下文：临时故障不应把用户踢回宿主。
    }
  }
  //#endif
}
