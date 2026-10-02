import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { TwoFactorChallenge } from './two-factor-challenge';
import { ApplicationHttpError } from '../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AccountService } from '../../services/account-service';

import type { MockedObject } from 'vitest';

function rejection(code: string): ApplicationHttpError {
  return ApplicationHttpError.from(
    new HttpErrorResponse({ status: 401, error: { status: 401, code, title: code } }),
  );
}

describe('TwoFactorChallenge', () => {
  let fixture: ComponentFixture<TwoFactorChallenge>;
  let account: Pick<MockedObject<AccountService>, 'completeTwoFactorLogin'>;
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
    account = {
      completeTwoFactorLogin: vi.fn().mockName('AccountService.completeTwoFactorLogin'),
    };
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

  it('submits the code and notifies the login page on success', async () => {
    account.completeTwoFactorLogin.mockReturnValue(of(undefined));

    type('123456');
    await submit();

    expect(account.completeTwoFactorLogin).toHaveBeenCalledTimes(1);

    expect(account.completeTwoFactorLogin).toHaveBeenCalledWith({
      token: 'challenge-token',
      code: '123456',
    });
    expect(completed).toBe(1);
  });

  // 验证器应用与短信常把验证码显示成"123 456"，整段粘贴进来也要能直接提交
  it('keeps only digits from a pasted separated code and submits once complete', async () => {
    account.completeTwoFactorLogin.mockReturnValue(of(undefined));

    const input = (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
    const clipboardData = new DataTransfer();
    clipboardData.setData('text/plain', '123 456');
    input.dispatchEvent(new ClipboardEvent('paste', { clipboardData }));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(account.completeTwoFactorLogin).toHaveBeenCalledTimes(1);

    expect(account.completeTwoFactorLogin).toHaveBeenCalledWith({
      token: 'challenge-token',
      code: '123456',
    });
    expect(completed).toBe(1);
  });

  it('stays on a wrong code and returns to the password step on expired credentials', async () => {
    account.completeTwoFactorLogin.mockReturnValue(
      throwError(() => rejection('Auth:TwoFactorCodeInvalid')),
    );
    type('000000');
    await submit();
    expect(cancelled).toBe(0);

    account.completeTwoFactorLogin.mockReturnValue(
      throwError(() => rejection('Auth:TwoFactorChallengeExpired')),
    );
    type('000000');
    await submit();
    expect(cancelled).toBe(1);
  });
});
