//#if (LocalIdentity)
import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { throwError, Observable, of } from 'rxjs';
import { userEvent } from 'vitest/browser';

//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import {
  renderSubmission,
  rejectedField,
} from '../../../../../../core/validation/form-submission.testing';
import {
  CreateOpenApplicationInputDto,
  OpenApplicationOutputDto,
  OpenApplicationScopeOutputDto,
  UpdateOpenApplicationInputDto,
} from '../../../../dtos/open-application.dto';
import { OpenApplicationEditDialog } from './open-application-edit-dialog';

/**
 * 下拉触发器与选项文案一致：不传 `itemToString` 时触发器显示原始值，只有点开、选中再读触发器
 * 才暴露得出来。
 */
@Component({
  imports: [OpenApplicationEditDialog],
  template: `
    <app-open-application-edit-dialog
      [(visible)]="visible"
      [application]="application()"
      [saveAction]="saveAction"
      [editing]="editing()"
      [loadError]="loadError()"
      [scopesLoading]="scopesLoading()"
      [scopesError]="scopesError()"
      [scopes]="scopes()"
    />
  `,
})
class HostComponent {
  readonly visible = signal(true);
  readonly editing = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly scopesLoading = signal(false);
  readonly scopesError = signal<string | null>(null);
  readonly scopes = signal<OpenApplicationScopeOutputDto[]>([
    {
      name: 'identity-api',
      displayName: 'Identity API',
      audience: 'identity-api',
      machineOnly: false,
    },
  ]);
  readonly application = signal<OpenApplicationOutputDto | null>(null);
  readonly saved: (CreateOpenApplicationInputDto | UpdateOpenApplicationInputDto)[] = [];
  request: Observable<unknown> = of(undefined);
  readonly saveAction = (dto: CreateOpenApplicationInputDto | UpdateOpenApplicationInputDto) => {
    this.saved.push(dto);
    return this.request;
  };
}

function registered(sessionBound: boolean | null): OpenApplicationOutputDto {
  return {
    id: 'bff',
    clientId: 'bff',
    applicationType: 'web',
    clientType: 'confidential',
    redirectUris: ['https://bff.example.test/signin-oidc'],
    postLogoutRedirectUris: [],
    permissions: ['ept:authorization', 'ept:token', 'gt:authorization_code', 'rst:code'],
    requirements: [],
    settings: {},
    properties: {},
    hasClientSecret: true,
    sessionBound,
    creationTime: '2026-05-01T09:00:00Z',
  };
}

describe('OpenApplicationEditDialog', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      // prettier-ignore
      providers: [
                provideZonelessChangeDetection(),
                //#if (IncludeLocalization)
                ...provideTranslocoTesting(['en']),
                //#endif
            ],
    });

    fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
  });

  afterEach(() => fixture.destroy());

  async function selectFirstOtherOption(triggerId: string): Promise<{
    optionText: string;
    triggerText: string;
  }> {
    const trigger = document.getElementById(triggerId);
    expect(trigger, `${triggerId} should be rendered`).not.toBeNull();

    await userEvent.click(trigger!);
    await expect.poll(() => trigger!.getAttribute('aria-expanded')).toBe('true');
    await fixture.whenStable();

    const options = Array.from(document.querySelectorAll<HTMLElement>('[role="option"]'));
    expect(options.length, `${triggerId} should offer options`).toBeGreaterThan(1);

    const option =
      options.find((item) => item.getAttribute('aria-selected') !== 'true') ?? options[0];
    const optionText = option.textContent?.trim() ?? '';

    option.click();
    await fixture.whenStable();

    return {
      optionText,
      triggerText: document.getElementById(triggerId)?.textContent?.trim() ?? '',
    };
  }

  function dialog(): OpenApplicationEditDialog {
    return fixture.debugElement.children[0].componentInstance as OpenApplicationEditDialog;
  }

  async function edit(application: OpenApplicationOutputDto): Promise<void> {
    fixture.componentInstance.application.set(application);
    await fixture.whenStable();
  }

  async function typeClientId(value: string): Promise<void> {
    const input = document.getElementById('application-client-id') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  }

  async function toggleSessionBound(): Promise<void> {
    document.getElementById('application-session-bound')!.click();
    await fixture.whenStable();
  }

  // 开关的标签经 for 关联到开关本身（点标签即切换、读屏读出名称），说明与它同属一个字段
  it('labels and describes the session binding switch', () => {
    const label = document.querySelector('label[for="application-session-bound"]');
    expect(label).not.toBeNull();
    const field = label!.closest('[hlmField]')!;
    expect(field.contains(document.getElementById('application-session-bound'))).toBe(true);

    const description = field.querySelector('[hlmFieldDescription]')?.textContent?.trim();
    //#if (IncludeLocalization)
    // 测试不装词条，缺失的键原样渲染：正好能看出读的是哪一条
    expect(description).toBe('openApp.sessionBound.hint');
    //#else
    expect(description).toMatch(/^Authorization codes and refresh tokens stop working/);
    //#endif
    // 读屏软件聚焦开关时连同说明一起读
    const describedBy = field.querySelector('[role="switch"]')?.getAttribute('aria-describedby');
    expect(describedBy).toBeTruthy();
    expect(document.getElementById(describedBy!)?.textContent?.trim()).toBe(description);
  });

  it('suggests the actual BFF callbacks only when their lists are empty', async () => {
    dialog().applicationForm.homeUrl().value.set('https://orders.test/base/');
    dialog().suggestCallbacks();
    expect(dialog().applicationForm.redirectUris().value()).toEqual([
      'https://orders.test/base/api/v1/auth/signin',
    ]);
    expect(dialog().applicationForm.postLogoutRedirectUris().value()).toEqual([
      'https://orders.test/base/api/v1/auth/signout',
    ]);
    dialog().applicationForm.homeUrl().value.set('https://other.test/');
    dialog().suggestCallbacks();
    expect(dialog().applicationForm.redirectUris().value()).toEqual([
      'https://orders.test/base/api/v1/auth/signin',
    ]);
    expect(dialog().applicationForm.postLogoutRedirectUris().value()).toEqual([
      'https://orders.test/base/api/v1/auth/signout',
    ]);
  });

  it('requires a valid home URL only when the application is shown in the switcher', async () => {
    expect(dialog().applicationForm.homeUrl().valid()).toBe(true);
    dialog().setShowInSwitcher(true);
    expect(dialog().applicationForm.homeUrl().invalid()).toBe(true);
    for (const value of ['/relative', 'http://orders.test/', 'https://bad host.test/']) {
      dialog().applicationForm.homeUrl().value.set(value);
      expect(dialog().applicationForm.homeUrl().invalid()).toBe(true);
    }
    dialog().applicationForm.homeUrl().value.set('https://orders.test/');
    expect(dialog().applicationForm.homeUrl().valid()).toBe(true);
    await typeClientId('orders');
    await dialog().save();
    expect(fixture.componentInstance.saved).toContainEqual(
      expect.objectContaining({ homeUrl: 'https://orders.test/', showInSwitcher: true }),
    );
  });

  it('binds new browser applications to the sign-in session by default', async () => {
    await typeClientId('spa');
    dialog().save();

    expect(fixture.componentInstance.saved).toEqual([
      expect.objectContaining({ clientId: 'spa', sessionBound: true }),
    ]);
  });

  it('keeps the registered choice when editing instead of resetting it', async () => {
    await edit(registered(false));
    dialog().save();

    expect(fixture.componentInstance.saved).toEqual([
      expect.objectContaining({ sessionBound: false }),
    ]);
  });

  it('requires an explicit choice for applications registered before session binding', async () => {
    await edit(registered(null));
    expect(document.querySelector('[data-testid="session-bound-unset"]')).not.toBeNull();

    dialog().save();
    expect(fixture.componentInstance.saved).toEqual([]);

    await toggleSessionBound();
    expect(document.querySelector('[data-testid="session-bound-unset"]')).toBeNull();
    dialog().save();
    expect(fixture.componentInstance.saved).toEqual([
      expect.objectContaining({ sessionBound: true }),
    ]);
  });

  it('does not bind desktop or machine clients from their templates', async () => {
    dialog().applyTemplate('desktop');
    await fixture.whenStable();
    dialog().save();
    dialog().applyTemplate('service');
    await typeClientId('machine');
    dialog().save();

    expect(fixture.componentInstance.saved).toEqual([
      expect.objectContaining({ applicationType: 'native', sessionBound: false }),
      expect.objectContaining({ applicationType: 'service', sessionBound: false }),
    ]);
  });

  async function openPermissionOptions(): Promise<void> {
    const trigger = document.getElementById('application-permissions')!;
    await userEvent.click(trigger);
    await expect.poll(() => trigger.getAttribute('aria-expanded')).toBe('true');
    await fixture.whenStable();
  }

  function permissionOption(name: string): HTMLElement {
    const option = Array.from(document.querySelectorAll<HTMLElement>('[role="option"]')).find(
      (item) => item.querySelector('.text-muted-foreground')?.textContent?.trim() === name,
    );
    expect(option, `Permission option ${name}`).toBeDefined();
    return option!;
  }

  it('grants the Identity service scope when creating a client from the service template', async () => {
    const template = document.getElementById('application-template')!;
    await userEvent.click(template);
    await expect.poll(() => template.getAttribute('aria-expanded')).toBe('true');
    const templates = Array.from(document.querySelectorAll<HTMLElement>('[role="option"]'));
    const service = templates.find((option) => /service/i.test(option.textContent ?? ''));
    expect(service).toBeDefined();
    await userEvent.click(service!);
    await typeClientId('orders-machine');

    await openPermissionOptions();
    expect(permissionOption('gt:client_credentials').getAttribute('aria-disabled')).toBe('true');
    await userEvent.click(permissionOption('scp:identity-api'));
    await dialog().save();

    expect(fixture.componentInstance.saved).toEqual([
      expect.objectContaining({
        clientId: 'orders-machine',
        applicationType: 'service',
        clientType: 'confidential',
        permissions: ['ept:token', 'gt:client_credentials', 'scp:identity-api'],
      }),
    ]);
  });

  it('lets an administrator remove a retired scope from a registered service client', async () => {
    await edit({
      ...registered(false),
      applicationType: 'service',
      permissions: [
        'ept:token',
        'gt:client_credentials',
        'scp:tenant-routing.read',
        'scp:identity-api',
      ],
    });

    await openPermissionOptions();
    await userEvent.click(permissionOption('scp:tenant-routing.read'));
    await dialog().save();

    expect(fixture.componentInstance.saved).toEqual([
      expect.objectContaining({
        permissions: ['ept:token', 'gt:client_credentials', 'scp:identity-api'],
      }),
    ]);
  });

  for (const triggerId of ['application-type', 'application-client-type']) {
    it(`shows the picked option's own label in ${triggerId}`, async () => {
      const { optionText, triggerText } = await selectFirstOtherOption(triggerId);

      expect(optionText).not.toBe('');
      expect(triggerText).toBe(optionText);
    });
  }

  it('review R1 keeps failed detail in edit mode and blocks saving until retry', async () => {
    const retry = vi.fn();
    dialog().retry.subscribe(retry);
    fixture.componentInstance.editing.set(true);
    fixture.componentInstance.loadError.set('Detail unavailable');
    await fixture.whenStable();
    expect(document.querySelector('form')).toBeNull();
    expect(document.body.textContent).toContain('Detail unavailable');
    await dialog().save();
    expect(fixture.componentInstance.saved).toEqual([]);
    document.querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    expect(retry).toHaveBeenCalledOnce();
  });
  it('review R1 shows failed authorization options with retry and blocks saving', async () => {
    // 首次加载：骨架占住授权选择框的位置，不在正文末尾放裸 spinner；失败也在这个字段里
    fixture.componentInstance.scopesLoading.set(true);
    await fixture.whenStable();
    const field = document
      .querySelector('label[for="application-permissions"]')!
      .closest('[hlmField]')!;
    expect(field.querySelector('[data-testid="application-permissions-loading"]')).not.toBeNull();
    expect(document.querySelector('[data-dialog-body] hlm-spinner')).toBeNull();
    fixture.componentInstance.scopesLoading.set(false);
    fixture.componentInstance.scopesError.set('Options unavailable');
    await fixture.whenStable();
    expect(field.querySelector('[role="alert"]')?.textContent).toContain('Options unavailable');
    expect(
      document.querySelector<HTMLButtonElement>('hlm-dialog-footer button[type="submit"]')!
        .disabled,
    ).toBe(true);
    await dialog().save();
    expect(fixture.componentInstance.saved).toEqual([]);
  });

  it('review R1 normalizes application identifiers and focuses local and server errors', async () => {
    await renderSubmission(fixture, () => dialog().save());
    expect(document.activeElement?.id).toBe('application-client-id');
    dialog().applicationForm.sessionBound().value.set(false);
    dialog().applicationForm.clientId().value.set('  browser-client  ');
    dialog().applicationForm.displayName().value.set('  Browser client  ');
    fixture.componentInstance.request = throwError(() => rejectedField('ClientId'));
    await renderSubmission(fixture, () => dialog().save());
    expect(fixture.componentInstance.saved[0]).toMatchObject({
      clientId: 'browser-client',
      displayName: 'Browser client',
    });
    expect(document.activeElement?.id).toBe('application-client-id');
  });
});
//#endif
