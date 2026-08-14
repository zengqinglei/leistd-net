import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
//#if (IncludeLocalization)
import { provideTransloco, TRANSLOCO_LOADER } from '@jsverse/transloco';
//#endif
import { of, throwError } from 'rxjs';

import { Register } from './register';
import { AccountService } from '../../services/account-service';

/**
 * 注册提交路径。
 *
 * 注册是唯一一个匿名可达的写接口，提交前的闸门都在这里：表单校验、验证码令牌、
 * 失败后刷新验证码。任何一道形同虚设，都会在真实站点上被批量利用。
 */
describe('Register', () => {
  let fixture: ComponentFixture<Register>;
  let component: Register;
  let router: Router;
  let accountService: jasmine.SpyObj<AccountService>;
  let queryParams: Record<string, string>;

  async function setUp(): Promise<void> {
    accountService = jasmine.createSpyObj<AccountService>('AccountService', [
      'register',
      'getCaptcha',
      'getSecurityConfig',
      'sendEmailCode',
    ]);
    accountService.register.and.returnValue(of(undefined));
    accountService.getCaptcha.and.returnValue(
      of({ captchaToken: 'token-1', captchaImage: 'data:image/png;base64,' }) as never,
    );
    accountService.getSecurityConfig.and.returnValue(
      of({ enableEmailVerification: false }) as never,
    );

    await TestBed.configureTestingModule({
      imports: [Register],
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
        { provide: AccountService, useValue: accountService },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Register);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);

    spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges();
  }

  function fillValidForm(): void {
    component.registerForm.email().value.set('someone@example.test');
    component.registerForm.username().value.set('someone');
    component.registerForm.captchaCode().value.set('1234');
    component.registerForm.password().value.set('Passw0rd!');
    component.registerForm.confirmPassword().value.set('Passw0rd!');
  }

  beforeEach(() => {
    queryParams = {};
  });

  it('表单非法时不提交注册请求', async () => {
    await setUp();

    await component.onSubmit();

    expect(accountService.register).not.toHaveBeenCalled();
    expect(component.registerForm().touched()).toBeTrue();
    expect(component.isLoading()).toBeFalse();
  });

  it('两次密码不一致时表单非法', async () => {
    await setUp();
    fillValidForm();
    component.registerForm.confirmPassword().value.set('Different1!');

    await component.onSubmit();

    expect(accountService.register).not.toHaveBeenCalled();
  });

  it('缺少验证码令牌时不提交，并复位加载状态', async () => {
    await setUp();
    fillValidForm();
    component.captchaData.set(null);

    await component.onSubmit();

    // 没有令牌就提交，服务端必然拒绝；本地先拦下来才不会白跑一趟并把按钮卡在加载态。
    expect(accountService.register).not.toHaveBeenCalled();
    expect(component.isLoading()).toBeFalse();
  });

  it('注册成功后跳转登录页并带上 returnUrl', async () => {
    queryParams = { returnUrl: '/platform/users' };
    await setUp();
    fillValidForm();

    await component.onSubmit();

    expect(accountService.register).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/auth/login'], {
      queryParams: { returnUrl: '/platform/users' },
    });
  });

  it('没有 returnUrl 时不带空查询参数', async () => {
    await setUp();
    fillValidForm();

    await component.onSubmit();

    expect(router.navigate).toHaveBeenCalledWith(['/auth/login'], { queryParams: undefined });
  });

  it('注册失败时刷新验证码、不跳转，并复位加载状态', async () => {
    await setUp();
    fillValidForm();
    accountService.register.and.returnValue(throwError(() => new Error('captcha mismatch')));
    accountService.getCaptcha.calls.reset();

    await component.onSubmit();

    // 验证码是一次性的：失败后不换一张，用户只能一直提交同一个必然失败的组合。
    expect(accountService.getCaptcha).toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
    expect(component.isLoading()).toBeFalse();
  });
});
