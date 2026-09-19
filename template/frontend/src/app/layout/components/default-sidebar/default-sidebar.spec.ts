import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { DefaultSidebar } from './default-sidebar';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
import { AuthorizationService } from '../../../core/services/authorization-service';
import { PERMISSIONS } from '../../../shared/models/permission';
import { LayoutService } from '../../services/layout-service';

//#if (IncludeLocalization)
// 空词条下 translate() 回落成键名，所以这里按**键**断言：分组骨架与文案无关，
// 改一句中文不该让这组用例变红。
const WORK = 'layout.sidebar.groupWork';
const BUSINESS = 'layout.sidebar.groupBusiness';
const PERSONAL = 'layout.sidebar.groupPersonal';
const IDENTITY = 'layout.sidebar.groupIdentity';
//#if (OpenIddictServer)
const DEVELOPER = 'layout.sidebar.groupDeveloper';
//#endif
const AUDIT = 'layout.sidebar.groupAudit';
const SYSTEM = 'layout.sidebar.groupSystem';
//#else
const WORK = 'Work';
const BUSINESS = 'Business';
const PERSONAL = 'Personal';
const IDENTITY = 'Identity & access';
//#if (OpenIddictServer)
const DEVELOPER = 'Developer';
//#endif
const AUDIT = 'Audit';
const SYSTEM = 'System';
//#endif

/**
 * 侧栏菜单的信息架构。
 *
 * 分组改错了既不报错、也不影响任何功能，只是让人找不到入口——没有断言就等于没有约束。
 * 这里钉住两个区各自的分组骨架，以及判据里能自动验的部分：不留兜底组、
 * 个人设置只在工作空间（管理平台不另放一份）、权限不足的组整组消失。
 */
describe('DefaultSidebar 菜单分组', () => {
  /**
   * 只构造类，不渲染模板。
   *
   * 分组是这个类上的 computed，而模板依赖 Spartan 的 sidebar 上下文；为了验分组去搭那套
   * 上下文，等于让这组用例依赖一堆与 IA 无关的东西。
   */
  function build(options: { platform: boolean; permissions?: readonly string[] }): DefaultSidebar {
    const granted = new Set(options.permissions ?? []);
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
          useValue: {
            hasAny: (...permissions: string[]) => permissions.some((p) => granted.has(p)),
            canAccessPlatform: () => options.platform,
          },
        },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
      ],
    });

    return TestBed.runInInjectionContext(() => new DefaultSidebar());
  }

  const groupsOf = (sidebar: DefaultSidebar) => sidebar.menuGroups().map((group) => group.label);
  const routesOf = (sidebar: DefaultSidebar) =>
    sidebar.menuGroups().flatMap((group) => group.items.map((item) => item.route));

  it('工作空间侧：工作 → 业务 → 个人，不含兜底组', () => {
    const sidebar = build({ platform: false });

    expect(groupsOf(sidebar)).toEqual([WORK, BUSINESS, PERSONAL]);
    expect(routesOf(sidebar)).toEqual([
      '/workspace/dashboard',
      '/workspace/placeholder',
      '/workspace/settings',
    ]);
  });

  // 个人设置每个用户都有，只放工作空间一处；管理人员也是用户，从头像菜单进同一处。
  // 管理平台再放一份，就会出现两个入口、两份状态。
  it('管理平台侧不放个人设置入口', () => {
    const everything = Object.values(PERMISSIONS).map((group) => group.default);
    expect(routesOf(build({ platform: true, permissions: everything }))).not.toContain(
      '/workspace/settings',
    );
  });

  it('平台侧：工作 → 身份与访问 → 开发者 → 审计 → 系统', () => {
    const permissions = [
      PERMISSIONS.users.default,
      PERMISSIONS.roles.default,
      PERMISSIONS.settings.default,
      PERMISSIONS.operationRecords.default,
      //#if (LocalIdentity)
      PERMISSIONS.tenants.default,
      //#endif
      //#if (OpenIddictServer)
      PERMISSIONS.openApplications.default,
      //#endif
    ];
    const expected = [WORK, IDENTITY];
    //#if (OpenIddictServer)
    expected.push(DEVELOPER);
    //#endif
    expected.push(AUDIT, SYSTEM);

    const sidebar = build({ platform: true, permissions });

    expect(groupsOf(sidebar)).toEqual(expected);
    expect(routesOf(sidebar)).toContain('/platform/settings');
  });

  // 权限不足时整组消失，不留一个空标题——空标题看起来像加载失败。
  it('平台侧无管理权限时只剩工作组', () => {
    const sidebar = build({ platform: true });

    expect(groupsOf(sidebar)).toEqual([WORK]);
    expect(routesOf(sidebar)).toEqual(['/platform']);
  });
});
