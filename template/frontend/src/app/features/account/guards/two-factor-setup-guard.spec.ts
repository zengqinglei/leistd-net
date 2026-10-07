import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  provideRouter,
  Router,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { twoFactorSetupGuard } from './two-factor-setup-guard';
import { AuthService } from '../../../core/services/auth-service';
import { User } from '../../../shared/models/user.model';

describe('twoFactorSetupGuard', () => {
  const currentUser = signal<User | null>(null);
  let loadUser: () => Observable<unknown>;

  beforeEach(() => {
    currentUser.set(null);
    loadUser = vi.fn(() => throwError(() => new Error('401')));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            isAuthenticated: () => currentUser() !== null,
            currentUser,
            loadUser: () => loadUser(),
          },
        },
      ],
    });
  });

  async function activate(): Promise<string | boolean> {
    const result = await TestBed.runInInjectionContext(() =>
      twoFactorSetupGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
    return result instanceof UrlTree
      ? TestBed.inject(Router).serializeUrl(result)
      : (result as boolean);
  }

  function user(twoFactorSetupRequired: boolean): User {
    return new User({ id: 'u1', username: 'alice', roles: [], twoFactorSetupRequired });
  }

  it('lets a session that must set up two-factor authentication open the page', async () => {
    currentUser.set(user(true));

    expect(await activate()).toBe(true);
    expect(loadUser).not.toHaveBeenCalled();
  });

  it('sends an unrestricted session to the workspace', async () => {
    currentUser.set(user(false));

    expect(await activate()).toBe('/workspace');
  });

  // /auth 下的页面启动时不预取当前用户，守卫自己取一次
  it('loads the current user first when the session has not been loaded yet', async () => {
    loadUser = vi.fn(() => {
      currentUser.set(user(true));
      return of(undefined);
    });

    expect(await activate()).toBe(true);
    expect(loadUser).toHaveBeenCalledTimes(1);
  });

  it('sends a visitor without a session to the login page', async () => {
    expect(await activate()).toBe('/auth/login');
  });
});
