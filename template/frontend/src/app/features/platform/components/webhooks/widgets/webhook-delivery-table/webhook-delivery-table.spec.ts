import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, DeferBlockState, TestBed } from '@angular/core/testing';
import { page } from 'vitest/browser';

//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { WebhookDeliveryOutputDto } from '../../../../dtos/webhook.dto';
import { WebhookDeliveryTable } from './webhook-delivery-table';

function delivery(
  id: string,
  status: WebhookDeliveryOutputDto['status'],
): WebhookDeliveryOutputDto {
  return {
    id,
    eventId: `evt-${id}`,
    eventType: 'order.created',
    subscriptionId: 'sub-1',
    subscriptionName: 'ERP order sync',
    subscriptionUrl: 'https://erp.example.com/hooks',
    status,
    attemptCount: 1,
    lastStatusCode: status === 'succeeded' ? 200 : 503,
    nextAttemptAt: status === 'pending' ? '2026-11-07T10:05:00.000Z' : null,
    nextTrigger: status === 'pending' ? 'automatic' : null,
    creationTime: '2026-11-07T09:00:00.000Z',
  };
}

/** 投递日志的行操作（列表规范 §2）：≥sm 平铺详情与重发；<sm 收进 … 菜单，菜单里能开详情、触发允许的重发。 */
describe('WebhookDeliveryTable row actions', () => {
  let fixture: ComponentFixture<WebhookDeliveryTable>;
  let table: WebhookDeliveryTable;

  const host = () => fixture.nativeElement as HTMLElement;
  const row = (id: string) =>
    host().querySelector<HTMLElement>(
      `[data-testid="webhook-delivery-row"][data-delivery-id="${id}"]`,
    )!;
  const visible = (element: Element | null) => !!element && element.getClientRects().length > 0;
  const menu = () => document.querySelector<HTMLElement>('[data-slot="dropdown-menu"]');
  const menuItems = () => [
    ...(menu()?.querySelectorAll<HTMLButtonElement>('[hlmDropdownMenuItem]') ?? []),
  ];

  async function render(width: number, canManage = true): Promise<void> {
    await page.viewport(width, 844);
    onTestFinished(() => page.viewport(1280, 800));
    fixture = TestBed.createComponent(WebhookDeliveryTable);
    table = fixture.componentInstance;
    fixture.componentRef.setInput('deliveries', [
      delivery('failed-1', 'failed'),
      delivery('pending-1', 'pending'),
    ]);
    fixture.componentRef.setInput('totalCount', 2);
    fixture.componentRef.setInput('canManage', canManage);
    fixture.detectChanges();
    // 表格包在 @defer 里，测试环境不会自己渲染它
    const [block] = await fixture.getDeferBlocks();
    await block.render(DeferBlockState.Complete);
    fixture.autoDetectChanges();
    await fixture.whenStable();
  }

  async function openMenu(id: string): Promise<void> {
    row(id).querySelector<HTMLButtonElement>('[data-testid="webhook-delivery-menu"]')!.click();
    await vi.waitFor(() => expect(menu()).not.toBeNull());
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WebhookDeliveryTable],
      // prettier-ignore
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    }).compileComponents();
  });

  afterEach(() => {
    fixture?.destroy();
    document.querySelector('.cdk-overlay-container')?.remove();
  });

  it('lays the details and resend out inline from sm without a menu', async () => {
    await render(1280);

    const actions = row('failed-1');
    expect(visible(actions.querySelector('[data-testid="webhook-delivery-details"]'))).toBe(true);
    expect(visible(actions.querySelector('[data-testid="webhook-delivery-retry"]'))).toBe(true);
    expect(visible(actions.querySelector('[data-testid="webhook-delivery-menu"]'))).toBe(false);
  });

  it('folds the row actions into a menu at 390px that opens details and resends', async () => {
    await render(390);
    const open = vi.fn();
    const retry = vi.fn();
    table.open.subscribe(open);
    table.retry.subscribe(retry);

    const actions = row('failed-1');
    expect(visible(actions.querySelector('[data-testid="webhook-delivery-details"]'))).toBe(false);
    expect(visible(actions.querySelector('[data-testid="webhook-delivery-retry"]'))).toBe(false);
    const trigger = actions.querySelector<HTMLElement>('[data-testid="webhook-delivery-menu"]')!;
    expect(visible(trigger)).toBe(true);
    expect(trigger.getAttribute('aria-label')).toBeTruthy();
    // 关闭详情后回焦落在窄屏可见的菜单触发器上
    expect(table.focusDetails('failed-1')).toBe(true);
    expect(document.activeElement).toBe(trigger);

    await openMenu('failed-1');
    expect(menuItems()).toHaveLength(2);
    menuItems()[0].click();
    expect(open).toHaveBeenCalledWith('failed-1');

    await vi.waitFor(() => expect(menu()).toBeNull());
    await openMenu('failed-1');
    menuItems()[1].click();
    expect(retry).toHaveBeenCalledWith(expect.objectContaining({ id: 'failed-1' }));
  });

  it('disables resending a retrying delivery in the menu and links the reason', async () => {
    await render(390);

    await openMenu('pending-1');
    const resend = menuItems()[1];
    expect(resend.disabled || resend.getAttribute('aria-disabled') === 'true').toBe(true);
    const reasonId = resend.getAttribute('aria-describedby')!;
    expect(reasonId).toBeTruthy();
    expect(document.getElementById(reasonId)?.closest('[data-slot="dropdown-menu"]')).toBe(menu());
  });

  it('keeps only the details in the menu without the manage permission', async () => {
    await render(390, false);

    await openMenu('failed-1');
    expect(menuItems()).toHaveLength(1);
  });
});
