import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
// prettier-ignore
import {
  ActivatedRouteSnapshot,
  RouterStateSnapshot,
  //#if (LocalIdentity)
  provideRouter,
  Router,
  UrlTree,
  //#endif
} from '@angular/router';

import { authGuard } from './auth-guard';
import { User } from '../../shared/models/user.model';
import { AuthService } from '../services/auth-service';

describe('authGuard', () => {
  const currentUser = signal<User | null>(null);
  //#if (RemoteTokenAuth)
  const startLogin = vi.fn();
  //#endif

  beforeEach(() => {
    currentUser.set(null);
    TestBed.configureTestingModule({
      providers: [
        //#if (LocalIdentity)
        provideRouter([]),
        //#endif
        {
          provide: AuthService,
          useValue: {
            isAuthenticated: () => currentUser() !== null,
            currentUser,
            //#if (RemoteTokenAuth)
            startLogin,
            //#endif
          },
        },
      ],
    });
  });

  function activate(url: string) {
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    );
    //#if (LocalIdentity)
    return result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : result;
    //#else
    return result;
    //#endif
  }

  function signIn(user: Partial<User> = {}): void {
    currentUser.set(new User({ id: 'u1', username: 'alice', roles: [], ...user }));
  }

  it('lets an authenticated user through', () => {
    signIn();

    expect(activate('/workspace')).toBe(true);
  });
  //#if (LocalIdentity)

  it('sends an anonymous visitor to the login page with the requested url as returnUrl', () => {
    expect(activate('/platform/users?offset=20')).toBe(
      '/auth/login?returnUrl=%2Fplatform%2Fusers%3Foffset%3D20',
    );
  });

  // 受限会话只能去两步验证设置页；服务端同样只放行设置所需的接口
  it('confines a session that must set up two-factor authentication to the setup page', () => {
    signIn({ twoFactorSetupRequired: true });

    expect(activate('/workspace')).toBe('/auth/two-factor-setup');
  });
  //#else

  it('starts the login flow for an anonymous visitor and blocks the navigation', () => {
    expect(activate('/platform/users')).toBe(false);
    expect(startLogin).toHaveBeenCalledWith('/platform/users');
  });
  //#endif
});
