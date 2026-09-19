import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { TwoFactorChallenge } from './two-factor-challenge';
import { ApplicationHttpError } from '../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AccountService } from '../../services/account-service';

function rejection(code: string): ApplicationHttpError {
  return ApplicationHttpError.from(
    new HttpErrorResponse({ status: 401, error: { status: 401, code, title: code } }),
  );
}

describe('TwoFactorChallenge', () => {
  let fixture: ComponentFixture<TwoFactorChallenge>;
  let account: jasmine.SpyObj<AccountService>;
  let completed: number;
  let cancelled: number;

  function type(value: string): void {
    const input = (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  async function submit(): Promise<void> {
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    account = jasmine.createSpyObj<AccountService>('AccountService', ['completeTwoFactorLogin']);
    TestBed.configureTestingModule({
      imports: [TwoFactorChallenge],
      // prettier-ignore
      providers: [
        { provide: AccountService, useValue: account },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });
    fixture = TestBed.createComponent(TwoFactorChallenge);
    fixture.componentRef.setInput('token', 'challenge-token');
    completed = 0;
    cancelled = 0;
    fixture.componentInstance.completed.subscribe(() => completed++);
    fixture.componentInstance.cancelled.subscribe(() => cancelled++);
    fixture.detectChanges();
  });

  it('提交验证码，通过后通知登录页', async () => {
    account.completeTwoFactorLogin.and.returnValue(of(undefined));

    type('123456');
    await submit();

    expect(account.completeTwoFactorLogin).toHaveBeenCalledOnceWith({
      token: 'challenge-token',
      code: '123456',
    });
    expect(completed).toBe(1);
  });

  // 验证器应用与短信常把验证码显示成"123 456"，整段粘贴进来也要能直接提交
  it('粘贴带分隔的验证码时只保留数字，输满即提交', async () => {
    account.completeTwoFactorLogin.and.returnValue(of(undefined));

    const input = (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
    const clipboardData = new DataTransfer();
    clipboardData.setData('text/plain', '123 456');
    input.dispatchEvent(new ClipboardEvent('paste', { clipboardData }));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(account.completeTwoFactorLogin).toHaveBeenCalledOnceWith({
      token: 'challenge-token',
      code: '123456',
    });
    expect(completed).toBe(1);
  });

  it('验证码错误时留在这一步；凭据失效时回到密码那一步', async () => {
    account.completeTwoFactorLogin.and.returnValue(
      throwError(() => rejection('Auth:TwoFactorCodeInvalid')),
    );
    type('000000');
    await submit();
    expect(cancelled).toBe(0);

    account.completeTwoFactorLogin.and.returnValue(
      throwError(() => rejection('Auth:TwoFactorChallengeExpired')),
    );
    type('000000');
    await submit();
    expect(cancelled).toBe(1);
  });
});
