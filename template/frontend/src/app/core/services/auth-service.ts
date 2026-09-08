//#if (LocalIdentity)
import { HttpClient, HttpContext } from '@angular/common/http';
//#endif
// prettier-ignore
import {
  Injectable,
  inject,
  signal,
} from '@angular/core';
//#if (LocalIdentity)
import { Observable, lastValueFrom, tap } from 'rxjs';
//#endif
//#if (!LocalIdentity)
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { firstValueFrom } from 'rxjs';
//#endif

//#if (IncludeNotifications)
import { SignalRService } from './signalr-service';
//#endif
//#if (LocalIdentity)
import { LoginInputDto, UserOutputDto } from '../../shared/dtos/auth.dto';
//#endif
//#if (!LocalIdentity)
import { TenantContextService } from './tenant-context-service';
//#endif
import { User } from '../../shared/models/user.model';
//#if (LocalIdentity)
import { SILENT_AUTH } from '../interceptors/http-context-tokens';
//#endif

//#if (LocalIdentity)
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  //#if (IncludeNotifications)
  private readonly signalR = inject(SignalRService);
  //#endif

  private readonly _currentUser = signal<User | null>(null);
  public readonly currentUser = this._currentUser.asReadonly();

  isAuthenticated(): boolean {
    return this._currentUser() !== null;
  }

  login(credentials: LoginInputDto): Observable<void> {
    return this.http.post<void>('/api/v1/auth/session-login', credentials);
  }

  // 错误按原样抛出：401（未登录）与服务故障（503/断网）由 StartupService 分别处理，
  // 吞掉异常会把认证服务故障误判成「未登录」。
  async initializeAuth(): Promise<void> {
    await lastValueFrom(this.loadUser());
  }

  loadUser(): Observable<UserOutputDto> {
    // 标记为静默认证：/me 探测的 401 由 Guard（负责 returnUrl）与登录流各自本地处理，
    // 不触发 HTTP 拦截器的全局跳转，避免启动阶段覆盖 Guard 的 returnUrl。
    return this.http
      .get<UserOutputDto>('/api/v1/auth/me', {
        context: new HttpContext().set(SILENT_AUTH, true),
      })
      .pipe(tap((user) => this.setCurrentUser(user)));
  }

  setCurrentUser(user: UserOutputDto): void {
    this._currentUser.set(new User(user));
  }

  /**
   * 清空当前认证主体的一切本地状态。
   *
   * 只清认证数据。权限与设置也跟着主体走，但它们的清理在 <c>SessionContextService.clear()</c>：
   * 非静默 401 与启动流进登录页都走那个入口，一处清三样，避免各自记得调而漏掉一条。
   * <c>logout()</c> 之后是整页跳转，内存状态随页面重建，不必再走一遍。
   */
  clearAuthData(): void {
    this._currentUser.set(null);
    //#if (IncludeNotifications)
    // 不等待：状态与连接引用在 reset() 内部同步清掉，真正的 stop() 是网络动作，
    // 让它在后台完成即可，不该把登出卡在网络上。
    void this.signalR.reset();
    //#endif
  }

  logout(): void {
    this.clearAuthData();
    this.http.post('/api/v1/auth/logout', {}).subscribe({
      next: () => (window.location.href = '/auth/login'),
      error: () => (window.location.href = '/auth/login'),
    });
  }
}
//#else
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly oidc = inject(OidcSecurityService);
  private readonly tenantContext = inject(TenantContextService);
  //#if (IncludeNotifications)
  private readonly signalR = inject(SignalRService);
  //#endif
  private readonly _currentUser = signal<User | null>(null);
  public readonly currentUser = this._currentUser.asReadonly();

  isAuthenticated(): boolean {
    return this._currentUser() !== null;
  }

  async initializeAuth(): Promise<void> {
    const result = await firstValueFrom(this.oidc.checkAuth());
    if (!result.isAuthenticated || !result.accessToken) {
      this.clearAuthData();
      return;
    }

    const claims = decodeJwtPayload(result.accessToken);
    const tenantId = requireSingleTenantId(claims['tenant_id']);
    this.tenantContext.setAuthenticatedTenant(tenantId);
    this._currentUser.set(
      new User({
        id: claimString(claims['sub']),
        username: claimString(claims['preferred_username']) || claimString(claims['name']),
        email: claimString(claims['email']),
        displayName: claimString(claims['name']),
        roles: claimStrings(claims['role']),
        isSuperAdmin: claims['is_super_admin'] === true || claims['is_super_admin'] === 'true',
      }),
    );
  }

  /**
   * 取出并清掉登录前记下的落地地址；没记过就回落 /workspace。
   *
   * **导航不在 initializeAuth 里做**：那时启动流还停在 loading，而 permissionGuard 要等
   * 启动状态离开 loading 才放行——导航到 /platform 会形成
   * 启动 → 确立主体 → 导航 → Guard 等启动 的环。权限与设置也还没就位，
   * 即使落到 /workspace 不卡住，也是在无权限状态下渲染。
   * 因此由 OIDC 回调组件在会话上下文就绪后消费它，而这里不判「是否在回调页」——
   * 唯一的调用方就是那个只挂在 /auth/callback 上的组件。
   */
  takeReturnUrl(): string {
    const returnUrl = sessionStorage.getItem('app.auth.returnUrl') || '/workspace';
    sessionStorage.removeItem('app.auth.returnUrl');
    return returnUrl;
  }

  login(returnUrl = '/workspace'): void {
    sessionStorage.setItem('app.auth.returnUrl', returnUrl);
    this.oidc.authorize();
  }

  clearAuthData(): void {
    this._currentUser.set(null);
    this.tenantContext.clear();
    //#if (IncludeNotifications)
    // Hub principal 在握手时已固定，主体切换必须断开旧连接并清空通知状态。
    void this.signalR.reset();
    //#endif
  }

  logout(): void {
    this.clearAuthData();
    this.oidc.logoff().subscribe();
  }
}

function decodeJwtPayload(accessToken: string): Record<string, unknown> {
  const encodedPayload = accessToken.split('.')[1];
  if (!encodedPayload) throw new Error('The access token has no JWT payload.');
  const normalized = encodedPayload.replace(/-/g, '+').replace(/_/g, '/');
  const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '=');
  return JSON.parse(atob(padded)) as Record<string, unknown>;
}

function requireSingleTenantId(value: unknown): string {
  if (Array.isArray(value) || typeof value !== 'string' || !isGuid(value)) {
    throw new Error('The validated access token must contain exactly one tenant_id claim.');
  }
  return value;
}

function isGuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}

function claimString(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

function claimStrings(value: unknown): string[] {
  if (typeof value === 'string') return [value];
  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === 'string')
    : [];
}
//#endif
