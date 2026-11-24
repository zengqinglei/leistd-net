import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
//#if (IncludeLocalization)
import { TranslocoLoader } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';
import { Observable, of, Subject, throwError } from 'rxjs';

import { ApplicationHttpError } from '../../../../../../core/errors/application-http-error';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { AuthService } from '../../../../../../core/services/auth-service';
import { SettingService } from '../../../../../../core/settings/setting-service';
import {
  renderSubmission,
  rejectedField,
} from '../../../../../../core/validation/form-submission.testing';
import { EmailTest } from './email-test';

import type { Mock } from 'vitest';
//#if (IncludeLocalization)

/**
 * 提示里断言的英文词条，与 `en.json` 一致：其余文案回落成键名。不含本地化时同样的句子来自组件的内联英文表，
 * 两种形态断言同一句话。
 */
const texts: TranslocoLoader = {
  getTranslation: () =>
    of({
      settings: {
        emailTest: {
          sent: 'Test email sent',
          sentDescription: 'Delivered to {{to}}. Check the inbox',
          failed: "The test email wasn't sent",
          failure: {
            unreachable: "Couldn't reach the SMTP server. Check the host, port and TLS setting",
            authRejected: 'The SMTP server rejected the sign-in. Check the username and password',
            rejected:
              'The SMTP server refused the message. Check the sender address and the recipient',
            sendFailed:
              "Couldn't connect, sign in or deliver through the SMTP server. Check the host, port, TLS and credentials; the server logs have the details",
          },
        },
      },
    }),
};
//#endif

/**
 * 测试发信：用当前生效的邮件参数发一封信。先等本组写入落定再发；管理员靠失败原因改参数，
 * 所以失败提示按错误码给出具体原因并保留到手动关闭，而不是一句"请求失败"。
 */
describe('EmailTest', () => {
  let fixture: ComponentFixture<EmailTest>;
  let sendTestEmail: Mock<(to: string) => Observable<void>>;
  let settle: Mock<() => Promise<boolean>>;

  function recipient(): HTMLInputElement {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      '[data-testid="email-test-to"]',
    )!;
  }

  function sendButton(): HTMLButtonElement {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '[data-testid="email-test-send"]',
    )!;
  }

  async function typeRecipient(value: string): Promise<void> {
    recipient().value = value;
    recipient().dispatchEvent(new Event('input'));
    await fixture.whenStable();
  }

  async function submit(): Promise<void> {
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();
  }

  beforeEach(async () => {
    sendTestEmail = vi.fn().mockName('SettingService.sendTestEmail');
    sendTestEmail.mockReturnValue(of(undefined));
    settle = vi.fn().mockName('settle').mockResolvedValue(true);
    vi.spyOn(toast, 'success').mockImplementation(() => '');
    vi.spyOn(toast, 'error').mockImplementation(() => '');

    TestBed.configureTestingModule({
      imports: [EmailTest],
      // prettier-ignore
      providers: [
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en'], texts),
        //#endif
        { provide: SettingService, useValue: { sendTestEmail } },
        {
          provide: AuthService,
          useValue: { currentUser: signal({ email: 'admin@example.test' }) },
        },
      ],
    });

    fixture = TestBed.createComponent(EmailTest);
    fixture.componentRef.setInput('settle', settle);
    await fixture.whenStable();
  });

  afterEach(() => fixture.destroy());

  it('addresses the test email to the signed-in user by default', async () => {
    expect(recipient().value).toBe('admin@example.test');
    // 没有可见标签，靠 aria-label 给输入框一个可读的名字
    expect(recipient().getAttribute('aria-label')).toBeTruthy();
    await expect.poll(() => sendButton().disabled).toBe(false);
  });

  it.each(['', 'admin', 'a b@example.test'])('does not send to %j', async (address) => {
    await typeRecipient(address);

    expect(sendButton().disabled).toBe(false);
    await submit();
    expect(sendTestEmail).not.toHaveBeenCalled();
  });

  it('sends to the typed address and confirms', async () => {
    await typeRecipient('ops@example.test');

    sendButton().click();
    await fixture.whenStable();

    expect(sendTestEmail).toHaveBeenCalledExactlyOnceWith('ops@example.test');
    expect(toast.success).toHaveBeenCalledExactlyOnceWith('Test email sent', {
      description: 'Delivered to ops@example.test. Check the inbox',
    });
    expect(toast.error).not.toHaveBeenCalled();
    await expect.poll(() => sendButton().disabled).toBe(false);
  });

  // 点发送时输入框失焦提交的那次写入还在排队：落定之前不发，发送按钮保持进行中
  it('waits for the group writes to settle before sending', async () => {
    let release!: (settled: boolean) => void;
    settle.mockReturnValue(new Promise<boolean>((resolve) => (release = resolve)));

    sendButton().click();
    await fixture.whenStable();
    expect(settle).toHaveBeenCalledOnce();
    expect(sendTestEmail).not.toHaveBeenCalled();
    expect(sendButton().disabled).toBe(true);

    release(true);
    await expect.poll(() => sendTestEmail.mock.calls.length).toBe(1);
    await expect.poll(() => sendButton().disabled).toBe(false);
  });

  it('does not send when the group still has unsaved settings', async () => {
    settle.mockResolvedValue(false);

    sendButton().click();
    await fixture.whenStable();

    await expect.poll(() => sendButton().disabled).toBe(false);
    expect(sendTestEmail).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('blocks a second send while the first is in flight', async () => {
    const response = new Subject<void>();
    sendTestEmail.mockReturnValue(response);

    sendButton().click();
    await fixture.whenStable();
    expect(sendButton().disabled).toBe(true);
    await submit();

    expect(sendTestEmail).toHaveBeenCalledOnce();

    response.next();
    response.complete();
    await fixture.whenStable();
    await expect.poll(() => sendButton().disabled).toBe(false);
  });

  // 后端以 422 + 错误码返回（5xx 写请求会被当成"可能已生效"、只剩通用文案）；原因取本地词条，不取服务端文案
  it('explains the failure by its error code and keeps the toast until closed', async () => {
    const rejected = ApplicationHttpError.from(
      new HttpErrorResponse({
        status: 422,
        error: { code: 'AppSetting:TestEmailFailed', detail: 'Server-side wording' },
      }),
      { method: 'POST' },
    );
    sendTestEmail.mockReturnValue(throwError(() => rejected));
    await typeRecipient('ops@example.test');

    sendButton().click();
    await fixture.whenStable();

    expect(toast.error).toHaveBeenCalledExactlyOnceWith("The test email wasn't sent", {
      description:
        "Couldn't connect, sign in or deliver through the SMTP server. Check the host, port, TLS and credentials; the server logs have the details",
      duration: Number.POSITIVE_INFINITY,
      closeButton: true,
    });
    expect(toast.success).not.toHaveBeenCalled();
    expect(recipient().value).toBe('ops@example.test');
    await expect.poll(() => sendButton().disabled).toBe(false);
  });

  // 后端按发信端报告的失败类别给码：提示要指明该查地址与端口、用户名与口令，还是发件地址与收件人
  it.each([
    [
      'AppSetting:TestEmailUnreachable',
      "Couldn't reach the SMTP server. Check the host, port and TLS setting",
    ],
    [
      'AppSetting:TestEmailAuthRejected',
      'The SMTP server rejected the sign-in. Check the username and password',
    ],
    [
      'AppSetting:TestEmailRejected',
      'The SMTP server refused the message. Check the sender address and the recipient',
    ],
  ])('tells what to check for %s', async (code, reason) => {
    const rejected = ApplicationHttpError.from(
      new HttpErrorResponse({ status: 422, error: { code, detail: 'Server-side wording' } }),
      { method: 'POST' },
    );
    sendTestEmail.mockReturnValue(throwError(() => rejected));

    sendButton().click();
    await fixture.whenStable();

    expect(toast.error).toHaveBeenCalledExactlyOnceWith("The test email wasn't sent", {
      description: reason,
      duration: Number.POSITIVE_INFINITY,
      closeButton: true,
    });
    await expect.poll(() => sendButton().disabled).toBe(false);
  });

  it('falls back to the general reason for failures without a known code', async () => {
    const uncertain = ApplicationHttpError.from(new HttpErrorResponse({ status: 502 }), {
      method: 'POST',
      uncertainResultMessage: 'It may have been sent. Check before trying again.',
    });
    sendTestEmail.mockReturnValue(throwError(() => uncertain));

    sendButton().click();
    await fixture.whenStable();

    expect(toast.error).toHaveBeenCalledOnce();
    expect(vi.mocked(toast.error).mock.calls[0][1]).toMatchObject({
      description: 'It may have been sent. Check before trying again.',
      duration: Number.POSITIVE_INFINITY,
    });
  });

  it('review R1 shows invalid recipient errors and focuses local and server rejection', async () => {
    await typeRecipient('bad address');
    await renderSubmission(fixture, () => submit());
    expect(document.activeElement).toBe(recipient());
    const error = (fixture.nativeElement as HTMLElement).querySelector(
      'hlm-field-error',
    ) as HTMLElement;
    expect(error.hidden).toBe(false);
    await typeRecipient(' good@example.com ');
    sendTestEmail.mockReturnValue(throwError(() => rejectedField('To')));
    await renderSubmission(fixture, () => submit());
    expect(sendTestEmail).toHaveBeenCalledWith('good@example.com');
    expect(document.activeElement).toBe(recipient());
    const serverError = (fixture.nativeElement as HTMLElement).querySelector(
      'hlm-field-error',
    ) as HTMLElement;
    expect(serverError.hidden).toBe(false);
    expect(serverError.textContent).toContain('Server rejected this field');
  });
});
