//#if (OpenIddictServer)
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { isObservable, Observable, of, Subject, throwError } from 'rxjs';

//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { FullPageNavigationService } from '../../../../core/routing/full-page-navigation-service';
import { AuthService } from '../../../../core/services/auth-service';
import { LogoutConfirmationOutputDto } from '../../dtos/account.dto';
import { AccountService } from '../../services/account-service';
import { LogoutConfirm } from './logout-confirm';

import type { MockInstance } from 'vitest';

/**
 * 依赖方发起退出的确认页。
 *
 * 确认必须是一次带防伪令牌的整页表单 POST 回协议端点：改成 XHR 的话，退出完成后回到依赖方的跳转
 * 不会由浏览器跟随；漏掉任何一个字段，服务端都会把它当成未确认、再次要求确认，用户卡在这一页。
 */
describe('LogoutConfirm', () => {
  let fixture: ComponentFixture<LogoutConfirm>;
  let getLogoutConfirmation: ReturnType<typeof vi.fn>;
  let post: MockInstance<FullPageNavigationService['post']>;

  async function render(
    query: Record<string, string>,
    response: LogoutConfirmationOutputDto | Error | Observable<LogoutConfirmationOutputDto>,
  ): Promise<void> {
    getLogoutConfirmation = vi
      .fn()
      .mockReturnValue(
        response instanceof Error
          ? throwError(() => response)
          : isObservable(response)
            ? response
            : of(response),
      );
    TestBed.configureTestingModule({
      imports: [LogoutConfirm],
      providers: [
        provideRouter([{ path: 'auth/logout-expired', children: [] }]),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
        { provide: AccountService, useValue: { getLogoutConfirmation } },
        { provide: AuthService, useValue: { logout: vi.fn() } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(query) } },
        },
      ],
    });
    post = vi
      .spyOn(TestBed.inject(FullPageNavigationService), 'post')
      .mockImplementation(() => undefined);
    fixture = TestBed.createComponent(LogoutConfirm);
    await fixture.whenStable();
  }

  const element = (testId: string) =>
    (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${testId}"]`);

  afterEach(() => fixture?.destroy());

  it('posts the confirmation with its antiforgery token back to the end session endpoint', async () => {
    await render(
      { request_uri: 'urn:request', confirmation: 'protected' },
      {
        isValid: true,
        applicationName: 'BILLING',
        antiforgeryFieldName: '__RequestVerificationToken',
        antiforgeryToken: 'csrf-token',
      },
    );
    expect(getLogoutConfirmation).toHaveBeenCalledWith('urn:request', 'protected');
    expect(element('logout-confirm-description')).not.toBeNull();

    (element('logout-confirm-submit') as HTMLButtonElement).click();

    expect(post).toHaveBeenCalledExactlyOnceWith('/connect/logout', {
      request_uri: 'urn:request',
      confirmation: 'protected',
      __RequestVerificationToken: 'csrf-token',
    });
  });

  // 首次加载：骨架占住说明文字的位置，不放 spinner
  it('shows a text skeleton while the request is checked', async () => {
    await render({ request_uri: 'urn:request', confirmation: 'protected' }, new Subject());

    expect(element('logout-confirm-loading')).not.toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('hlm-spinner')).toBeNull();
  });

  it('sends expired confirmation to the Identity sign-out recovery page', async () => {
    await render({ request_uri: 'urn:request', confirmation: 'stale' }, { isValid: false });

    expect(TestBed.inject(Router).url).toBe('/auth/logout-expired');
    expect(element('logout-confirm-submit')).toBeNull();
  });

  it('reports a failed check as a request failure that can be retried, not as an expired request', async () => {
    await render({ request_uri: 'urn:request', confirmation: 'protected' }, new Error('offline'));

    expect(element('logout-confirm-failed')).not.toBeNull();
    expect(TestBed.inject(Router).url).not.toBe('/auth/logout-expired');
    expect(element('logout-confirm-submit')).toBeNull();

    getLogoutConfirmation.mockReturnValue(
      of({ isValid: true, antiforgeryFieldName: 'f', antiforgeryToken: 't' }),
    );
    (element('logout-confirm-retry') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(element('logout-confirm-failed')).toBeNull();
    expect(element('logout-confirm-submit')).not.toBeNull();
  });

  it('does not ask the server without both references', async () => {
    await render({ confirmation: 'protected' }, { isValid: true });

    expect(getLogoutConfirmation).not.toHaveBeenCalled();
    expect(TestBed.inject(Router).url).toBe('/auth/logout-expired');
  });

  it('stays signed in when cancelled', async () => {
    await render(
      { request_uri: 'urn:request', confirmation: 'protected' },
      { isValid: true, antiforgeryFieldName: 'f', antiforgeryToken: 't' },
    );
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    (element('logout-confirm-cancel') as HTMLButtonElement).click();

    expect(navigate).toHaveBeenCalledWith('/');
    expect(post).not.toHaveBeenCalled();
  });
});
//#endif
