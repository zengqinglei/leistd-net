//#if (IncludeIdentity)
import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, lastValueFrom, tap } from 'rxjs';

import { UserOutputDto } from '../../features/account/models/account.dto';
import { User } from '../../shared/models/user.model';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly _currentUser = signal<User | null>(null);
  public readonly currentUser = this._currentUser.asReadonly();

  isAuthenticated(): boolean {
    return this._currentUser() !== null;
  }

  login(credentials: any): Observable<void> {
    return this.http.post<void>('/api/v1/auth/session-login', credentials);
  }

  async initializeAuth(): Promise<boolean> {
    try {
      await lastValueFrom(this.loadUser());
      return true;
    } catch {
      return false;
    }
  }

  loadUser(): Observable<UserOutputDto> {
    return this.http.get<UserOutputDto>('/api/v1/auth/me').pipe(tap(user => this.setCurrentUser(user)));
  }

  setCurrentUser(user: UserOutputDto): void {
    this._currentUser.set(new User(user));
  }

  hasRole(role: string): boolean {
    return this._currentUser()?.roles.includes(role) ?? false;
  }

  clearAuthData(): void {
    this._currentUser.set(null);
  }

  logout(): void {
    this.clearAuthData();
    this.http.post('/api/v1/auth/logout', {}).subscribe({
      next: () => (window.location.href = '/auth/login'),
      error: () => (window.location.href = '/auth/login')
    });
  }
}
//#else
import { Injectable, signal } from '@angular/core';

import { User } from '../../shared/models/user.model';

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
