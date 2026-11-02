import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { WebhookSubscriptionDialog } from './webhook-subscription-dialog';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { AccountPreferencesService } from '../../../../../../core/services/account-preferences-service';
import {
  CreateWebhookSubscriptionInputDto,
  WebhookSubscriptionOutputDto,
} from '../../../../dtos/webhook.dto';

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
    component.addHeader();
    component.addHeader();
    const [first] = (
      component as unknown as { formModel: () => { headers: { key: number }[] } }
    ).formModel().headers;
    component.setHeader(first.key, 'name', ' X-Tenant-Code ');
    component.setHeader(first.key, 'value', 'acme');

    await component.save();

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
