import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { ExternalLogins } from './external-logins';
import { ConfirmService } from '../../../../core/feedback/confirm-service';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { SettingContextService } from '../../../../core/settings/setting-context-service';
import { ExternalLoginsOutputDto } from '../../models/account.dto';
import { AccountService } from '../../services/account-service';

describe('ExternalLogins', () => {
  let fixture: ComponentFixture<ExternalLogins>;
  let account: jasmine.SpyObj<AccountService>;

  const github = {
    provider: 'github',
    link: { id: 'l1', providerUsername: 'octocat', creationTime: '2026-06-01T00:00:00Z' },
  };

  async function render(data: ExternalLoginsOutputDto): Promise<HTMLElement> {
    account.getExternalLogins.and.returnValue(of(data));
    fixture = TestBed.createComponent(ExternalLogins);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    account = jasmine.createSpyObj<AccountService>('AccountService', [
      'getExternalLogins',
      'unlinkExternalLogin',
      'getExternalLinkUrl',
    ]);
    TestBed.configureTestingModule({
      imports: [ExternalLogins],
      providers: [
        { provide: AccountService, useValue: account },
        { provide: ConfirmService, useValue: { open: () => Promise.resolve(true) } },
        {
          provide: SettingContextService,
          useValue: { timeZone: signal<string | undefined>('UTC'), displayLocale: signal('en') },
        },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });
  });

  it('未绑定的提供商给绑定入口，已绑定的给解绑入口', async () => {
    const host = await render({
      hasPassword: true,
      providers: [github, { provider: 'google', link: null }],
    });

    const rows = host.querySelectorAll('[data-testid="external-login-row"]');
    expect(rows[0].querySelector('[data-testid="external-login-unlink"]')).not.toBeNull();
    expect(rows[1].querySelector('[data-testid="external-login-link"]')).not.toBeNull();
  });

  it('没有密码时最后一个绑定不给解绑入口', async () => {
    const host = await render({
      hasPassword: false,
      providers: [github, { provider: 'google', link: null }],
    });

    expect(host.querySelector('[data-testid="external-login-unlink"]')).toBeNull();
  });
});
