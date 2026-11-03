import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, DeferBlockState, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { userEvent } from 'vitest/browser';

import { WebhookSubscriptionTable } from './webhook-subscription-table';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif
import { parkPointer } from '../../../../../../shared/utils/pointer.testing';
import { WebhookSubscriptionOutputDto } from '../../../../dtos/webhook.dto';

const LONG_NAME = 'Order fulfilment notifications for the regional warehouse integration endpoint';

function subscription(id: string, name: string): WebhookSubscriptionOutputDto {
  return {
    id,
    name,
    url: `https://example.com/${id}`,
    eventTypes: ['*'],
    headers: {},
    secretPrefix: 'whsec_ab',
    isEnabled: true,
    creationTime: '2026-11-01T00:00:00.000Z',
  };
}

/** 订阅名称截断时才提示全文：截断的名称可聚焦并给出完整名称，放得下的名称不提示、不多一个 Tab 停靠点。 */
describe('WebhookSubscriptionTable', () => {
  let fixture: ComponentFixture<WebhookSubscriptionTable>;

  const host = () => fixture.nativeElement as HTMLElement;
  const tooltip = () => document.querySelector<HTMLElement>('[role="tooltip"]');
  const nameOf = (text: string) =>
    Array.from(
      host().querySelectorAll<HTMLElement>('[data-testid="webhook-subscription-name"]'),
    ).find((span) => span.textContent?.trim() === text)!;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WebhookSubscriptionTable],
      // prettier-ignore
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    }).compileComponents();

    // 聚焦与悬停打开提示：先停车，残留指针悬停出的提示会被读成这一个
    await parkPointer();
    fixture = TestBed.createComponent(WebhookSubscriptionTable);
    host().style.display = 'block';
    host().style.width = '360px';
    fixture.componentRef.setInput('subscriptions', [
      subscription('long', LONG_NAME),
      subscription('short', 'Ops'),
    ]);
    fixture.componentRef.setInput('totalCount', 2);
    fixture.detectChanges();
    // 表格包在 @defer 里，测试环境不会自己渲染它
    const [table] = await fixture.getDeferBlocks();
    await table.render(DeferBlockState.Complete);
    fixture.autoDetectChanges();
    onTestFinished(() => document.querySelector('.cdk-overlay-container')?.remove());
    await expect.poll(() => nameOf(LONG_NAME).getAttribute('tabindex')).toBe('0');
  });

  it('puts the full name of a cut subscription into a focusable tooltip', async () => {
    const name = nameOf(LONG_NAME);
    expect(name.scrollWidth).toBeGreaterThan(name.clientWidth);

    name.focus();

    await expect.poll(() => tooltip()?.textContent?.trim() ?? '').toBe(LONG_NAME);
  });

  it('gives no tooltip and no tab stop to a name that fits', async () => {
    const name = nameOf('Ops');
    expect(name.scrollWidth).toBeLessThanOrEqual(name.clientWidth);
    expect(name.hasAttribute('tabindex')).toBe(false);

    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval'] });
    try {
      await userEvent.hover(name);
      // 推过提示的显示延时，"没出现"即禁用
      await vi.advanceTimersByTimeAsync(2000);
      expect(tooltip()).toBeNull();
    } finally {
      vi.useRealTimers();
      await parkPointer();
    }
  });
});
