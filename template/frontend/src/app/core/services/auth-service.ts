//#if (IncludeIdentity)
import { HttpClient, HttpContext } from '@angular/common/http';
//#endif
// prettier-ignore
import {
  Injectable,
  //#if (IncludeIdentity)
  inject,
  //#endif
  signal,
} from '@angular/core';
//#if (IncludeIdentity)
import { Observable, lastValueFrom, tap } from 'rxjs';
//#endif

//#if (IncludeNotifications)
import { SignalRService } from './signalr-service';
//#endif
//#if (IncludeIdentity)
import { LoginInputDto, UserOutputDto } from '../../features/account/models/account.dto';
//#endif
import { User } from '../../shared/models/user.model';
//#if (IncludeIdentity)
import { SILENT_AUTH } from '../interceptors/http-context-tokens';
//#endif

//#if (IncludeIdentity)
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
   * 登出、非静默 401、启动流进登录页三条路径都汇到这里，所以主体相关的清理
   * 一律挂在这一处——各自记得调的做法，迟早会漏掉其中一条。
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
/**
 * 未启用认证模块时的占位实现：始终无登录用户。
 * 保留 currentUser 信号与 isAuthenticated()，供布局/仪表盘等只读消费。
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly _currentUser = signal<User | null>(null);
  public readonly currentUser = this._currentUser.asReadonly();

  isAuthenticated(): boolean {
    return false;
  }
}
//#endif
