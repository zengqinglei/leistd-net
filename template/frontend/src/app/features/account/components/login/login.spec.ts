import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { provideTransloco, TRANSLOCO_LOADER } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';
import { of, throwError } from 'rxjs';

import { Login } from './login';
import { permissionGuard } from '../../../../core/guards/permission-guard';
import { AuthService } from '../../../../core/services/auth-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { SessionContextService } from '../../../../core/services/session-context-service';
import { StartupService } from '../../../../core/services/startup-service';
import { PERMISSIONS } from '../../../../shared/models/permission';

/**
 * 登录提交路径。
 *
 * 其中 returnUrl 的处理是安全相关的：它来自查询串，攻击者可以构造一个指向外站的
 * 登录链接，用户登录成功后被直接送走。因此只接受本地绝对路径，`//host` 与
 * 含 `://` 的一律丢弃——这条判断没有测试的话，放宽它不会有任何提示。
 */
describe('Login', () => {
  let fixture: ComponentFixture<Login>;
  let component: Login;
  let router: Router;
  let authService: jasmine.SpyObj<AuthService>;
  let authorization: AuthorizationService;
  let queryParams: Record<string, string>;

  async function setUp(): Promise<void> {
    authService = jasmine.createSpyObj<AuthService>('AuthService', [
      'login',
      'loadUser',
      'clearAuthData',
    ]);
    // 具体载荷与本用例无关：登录流程只关心"成功/失败"和随后的跳转。
    authService.login.and.returnValue(of(undefined) as never);
    authService.loadUser.and.returnValue(of(undefined) as never);

    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        // 组件模板用了 transloco 管道，手写 TranslocoService 桩补不齐它依赖的内部配置；
        // 用真实 provider 配一个空加载器，文案回落成键名即可，本组用例不关心文案。
        provideTransloco({
          config: { availableLangs: ['en'], defaultLang: 'en', fallbackLang: 'en' },
        }),
        { provide: TRANSLOCO_LOADER, useValue: { getTranslation: () => of({}) } },
        //#endif
        { provide: AuthService, useValue: authService },
        // 真实 permissionGuard 会先等启动流结束；登录页自身不依赖它，给个已完成的桩即可。
        { provide: StartupService, useValue: { status: signal('success' as const) } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } },
        },
      ],
    }).compileComponents();

    // 登录成功后会建立会话上下文（拉权限 + 拉设置）来决定落地页；不打桩的话这些真实请求
    // 永远等不到响应，用例会以超时失败，而不是报出真正的断言。
    authorization = TestBed.inject(AuthorizationService);
    spyOn(TestBed.inject(SessionContextService), 'establish').and.resolveTo();

    fixture = TestBed.createComponent(Login);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);

    spyOn(router, 'navigateByUrl').and.resolveTo(true);
    spyOn(router, 'navigate').and.resolveTo(true);

    fixture.detectChanges();
  }

  function fillValidCredentials(): void {
    component.loginForm.usernameOrEmail().value.set('admin');
    component.loginForm.password().value.set('Admin@123456');
  }

  beforeEach(() => {
    queryParams = {};
  });

  it('表单非法时不发起登录请求', async () => {
    await setUp();

    // 空表单直接提交：应停在原地并标记为已触碰，让校验信息显示出来。
    await component.onSubmit();

    expect(authService.login).not.toHaveBeenCalled();
    expect(component.loginForm().touched()).toBeTrue();
    expect(component.isLoading()).toBeFalse();
  });

  it('登录成功后跳转到安全的本地 returnUrl', async () => {
    queryParams = { returnUrl: '/platform/users' };
    await setUp();
    fillValidCredentials();

    await component.onSubmit();

    expect(authService.login).toHaveBeenCalled();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/platform/users');
  });

  for (const hostile of ['//evil.example.com', 'https://evil.example.com', 'javascript://evil']) {
    it(`丢弃指向站外的 returnUrl：${hostile}`, async () => {
      queryParams = { returnUrl: hostile };
      await setUp();
      fillValidCredentials();

      await component.onSubmit();

      // 登录成功后被送到外站，是钓鱼链接最省事的一种玩法。
      expect(router.navigateByUrl).not.toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalled();
    });
  }

  it('会话建立失败时只弹失败提示，不弹成功提示', async () => {
    await setUp();
    fillValidCredentials();
    (TestBed.inject(SessionContextService).establish as jasmine.Spy).and.rejectWith(
      new Error('settings unavailable'),
    );
    // toast 是模块级单例对象，组件与此处引用同一个，改它的方法组件立刻看得见。
    const success = spyOn(toast, 'success');
    const error = spyOn(toast, 'error');

    await component.onSubmit();

    // 先弹「登录成功」再弹「登录失败」的话，用户看到的是两条互相打脸的提示，
    // 而人还停在登录页——提示必须等会话真的建立完再发。
    expect(success).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('登录失败时不跳转，并复位加载状态', async () => {
    await setUp();
    fillValidCredentials();
    authService.login.and.returnValue(throwError(() => new Error('bad credentials')));

    await component.onSubmit();

    expect(router.navigate).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();

    // 加载状态卡住的话，按钮会一直转，用户只能刷新页面。
    expect(component.isLoading()).toBeFalse();
  });

  it('登录到受保护的 returnUrl 时，权限先加载完再导航', async () => {
    queryParams = { returnUrl: '/protected' };
    await setUp();
    fillValidCredentials();

    // 用真实 Router + 真实 permissionGuard 跑完整条路：
    // spy 掉 Router 只能验到"调用了 navigateByUrl"，验不到导航之后 guard 怎么判。
    // 进登录页时会话上下文已被清空；若在建立它之前就跳转，
    // guard 会在空权限下判定并把人踢到 403——从深链登录本该落到那个页面。
    (router.navigateByUrl as jasmine.Spy).and.callThrough();
    (TestBed.inject(SessionContextService).establish as jasmine.Spy).and.callFake(async () => {
      authorization.setPermissions({
        permissions: [PERMISSIONS.users.default],
        isSuperAdmin: false,
        versionToken: 'r1',
      });
    });

    router.resetConfig([
      {
        path: 'protected',
        canActivate: [permissionGuard],
        data: { permission: PERMISSIONS.users.default },
        children: [],
      },
      { path: '403-forbidden', children: [] },
    ]);

    await component.onSubmit();

    expect(router.url).toBe('/protected');
  });

  it('按权限决定落地页，而不是按角色名或超管标志', async () => {
    await setUp();
    fillValidCredentials();

    // 平台入口可见性由权限集合决定：这里通过真实的 setPermissions 造状态，
    // 而不是打桩 canAccessPlatform——打桩就绕过了"判据是权限而非角色"这件事本身。
    authorization.setPermissions({ permissions: [], isSuperAdmin: true, versionToken: 'r1' });

    await component.onSubmit();
    expect(router.navigate).toHaveBeenCalledWith(['/workspace']);

    (router.navigate as jasmine.Spy).calls.reset();
    authorization.setPermissions({
      permissions: [PERMISSIONS.users.default],
      isSuperAdmin: false,
      versionToken: 'r2',
    });

    await component.onSubmit();
    expect(router.navigate).toHaveBeenCalledWith(['/platform']);
  });
});
