//#if (LocalIdentity)
import { HttpClient } from '@angular/common/http';
//#endif
// prettier-ignore
import {
  Injectable,
  inject,
  signal,
} from '@angular/core';
//#if (LocalIdentity)
import { firstValueFrom } from 'rxjs';
//#endif

import { AuthService } from './auth-service';
import { SessionContextService } from './session-context-service';
//#if (LocalIdentity)
import { TenantContextService } from './tenant-context-service';
import { TenantLookupOutputDto } from '../../shared/dtos/tenant.dto';
//#endif
import { ApplicationHttpError } from '../errors/application-http-error';
import { entryRoutePath } from '../routing/entry-route';

export type StartupStatus = 'loading' | 'success' | 'failed';

@Injectable({ providedIn: 'root' })
export class StartupService {
  private authService = inject(AuthService);
  private readonly sessionContext = inject(SessionContextService);
  //#if (LocalIdentity)
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

    //#if (LocalIdentity)
    // 本地存有租户时先校验其仍然存在且启用；404/停用 → 清除。
    // 必须阻塞在认证初始化之前：后续启动请求都会携带 X-Tenant-Id，
    // 失效租户的头会让它们全部被 403 拒绝，启动误入故障分支。
    await this.validateTenantContext();
    //#endif
    // 入口路由只认一份读法（见 entryRoutePath）：路径按边界比对，不拿整条 URL 去
    // includes——查询串或锚点里出现 `/auth/callback` 不代表人在回调页，误判会让普通
    // 会话过期走进回调专用的处置分支。
    const route = entryRoutePath();
    //#if (!LocalIdentity)
    const isOidcCallback = route === '/auth/callback';
    //#endif

    //#if (LocalIdentity)
    if (route === '/auth/login') {
      this.sessionContext.clear();
      this._status.set('success');
      return;
    }

    //#endif
    //#if (LocalIdentity)
    if (route === '/auth/callback' || route === '/auth/external-callback') {
      this._status.set('success');
      return;
    }

    //#endif
    //#if (!LocalIdentity)
    if (!this.isProtectedRoute() && !isOidcCallback) {
    //#else
    if (!this.isProtectedRoute()) {
    //#endif
      this._status.set('success');
      return;
    }

    try {
      await this.authService.initializeAuth();
      // 权限与设置在同一次启动中就位：Guard 与菜单按权限裁剪、界面按设置渲染由它派生的
      // 状态，未就位前一律按无权限处理，避免受保护入口闪现。
      await this.sessionContext.establish();
      this._status.set('success');
    } catch (err: unknown) {
      if (err instanceof ApplicationHttpError && err.status === 401) {
        //#if (!LocalIdentity)
        // 回调页上的 401 不能当作「未登录」：这一刻刚从授权服务器换到令牌，是 API 拒了它。
        // 按未登录继续走下去，会跳进受保护路由，Guard 发现没有主体又发起一次授权，而
        // 授权服务器那边会话还在、立刻带着新 code 回到回调页，同样被拒——绕成死循环，
        // 而且每一圈都在浏览器和 IdP 之间来回。停下来把错误亮出来：重来一次不会有不同
        // 结果，至少有人看得见原因（配置错的受众、时钟偏移、账号被停用……）。
        if (isOidcCallback) {
          this._error.set(err);
          this._status.set('failed');
          return;
        }

        //#endif
        this.sessionContext.clear();
        this._status.set('success');
      } else {
        this._error.set(err);
        this._status.set('failed');
      }
    }
  }

  async retry(): Promise<void> {
    await this.load();
  }
  private isProtectedRoute(): boolean {
    const route = entryRoutePath();
    return route.startsWith('/workspace') || route.startsWith('/platform');
  }
  //#if (LocalIdentity)

  /** 校验本地租户上下文：不存在（404）或已停用则清除；其他故障保留，避免误清。 */
  private async validateTenantContext(): Promise<void> {
    const tenant = this.tenantContext.current();
    if (!tenant) {
      return;
    }

    try {
      const latest = await firstValueFrom(
        this.http.get<TenantLookupOutputDto>(
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
