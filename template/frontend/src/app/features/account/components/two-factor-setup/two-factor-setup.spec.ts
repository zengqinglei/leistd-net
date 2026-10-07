import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { TwoFactorSetup } from './two-factor-setup';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AccountService } from '../../services/account-service';

import type { MockedObject } from 'vitest';

/**
 * 开始设置会在服务端生成待启用的密钥（POST），属于写请求：
 * 只在 `ngOnInit` 发起，构造组件本身不改变服务端状态。
 */
describe('TwoFactorSetup', () => {
  let fixture: ComponentFixture<TwoFactorSetup>;
  let account: Pick<MockedObject<AccountService>, 'beginTwoFactorSetup'>;

  const host = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    account = {
      beginTwoFactorSetup: vi.fn().mockName('AccountService.beginTwoFactorSetup'),
    };
    account.beginTwoFactorSetup.mockReturnValue(
      of({
        secret: 'JBSWY3DPEHPK3PXP',
        otpAuthUri: 'otpauth://totp/App:alice?secret=JBSWY3DPEHPK3PXP',
      }),
    );
    TestBed.configureTestingModule({
      imports: [TwoFactorSetup],
      // 关闭本地化时只剩一项，prettier 会要求并成一行；条件块不能随形态换折行方式
      // prettier-ignore
      providers: [
        { provide: AccountService, useValue: account },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });
    fixture = TestBed.createComponent(TwoFactorSetup);
  });

  it('starts the setup on init, not on construction, and only once', async () => {
    expect(account.beginTwoFactorSetup).not.toHaveBeenCalled();

    fixture.detectChanges();
    await fixture.whenStable();

    expect(account.beginTwoFactorSetup).toHaveBeenCalledOnce();
    expect(host().querySelector('[data-testid="two-factor-secret"]')?.textContent?.trim()).toBe(
      'JBSW Y3DP EHPK 3PXP',
    );
  });

  it('labels the code input through the field label', async () => {
    fixture.detectChanges();
    await fixture.whenStable();

    const label = host().querySelector('label[hlmFieldLabel]');
    expect(label?.getAttribute('for')).toBe('two-factor-setup-code');
    expect(host().querySelector('input#two-factor-setup-code')).not.toBeNull();
  });

  it('offers a retry after a failed start and starts again only when asked', async () => {
    account.beginTwoFactorSetup.mockReturnValueOnce(
      throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Server Error' })),
    );
    fixture.detectChanges();
    await fixture.whenStable();

    expect(account.beginTwoFactorSetup).toHaveBeenCalledOnce();
    expect(host().querySelector('[data-testid="two-factor-setup"]')).toBeNull();
    const retry = host().querySelector<HTMLButtonElement>('button[variant="link"]');
    expect(retry, 'retry button should be rendered').not.toBeNull();

    retry!.click();
    await fixture.whenStable();

    expect(account.beginTwoFactorSetup).toHaveBeenCalledTimes(2);
    expect(host().querySelector('[data-testid="two-factor-setup"]')).not.toBeNull();
  });
});
