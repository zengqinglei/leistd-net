import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';
import { Observable, of, throwError } from 'rxjs';

import { Login } from './login';
import { permissionGuard } from '../../../../core/guards/permission-guard';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AuthService } from '../../../../core/services/auth-service';
import { AuthorizationService } from '../../../../core/services/authorization-service';
import { SessionContextService } from '../../../../core/services/session-context-service';
import { StartupService } from '../../../../core/services/startup-service';
//#if (LocalIdentity)
import { TenantContextService } from '../../../../core/services/tenant-context-service';
//#endif
//#if (LocalIdentity)
import { TenantByHostOutputDto } from '../../../../shared/dtos/tenant.dto';
//#endif
import { PERMISSIONS } from '../../../../shared/models/permission';
//#if (LocalIdentity)
import { TenantService } from '../../../platform/services/tenant-service';
//#endif
//#if (ExternalLogin)
import { AccountService } from '../../services/account-service';
//#endif

import type { Mock, MockedObject } from 'vitest';

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
  let authService: Pick<
    MockedObject<AuthService>,
    'login' | 'loadUser' | 'clearAuthData' | 'currentUser'
  >;
  let currentUser: WritableSignal<{
    twoFactorSetupRequired?: boolean;
  } | null>;
  let authorization: AuthorizationService;
  let queryParams: Record<string, string>;
  //#if (LocalIdentity)
  // 主机名探测的返回值。默认"域名不表态"，与本机开发一致；租户相关用例逐个覆盖它。
  let byHost: Observable<TenantByHostOutputDto>;
  //#endif

  async function setUp(): Promise<void> {
    currentUser = signal<{
      twoFactorSetupRequired?: boolean;
    } | null>(null);
    authService = {
      login: vi.fn().mockName('AuthService.login'),
      loadUser: vi.fn().mockName('AuthService.loadUser'),
      clearAuthData: vi.fn().mockName('AuthService.clearAuthData'),
      currentUser: currentUser as never,
    };
    // 具体载荷与本用例无关：登录流程只关心"成功/失败"和随后的跳转。
    authService.login.mockReturnValue(of(undefined) as never);
    authService.loadUser.mockReturnValue(of(undefined) as never);

    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en', 'zh-CN']),
        //#endif
        { provide: AuthService, useValue: authService },
        //#if (LocalIdentity)
        // 登录页构造时就会按主机名探测一次租户；不打桩的话它会挂在一个永不返回的请求上，
        // 租户区会一直停在 pending，所有与租户有关的断言都测不到真实分支。
        { provide: TenantService, useValue: { getByHost: () => byHost } },
        //#endif
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
    vi.spyOn(TestBed.inject(SessionContextService), 'establish').mockResolvedValue();

    fixture = TestBed.createComponent(Login);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);

    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture.detectChanges();
  }

  function fillValidCredentials(): void {
    component.loginForm.usernameOrEmail().value.set('admin');
    component.loginForm.password().value.set('Admin@123456');
  }

  beforeEach(() => {
    queryParams = {};
    //#if (LocalIdentity)
    byHost = of({ decision: 'undecided' as const });
    localStorage.clear();
    //#endif
  });
  //#if (LocalIdentity)
  afterEach(() => localStorage.clear());
  //#endif

  /** 页面上显示出来的校验提示（未显示的 hlm-field-error 不渲染内容）。 */
  function shownErrors(): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('hlm-field-error'))
      .map((element) => element.textContent!.trim())
      .filter((text) => text.length > 0);
  }

  //#if (IncludeLocalization)
  /**
   * 校验提示按错误类型取词条（`validation.<kind>`），参数来自校验器给出的错误对象。
   * 提示若在建表单时一次性翻译，切到另一种语言后标签都换了，错误提示还停在旧语言（全功能端到端测试发现）。
   */
  it('shows validation errors by kind, with their parameters, in the active language', async () => {
    await setUp();
    const transloco = TestBed.inject(TranslocoService);
    transloco.setTranslation(
      { validation: { required: 'Required', minLength: 'At least {{minLength}}' } },
      'en',
    );
    transloco.setTranslation(
      { validation: { required: '必填', minLength: '至少 {{minLength}} 位' } },
      'zh-CN',
    );
    component.loginForm.usernameOrEmail().value.set('ab');
    await component.onSubmit();
    await fixture.whenStable();

    expect(shownErrors()).toEqual(['At least 3', 'Required']);

    transloco.setActiveLang('zh-CN');
    await fixture.whenStable();

    expect(shownErrors()).toEqual(['至少 3 位', '必填']);
  });

  // 标签与按钮文案经模板结构指令的 t 取得：切换语言后已渲染的表单要换成新语言。
  // 防不住"文案改回组件里的 translate()"——那一条由 setting-section.spec 钉住。
  it('re-renders form labels and the submit button when the language changes', async () => {
    await setUp();
    const transloco = TestBed.inject(TranslocoService);
    transloco.setTranslation({ account: { login: { password: '密码', submit: '登录' } } }, 'zh-CN');
    const host = fixture.nativeElement as HTMLElement;
    const passwordLabel = () => host.querySelector('label[for="password"]')!.textContent!.trim();
    const submitText = () => host.querySelector('button[type="submit"]')!.textContent!.trim();
    expect(passwordLabel()).toBe('account.login.password');
    expect(submitText()).toBe('account.login.submit');

    transloco.setActiveLang('zh-CN');
    await fixture.whenStable();

    expect(passwordLabel()).toBe('密码');
    expect(submitText()).toBe('登录');
  });

  //#else
  // 不含本地化时提示来自内置英文表（english-text.ts），参数同样取自校验器给出的错误对象
  it('shows validation errors by kind from the built-in English table', async () => {
    await setUp();
    component.loginForm.usernameOrEmail().value.set('ab');
    await component.onSubmit();
    await fixture.whenStable();

    expect(shownErrors()).toEqual(['Must be at least 3 characters.', 'This field is required.']);
  });

  //#endif
  it('does not send a login request when the form is invalid', async () => {
    await setUp();

    // 空表单直接提交：应停在原地并标记为已触碰，让校验信息显示出来。
    await component.onSubmit();

    expect(authService.login).not.toHaveBeenCalled();
    expect(component.loginForm().touched()).toBe(true);
    expect(component.isLoading()).toBe(false);
  });

  it('navigates to a safe local returnUrl after a successful login', async () => {
    queryParams = { returnUrl: '/platform/users' };
    await setUp();
    fillValidCredentials();

    await component.onSubmit();

    expect(authService.login).toHaveBeenCalled();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/platform/users');
  });

  it('switches to the verification code step when two-factor is enabled, without loading the current user or navigating', async () => {
    await setUp();
    authService.login.mockReturnValue(
      of({ requiresTwoFactor: true, twoFactorToken: 'challenge-token' }) as never,
    );
    fillValidCredentials();

    await component.onSubmit();
    fixture.detectChanges();

    expect(authService.loadUser).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[data-testid="two-factor-challenge"]'),
    ).not.toBeNull();
  });

  it('sends a restricted session (organization requires two-factor) straight to the setup page without establishing session context', async () => {
    await setUp();
    authService.loadUser.mockImplementation(() => {
      currentUser.set({ twoFactorSetupRequired: true });
      return of(undefined) as never;
    });
    fillValidCredentials();

    await component.onSubmit();

    expect(router.navigate).toHaveBeenCalledWith(['/auth/two-factor-setup']);
    expect(TestBed.inject(SessionContextService).establish).not.toHaveBeenCalled();
  });

  for (const hostile of ['//evil.example.com', 'https://evil.example.com', 'javascript://evil']) {
    it(`discards an off-site returnUrl: ${hostile}`, async () => {
      queryParams = { returnUrl: hostile };
      await setUp();
      fillValidCredentials();

      await component.onSubmit();

      // 登录成功后被送到外站，是钓鱼链接最省事的一种玩法。
      expect(router.navigateByUrl).not.toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalled();
    });
  }

  it('shows only the failure toast, not the success toast, when establishing the session fails', async () => {
    await setUp();
    fillValidCredentials();
    (TestBed.inject(SessionContextService).establish as Mock).mockRejectedValue(
      new Error('settings unavailable'),
    );
    // toast 是模块级单例对象，组件与此处引用同一个，改它的方法组件立刻看得见。
    const success = vi.spyOn(toast, 'success').mockImplementation(() => '');
    const error = vi.spyOn(toast, 'error').mockImplementation(() => '');

    await component.onSubmit();

    // 先弹「登录成功」再弹「登录失败」的话，用户看到的是两条互相打脸的提示，
    // 而人还停在登录页——提示必须等会话真的建立完再发。
    expect(success).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('does not navigate and resets the loading state when login fails', async () => {
    await setUp();
    fillValidCredentials();
    authService.login.mockReturnValue(throwError(() => new Error('bad credentials')));

    await component.onSubmit();

    expect(router.navigate).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();

    // 加载状态卡住的话，按钮会一直转，用户只能刷新页面。
    expect(component.isLoading()).toBe(false);
  });

  it('loads permissions before navigating when logging in to a protected returnUrl', async () => {
    queryParams = { returnUrl: '/protected' };
    await setUp();
    fillValidCredentials();

    // 用真实 Router + 真实 permissionGuard 跑完整条路：
    // spy 掉 Router 只能验到"调用了 navigateByUrl"，验不到导航之后 guard 怎么判。
    // 进登录页时会话上下文已被清空；若在建立它之前就跳转，
    // guard 会在空权限下判定并把人踢到 403——从深链登录本该落到那个页面。
    vi.mocked(router.navigateByUrl).mockRestore();
    (TestBed.inject(SessionContextService).establish as Mock).mockImplementation(async () => {
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

  it('chooses the landing page by permissions rather than by role name or super-admin flag', async () => {
    await setUp();
    fillValidCredentials();

    // 平台入口可见性由权限集合决定：这里通过真实的 setPermissions 造状态，
    // 而不是打桩 canAccessPlatform——打桩就绕过了"判据是权限而非角色"这件事本身。
    authorization.setPermissions({ permissions: [], isSuperAdmin: true, versionToken: 'r1' });

    await component.onSubmit();
    expect(router.navigate).toHaveBeenCalledWith(['/workspace']);

    (router.navigate as Mock).mockClear();
    authorization.setPermissions({
      permissions: [PERMISSIONS.users.default],
      isSuperAdmin: false,
      versionToken: 'r2',
    });

    await component.onSubmit();
    expect(router.navigate).toHaveBeenCalledWith(['/platform']);
  });
  //#if (LocalIdentity)
  /**
   * 主机名定案的三档结果。
   *
   * 关键在于**"宿主定案"与"域名不表态"必须分开**：两者都当成"没有租户"时，
   * 宿主域上会残留上次记住的租户，而服务端已按宿主处理请求——
   * 界面显示的和实际生效的不是同一个租户上下文，登录会落在用户没选的那一侧。
   */
  describe('tenant resolution by host name', () => {
    // 匿名 by-host 只回租户名：回 id / 展示名 / 启用状态都会泄露"这个租户存在"
    const domainTenant = { name: 'acme' };

    /**
     * 造出"上次记住的租户"。
     *
     * 直接写存储而不是调 `TenantContextService.set`：探测在组件构造时就跑，
     * 得让上下文在服务初始化那一刻就已经有值，否则测不到"探测把它清掉"这件事。
     */
    function rememberTenant(): void {
      localStorage.setItem('app.tenant', JSON.stringify({ key: 'remembered' }));
    }

    it('host name points to a tenant: pins that tenant and disallows manual selection', async () => {
      byHost = of({ decision: 'tenant' as const, tenant: domainTenant });
      await setUp();

      expect(TestBed.inject(TenantContextService).current()?.key).toBe('acme');
      expect(component.tenantLocked()).toBe(true);
      expect(component.tenantSelectionBlocked()).toBe(true);
    });

    it('host name resolves to the host: clears the remembered tenant and disallows selection', async () => {
      byHost = of({ decision: 'host' as const });
      rememberTenant();
      await setUp();

      expect(TestBed.inject(TenantContextService).current()).toBeNull();
      expect(component.tenantLocked()).toBe(true);
    });

    it('host name is undecided: keeps the remembered tenant and still allows manual selection', async () => {
      byHost = of({ decision: 'undecided' as const });
      rememberTenant();
      await setUp();

      expect(TestBed.inject(TenantContextService).current()?.key).toBe('remembered');
      expect(component.tenantLocked()).toBe(false);
      expect(component.tenantSelectionBlocked()).toBe(false);
    });

    // 域名指向的租户不在库里（或已停用）：这个部署当前用不了。
    // 不能退回"让用户自己挑一个"——挑了也会被域名覆盖。
    it('tenant from the host name is unavailable: clears it, stays locked, and shows a message', async () => {
      byHost = of({ decision: 'tenant' as const });
      await setUp();

      expect(TestBed.inject(TenantContextService).current()).toBeNull();
      expect(component.tenantLocked()).toBe(true);
      expect(component.tenantError()).toBeTruthy();
    });

    /**
     * 探测失败不等于"域名不表态"。
     *
     * 未配置子域名格式的部署本来就正常返回 undecided，走不到这条分支；能走到的是
     * "域名指向的租户解析不了"（租户解析中间件直接 404）或后端不可达。把它折进 undecided，
     * 就是在不知道域名会怎么解析的情况下让人手选一个注定被覆盖的租户，然后带着它去登录。
     */
    it('probe failure is not treated as undecided: blocks manual selection and login and explains why', async () => {
      byHost = throwError(() => new Error('offline'));
      await setUp();
      fillValidCredentials();

      expect(component.hostProbe()).toBe('failed');
      expect(component.tenantSelectionBlocked()).toBe(true);
      expect(component.tenantError()).toBeTruthy();

      // 清除也算一次手动改租户：同样不放行，否则用户能在"不知道域名会怎么解析"时
      // 把上下文改成宿主，然后带着它去登录。
      TestBed.inject(TenantContextService).set(domainTenant.name);
      component.clearTenant();
      expect(TestBed.inject(TenantContextService).current()?.key).toBe('acme');

      await component.onSubmit();

      expect(authService.login).not.toHaveBeenCalled();
    });

    // 挡住之后必须给一条出路：瞬时故障不该让人只剩"刷新整页"，尤其表单可能已经填好。
    it('can retry after a probe failure and recovers once the retry gets a decision', async () => {
      byHost = throwError(() => new Error('offline'));
      await setUp();
      expect(component.authBlocked()).toBe(true);

      byHost = of({ decision: 'undecided' as const });
      await component.retryHostProbe();

      expect(component.hostProbe()).toBe('undecided');
      expect(component.authBlocked()).toBe(false);
      expect(component.tenantError()).toBeNull();
      expect(component.tenantSelectionBlocked()).toBe(false);
    });

    /**
     * 探测未回来时不发认证请求。
     *
     * 服务端按主机名解析租户且不接受请求头改写：此时提交，界面上显示着上次记住的租户，
     * 请求却落到域名对应的那个上下文里，而探测响应随后又会把前端状态改掉。
     */
    it('does not send an authentication request while the probe is pending', async () => {
      byHost = new Observable<TenantByHostOutputDto>(() => undefined);
      await setUp();
      fillValidCredentials();

      expect(component.authBlocked()).toBe(true);

      await component.onSubmit();

      expect(authService.login).not.toHaveBeenCalled();
    });

    // 反向的一条：域名已定案时禁止**选择**，但恰恰应该放行登录。
    // 把两件事共用一个条件，就会把子域名部署的登录整个挡死。
    it('blocks selection but still allows login when the host name is decided', async () => {
      byHost = of({ decision: 'tenant' as const, tenant: domainTenant });
      await setUp();
      fillValidCredentials();

      expect(component.tenantSelectionBlocked()).toBe(true);
      expect(component.authBlocked()).toBe(false);

      await component.onSubmit();

      expect(authService.login).toHaveBeenCalled();
    });
    //#if (ExternalLogin)

    // 第三方登录走的是同一条约束：回调最终也落在按主机名解析出的那个上下文里。
    it('does not start external login while the probe is pending', async () => {
      byHost = new Observable<TenantByHostOutputDto>(() => undefined);
      await setUp();
      const externalLogin = vi
        .spyOn(TestBed.inject(AccountService), 'getExternalLoginUrl')
        .mockReturnValue(of());

      component.loginWithGitHub();

      expect(externalLogin).not.toHaveBeenCalled();
    });
    //#endif

    // 探测未回来时不接受手选：它一回来就会覆盖上下文，此时选的会被无声换掉。
    it('disables the tenant area and ignores manual selection while the probe is pending', async () => {
      byHost = new Observable<TenantByHostOutputDto>(() => undefined);
      await setUp();

      expect(component.tenantSelectionBlocked()).toBe(true);

      component.tenantName.set('acme');
      await component.onConfirmTenant();

      expect(TestBed.inject(TenantContextService).current()).toBeNull();
    });
  });
  //#endif
});
