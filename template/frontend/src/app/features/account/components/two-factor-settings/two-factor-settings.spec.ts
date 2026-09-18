import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { TwoFactorSettings } from './two-factor-settings';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AuthService } from '../../../../core/services/auth-service';
import { TwoFactorStatusOutputDto } from '../../models/account.dto';
import { AccountService } from '../../services/account-service';

describe('TwoFactorSettings', () => {
  let fixture: ComponentFixture<TwoFactorSettings>;
  let account: jasmine.SpyObj<AccountService>;

  async function render(status: TwoFactorStatusOutputDto): Promise<HTMLElement> {
    account.getTwoFactorStatus.and.returnValue(of(status));
    fixture = TestBed.createComponent(TwoFactorSettings);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    account = jasmine.createSpyObj<AccountService>('AccountService', [
      'getTwoFactorStatus',
      'beginTwoFactorSetup',
    ]);
    TestBed.configureTestingModule({
      imports: [TwoFactorSettings],
      providers: [
        { provide: AccountService, useValue: account },
        { provide: AuthService, useValue: { loadUser: () => of(undefined) } },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });
  });

  it('未启用时给出启用入口，没有停用入口', async () => {
    const host = await render({ enabled: false, recoveryCodesLeft: 0, requiredByPolicy: false });

    expect(host.querySelector('[data-testid="two-factor-turn-on"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="two-factor-turn-off"]')).toBeNull();
  });

  it('组织要求两步验证时不给停用入口', async () => {
    const host = await render({ enabled: true, recoveryCodesLeft: 8, requiredByPolicy: true });

    expect(host.querySelector('[data-testid="two-factor-on"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="two-factor-turn-off"]')).toBeNull();
    expect(host.querySelector('[data-testid="recovery-codes-left"]')).not.toBeNull();
  });
});
