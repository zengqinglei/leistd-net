import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import {
  CreateWebhookSubscriptionInputDto,
  WebhookSubscriptionOutputDto,
} from '../../../../dtos/webhook.dto';
import { WebhookSubscriptionDialog } from './webhook-subscription-dialog';

const EVENT_TYPES = [
  { name: 'webhook.test', group: 'webhooks' },
  { name: 'user.created', group: 'users' },
  { name: 'user.disabled', group: 'users' },
];

/** 订阅对话框：表单模型到入参的映射（全部事件、请求头）与可选事件。 */
describe('WebhookSubscriptionDialog', () => {
  let fixture: ComponentFixture<WebhookSubscriptionDialog>;
  let component: WebhookSubscriptionDialog;
  let saved: CreateWebhookSubscriptionInputDto | undefined;

  beforeEach(async () => {
    saved = undefined;
    await TestBed.configureTestingModule({
      imports: [WebhookSubscriptionDialog],
      //#if (IncludeLocalization)
      providers: [...provideTranslocoTesting(['en'])],
      //#endif
    }).compileComponents();
    TestBed.inject(AccountPreferencesService);
    fixture = TestBed.createComponent(WebhookSubscriptionDialog);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('eventTypes', EVENT_TYPES);
    fixture.componentRef.setInput('saveAction', (dto: CreateWebhookSubscriptionInputDto) => {
      saved = dto;
      return of(undefined);
    });
  });

  function open(subscription: WebhookSubscriptionOutputDto | null = null): void {
    fixture.componentRef.setInput('subscription', subscription);
    component.visible.set(true);
    fixture.detectChanges();
  }

  function fill(name: string, url: string): void {
    const form = component.subscriptionForm;
    form.name().value.set(name);
    form.url().value.set(url);
  }

  it('lists the registered events by group without the built-in test event', () => {
    open();

    expect(component.eventGroups()).toEqual([
      { group: 'users', types: [EVENT_TYPES[1], EVENT_TYPES[2]] },
    ]);
  });

  it('submits ["*"] for all events and drops the individual choices', async () => {
    open();
    fill('ERP', 'https://erp.example.test/hooks');
    component.toggleEvent('user.created', true);

    component.setAllEvents(true);
    await component.save();

    expect(saved?.eventTypes).toEqual(['*']);
    expect(component.isEventSelected('user.disabled')).toBe(true);
  });

  it('submits the custom headers as a map and ignores blank rows', async () => {
    open();
    fill('ERP', 'https://erp.example.test/hooks');
    component.toggleEvent('user.created', true);
    const headers = document.querySelector<HTMLElement>('[data-testid="webhook-headers"]')!;
    const addHeader = headers.querySelector<HTMLButtonElement>(':scope > button')!;
    addHeader.click();
    addHeader.click();
    await fixture.whenStable();

    const inputs = headers.querySelectorAll<HTMLInputElement>('input');
    expect(inputs).toHaveLength(4);
    const [name, value] = inputs;
    name.value = ' X-Tenant-Code ';
    name.dispatchEvent(new Event('input', { bubbles: true }));
    value.value = 'acme';
    value.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();

    headers.closest('form')!.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await fixture.whenStable();

    expect(saved?.headers).toEqual({ 'X-Tenant-Code': 'acme' });
    expect(saved?.description).toBeNull();
  });

  it('does not submit without any event or with a non-http URL', async () => {
    open();
    fill('ERP', 'ftp://erp.example.test');

    await component.save();

    expect(saved).toBeUndefined();
    expect(
      component.subscriptionForm
        .eventTypes()
        .errors()
        .map((error) => error.kind),
    ).toEqual(['required']);
    expect(
      component.subscriptionForm
        .url()
        .errors()
        .map((error) => error.kind),
    ).toEqual(['uri']);
  });

  // 高度在打开时确定：详情与事件类型加载完成后弹窗高度不变；事件类型加载中是行骨架，不放裸 spinner
  it('keeps the dialog height from loading to the loaded form', async () => {
    fixture.componentRef.setInput('loading', true);
    fixture.componentRef.setInput('eventTypesLoading', true);
    open();
    await fixture.whenStable();
    const content = document.querySelector<HTMLElement>('[data-slot="dialog-content"]')!;
    expect(content.querySelector('app-dialog-loading')).not.toBeNull();
    const height = content.offsetHeight;

    fixture.componentRef.setInput('loading', false);
    await fixture.whenStable();
    const events = content.querySelector('[data-testid="webhook-event-types"]')!;
    expect(events.querySelector('app-list-loading')).not.toBeNull();
    expect(events.querySelector('hlm-spinner')).toBeNull();
    expect(content.offsetHeight).toBe(height);
    // 事件项是单行勾选：骨架每行一个勾选框加一行文字，勾选框与最终的事件勾选框同一缩进
    const skeletonRows = [...events.querySelectorAll('app-list-loading [aria-hidden="true"]')];
    expect(skeletonRows.length).toBeGreaterThan(0);
    for (const row of skeletonRows)
      expect(row.querySelectorAll('[hlmSkeleton]:not(.size-4)')).toHaveLength(1);
    const skeletonLeft = skeletonRows[0].querySelector('.size-4')!.getBoundingClientRect().left;

    fixture.componentRef.setInput('eventTypesLoading', false);
    await fixture.whenStable();
    const eventCheckbox = content.querySelector('[id="webhook-event-user.created"]')!;
    expect(eventCheckbox).not.toBeNull();
    expect(eventCheckbox.getBoundingClientRect().left).toBe(skeletonLeft);
    expect(content.offsetHeight).toBe(height);
  });

  it('fills the form from the subscription being edited', () => {
    open({
      id: 'sub-1',
      name: 'Portal',
      description: 'Owner: ops',
      url: 'https://portal.example.test/hooks',
      eventTypes: ['*'],
      headers: { 'X-Tenant-Code': 'acme' },
      secretPrefix: 'whsec_abcd',
      isEnabled: true,
      creationTime: '2026-11-01T00:00:00Z',
    });

    expect(component.isEditMode()).toBe(true);
    expect(component.allEvents()).toBe(true);
    expect(component.subscriptionForm.description().value()).toBe('Owner: ops');
  });
});
