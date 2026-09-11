import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { UserMenu } from './user-menu';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
import { AuthService } from '../../../core/services/auth-service';
import { AuthorizationService } from '../../../core/services/authorization-service';
//#if (IncludeLocalization)
import { LanguageService } from '../../../core/services/language-service';
//#endif
import { TenantContextService } from '../../../core/services/tenant-context-service';
import { LayoutService } from '../../services/layout-service';

//#if (IncludeLocalization)
// 空词条下 translate() 回落成键名，所以按**键**断言：改一句中文不该让这组用例变红。
const WORKSPACE = 'menu.workspace';
const PLATFORM = 'menu.platform';
//#if (LocalIdentity)
const PROFILE = 'menu.profile';
const CHANGE_PASSWORD = 'menu.changePassword';
//#endif
const PREFERENCES = 'menu.preferences';
const LOGOUT = 'menu.logout';
//#else
const WORKSPACE = 'Workspace';
const PLATFORM = 'Admin platform';
//#if (LocalIdentity)
const PROFILE = 'Profile';
const CHANGE_PASSWORD = 'Change password';
//#endif
const PREFERENCES = 'Preferences';
const LOGOUT = 'Sign out';
//#endif

/**
 * 头像菜单的构成。
 *
 * 这里钉住的是两条决定，它们改错了都不报错：
 * 1. **个人偏好在这儿**（`default-sidebar.spec.ts` 只证明它不在侧栏——入口被整个删掉、
 *    路由写错，那组用例照样全绿）。
 * 2. **没有「切换租户」**。理由见 `docs/standards/coding-frontend.md` §8：会话租户由
 *    cookie claim 定案，换租户只能重新登录。所以用例按**完整序列**断言而不是逐项存在，
 *    多出一项就会红。
 */
describe('UserMenu 菜单构成', () => {
  /**
   * `platform`（此刻在哪个区）与 `canAccessPlatform`（有没有权限进管理侧）是两个独立输入。
   *
   * 把它们绑成同一个布尔值，就漏掉了最常见的那个状态：管理员待在工作空间。
   * 而"回管理平台"这个入口只在那个状态下出现——绑在一起时，整段删掉用例也不会红。
   */
  function build(options: { platform: boolean; canAccessPlatform?: boolean }): UserMenu {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        // 真实 LayoutService 的 isPlatform 由路由派生，要靠导航才能切换；
        // 本组用例要的只是"当前在哪个区"这一个输入。
        {
          provide: LayoutService,
          useValue: {
            isPlatform: signal(options.platform),
            currentUrl: signal(options.platform ? '/platform' : '/workspace/dashboard'),
          },
        },
        {
          provide: AuthorizationService,
          useValue: { canAccessPlatform: () => options.canAccessPlatform ?? options.platform },
        },
        // currentUser / current 只被模板用到，这组用例只构造类；给出去是为了满足注入。
        { provide: AuthService, useValue: { currentUser: signal(null), logout: () => undefined } },
        { provide: TenantContextService, useValue: { current: signal(null) } },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        // 真实 LanguageService 会读设备语言并回写 document/transloco；这里只需要"活动语言"
        // 这一个可依赖的信号。
        { provide: LanguageService, useValue: { activeLang: signal('en') } },
        //#endif
      ],
    });

    return TestBed.runInInjectionContext(() => new UserMenu());
  }

  // 分隔线不是菜单项，只是视觉分隔；比较序列时去掉它。
  const labelsOf = (menu: UserMenu) =>
    menu
      .userMenuItems()
      .filter((item) => !item.separator)
      .map((item) => item.label);

  // prettier-ignore
  const personalItems = [
    //#if (LocalIdentity)
    PROFILE,
    CHANGE_PASSWORD,
    //#endif
    PREFERENCES,
  ];

  it('普通用户：个人一簇 + 退出，没有别的入口', () => {
    expect(labelsOf(build({ platform: false }))).toEqual([...personalItems, LOGOUT]);
  });

  it('平台侧多一个回工作空间的入口', () => {
    expect(labelsOf(build({ platform: true }))).toEqual([WORKSPACE, ...personalItems, LOGOUT]);
  });

  it('管理员待在工作空间时多一个回管理平台的入口', () => {
    const menu = build({ platform: false, canAccessPlatform: true });
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);

    expect(labelsOf(menu)).toEqual([PLATFORM, ...personalItems, LOGOUT]);

    menu.userMenuItems().find((item) => item.label === PLATFORM)!.action!();
    expect(navigate).toHaveBeenCalledWith(['/platform']);
  });

  it('偏好设置指向账户作用域的设置页', () => {
    const menu = build({ platform: false });
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);

    const preferences = menu.userMenuItems().find((item) => item.label === PREFERENCES);
    preferences!.action!();

    // /platform/settings 是租户级默认值（管理动作），两者不能混
    expect(navigate).toHaveBeenCalledWith(['/workspace/settings']);
  });
});
