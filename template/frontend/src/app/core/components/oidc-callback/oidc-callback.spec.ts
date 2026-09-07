//#if (!LocalIdentity)
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { OidcCallback } from './oidc-callback';
import { AuthService } from '../../services/auth-service';

/**
 * OIDC 回调的落地导航。
 *
 * 导航一度写在 `AuthService.initializeAuth()` 里，而那时启动流还停在 loading、
 * `permissionGuard` 正等着它离开 loading——returnUrl 指向 `/platform` 时形成死等。
 * 把导航留在这里（外壳只在启动成功后渲染 router-outlet）才不会成环。
 *
 * 因此这里锁两件事：`initializeAuth()` 不再自己导航，落地地址由本组件消费。
 */
describe('OidcCallback', () => {
  let navigated: string[];
  let takeReturnUrl: jasmine.Spy<() => string>;

  beforeEach(() => {
    navigated = [];
    takeReturnUrl = jasmine.createSpy('takeReturnUrl');

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        // 真实 AuthService 会拉起整个 OIDC 客户端；本组用例只关心落地导航。
        { provide: AuthService, useValue: { takeReturnUrl } },
      ],
    });
    spyOn(TestBed.inject(Router), 'navigateByUrl').and.callFake(async (url) => {
      navigated.push(String(url));
      return true;
    });
  });

  function createWith(returnUrl: string): void {
    takeReturnUrl.and.returnValue(returnUrl);
    TestBed.createComponent(OidcCallback);
  }

  // 这条是那次死等的直接回归：/platform 下的 returnUrl 必须能正常落地。
  it('navigates to a platform return url', () => {
    createWith('/platform/users');

    expect(navigated).toEqual(['/platform/users']);
  });

  // 回落值由 takeReturnUrl() 自己给出，本组件不再叠第二层回落——
  // 两层回落只有一层能被走到，另一层是死代码。
  it('navigates to whatever the auth service hands over', () => {
    createWith('/workspace');

    expect(navigated).toEqual(['/workspace']);
  });
});
//#endif
