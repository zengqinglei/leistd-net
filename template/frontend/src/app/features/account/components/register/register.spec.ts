import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { Register } from './register';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
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

  async function setUp(enableEmailVerification = false): Promise<void> {
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
    accountService.getSecurityConfig.and.returnValue(of({ enableEmailVerification }) as never);
    accountService.sendEmailCode.and.returnValue(
      of({
        challengeId: '11111111-1111-1111-1111-111111111111',
        expiresInSeconds: 300,
        retryAfterSeconds: 37,
      }) as never,
    );

    await TestBed.configureTestingModule({
      imports: [Register],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
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
    component.registerForm.password().value.set('RegisterSpec!Pw1');
    component.registerForm.confirmPassword().value.set('RegisterSpec!Pw1');
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
    // 长度合法但与上面不同：本用例测的是"两次不一致"，
    // 不该因为长度不足而由另一条规则拒绝——那样断言会通过，但通过的原因是错的
    component.registerForm.confirmPassword().value.set('RegisterSpec!Pw2');

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

  it('发送邮箱验证码后保存 challenge，并按服务端返回值倒计时', async () => {
    await setUp(true);
    fillValidForm();

    await component.sendEmailCode();

    expect(component.countdown()).toBe(37);
  });

  it('开启邮箱验证时以嵌套 challenge 契约提交注册', async () => {
    await setUp(true);
    fillValidForm();
    await component.sendEmailCode();
    component.registerForm.emailVerificationCode().value.set('123456');

    await component.onSubmit();

    expect(accountService.register).toHaveBeenCalledWith(
      jasmine.objectContaining({
        emailVerification: {
          challengeId: '11111111-1111-1111-1111-111111111111',
          code: '123456',
        },
      }),
    );
  });

  it('发送 challenge 后修改邮箱会作废原 challenge 并拒绝提交', async () => {
    await setUp(true);
    fillValidForm();
    await component.sendEmailCode();
    component.registerForm.emailVerificationCode().value.set('123456');

    component.registerForm.email().value.set('changed@example.test');
    fixture.detectChanges();
    await fixture.whenStable();
    await component.onSubmit();

    expect(accountService.register).not.toHaveBeenCalled();
  });
});
