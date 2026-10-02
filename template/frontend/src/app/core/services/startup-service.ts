// prettier-ignore
import {
  Injectable,
  inject,
  signal,
} from '@angular/core';

import { AuthService } from './auth-service';
//#if (LocalIdentity)
import { ImpersonationService } from './impersonation-service';
//#endif
import { SessionContextService } from './session-context-service';
import { ApplicationHttpError } from '../errors/application-http-error';
import { EntryRouteService } from '../routing/entry-route-service';

export type StartupStatus = 'loading' | 'success' | 'failed';

/**
 * 需要完成认证与会话初始化的顶层路由前缀。
 * 与 app.routes.ts 中使用 authGuard 的路由保持一致，由 app.routes.spec.ts 校验。
 */
export const PROTECTED_ROUTE_PREFIXES = ['/workspace', '/platform'] as const;

@Injectable({ providedIn: 'root' })
export class StartupService {
  private authService = inject(AuthService);
  private readonly sessionContext = inject(SessionContextService);
  private readonly entryRoute = inject(EntryRouteService);
  //#if (LocalIdentity)
  private readonly impersonation = inject(ImpersonationService);
  //#endif
  private _status = signal<StartupStatus>('loading');
  private _error = signal<unknown | null>(null);

  public readonly status = this._status.asReadonly();
  public readonly error = this._error.asReadonly();

  async load(): Promise<void> {
    this._status.set('loading');
    this._error.set(null);

    //#if (LocalIdentity)
    // 不在这里校验记住的租户：匿名确认"这个租户还在不在"等于给任何人一个枚举接口。
    // 租户失效由服务端在会话恢复中间件里处置（X-Tenant-Invalid 头），前端据此清上下文。
    //#endif
    // 入口路由只认一份读法（见 EntryRouteService.path）：路径按边界比对，不拿整条 URL 去
    // includes——查询串或锚点里的认证路由不代表当前入口，不能影响启动分支。
    //#if (LocalIdentity)
    const route = this.entryRoute.path();

    if (route === '/auth/login') {
      this.sessionContext.clear();
      this._status.set('success');
      return;
    }

    //#endif
    //#if (LocalIdentity)
    if (route === '/auth/callback' || route.startsWith('/auth/external-callback/')) {
      this._status.set('success');
      return;
    }

    //#endif
    if (!this.isProtectedRoute()) {
      this._status.set('success');
      return;
    }

    try {
      await this.authService.initializeAuth();
      // 权限与设置在同一次启动中就位：Guard 与菜单按权限裁剪、界面按设置渲染由它派生的
      // 状态，未就位前一律按无权限处理，避免受保护入口闪现。
      await this.sessionContext.establish();
      //#if (LocalIdentity)
      // 模拟态只有服务端的会话声明知道；顶栏的模拟提示要在外壳首帧就位，否则会闪一下"正常会话"。
      await this.impersonation.load();
      //#endif
      this._status.set('success');
    } catch (err: unknown) {
      if (err instanceof ApplicationHttpError && err.status === 401) {
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
    const route = this.entryRoute.path();
    return PROTECTED_ROUTE_PREFIXES.some((prefix) => route.startsWith(prefix));
  }
}
