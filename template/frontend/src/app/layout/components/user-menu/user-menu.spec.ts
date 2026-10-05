import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { UserMenu } from './user-menu';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
import { AuthService } from '../../../core/services/auth-service';
import { AuthorizationService } from '../../../core/services/authorization-service';
import { LayoutService } from '../../../core/services/layout-service';

//#if (IncludeLocalization)
// 空词条下 translate() 回落成键名，所以按**键**断言：改一句中文不该让这组用例变红。
const WORKSPACE = 'menu.workspace';
const PLATFORM = 'menu.platform';
const PERSONAL_SETTINGS = 'menu.personalSettings';
const LOGOUT = 'menu.logout';
//#else
const WORKSPACE = 'Workspace';
const PLATFORM = 'Admin platform';
const PERSONAL_SETTINGS = 'Personal settings';
const LOGOUT = 'Sign out';
//#endif

/**
 * 头像菜单的构成。
 *
 * 这里钉住的是两条决定，它们改错了都不报错：
 * 1. **个人设置在这儿有一个入口**，管理平台上也能直达（侧栏只在工作空间放它，
 *    管理人员不回工作空间就只能靠这里）。
 * 2. **没有「切换租户」**。理由见 `docs/standards/frontend-ui.md`「导航与菜单分组」：会话租户由
 *    cookie claim 定案，换租户只能重新登录。所以用例按**完整序列**断言而不是逐项存在，
 *    多出一项就会红。
 */
describe('UserMenu items', () => {
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
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
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

  const personalItems = [PERSONAL_SETTINGS];

  it('shows only the personal group and sign-out to a regular user', () => {
    expect(labelsOf(build({ platform: false }))).toEqual([...personalItems, LOGOUT]);
  });

  it('adds an entry back to the workspace on the platform', () => {
    expect(labelsOf(build({ platform: true }))).toEqual([WORKSPACE, ...personalItems, LOGOUT]);
  });

  it('adds an entry to the admin platform for an admin in the workspace', () => {
    const menu = build({ platform: false, canAccessPlatform: true });
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    expect(labelsOf(menu)).toEqual([PLATFORM, ...personalItems, LOGOUT]);

    menu.userMenuItems().find((item) => item.label === PLATFORM)!.action!();
    expect(navigate).toHaveBeenCalledWith(['/platform']);
  });

  it('points personal settings to the workspace settings page, even on the admin platform', () => {
    const menu = build({ platform: true });
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    menu.userMenuItems().find((item) => item.label === PERSONAL_SETTINGS)!.action!();

    // /platform/settings 是系统默认值与策略（管理动作），两者不能混
    expect(navigate).toHaveBeenCalledWith(['/workspace/settings']);
  });
});
