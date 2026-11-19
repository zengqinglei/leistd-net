import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { page } from 'vitest/browser';

import { RealtimeStatus } from './realtime-status';
//#if (IncludeLocalization)
import enTexts from '../../../../../public/i18n/en.json';
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
import {
  type RealtimeConnectionStatus,
  SignalRService,
} from '../../../core/services/signalr-service';
import {
  expectTouchTarget,
  setCoarsePointer,
  tapAt,
} from '../../../shared/utils/coarse-pointer.testing';

/**
 * 触屏手机上的实时连接提示：胶囊保持小尺寸，「立即重试」的命中区仍不小于 44px，点在按钮视觉范围之外、
 * 命中区之内也能触发重试。
 */
describe('RealtimeStatus on touch phones', () => {
  let fixture: ComponentFixture<RealtimeStatus>;
  let retryNow: ReturnType<typeof vi.fn<() => void>>;

  afterEach(async () => {
    vi.useRealTimers();
    await setCoarsePointer(false);
  });

  it('keeps retry now a full touch target inside the small floating notice', async () => {
    await setCoarsePointer(true);
    await page.viewport(390, 844);
    onTestFinished(() => page.viewport(1280, 800));
    const status = signal<RealtimeConnectionStatus>('connected');
    const nextRetryAt = signal<number | null>(null);
    retryNow = vi.fn();
    TestBed.configureTestingModule({
      // prettier-ignore
      providers: [
        { provide: SignalRService, useValue: { connectionStatus: status, nextRetryAt, retryNow } },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });
    //#if (IncludeLocalization)
    TestBed.inject(TranslocoService).setTranslation(enTexts, 'en');
    //#endif
    fixture = TestBed.createComponent(RealtimeStatus);
    const host = fixture.nativeElement as HTMLElement;
    // 宿主在页面中部，伸出的命中区不被视口顶边裁掉
    host.style.marginTop = '200px';
    fixture.detectChanges();
    await fixture.whenStable();

    vi.useFakeTimers({
      toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'],
    });
    nextRetryAt.set(Date.now() + 20_000);
    status.set('reconnecting');
    fixture.detectChanges();
    vi.advanceTimersByTime(3000);
    fixture.detectChanges();
    vi.useRealTimers();

    const notice = host.querySelector<HTMLElement>('[data-testid="realtime-status-notice"]')!;
    const button = host.querySelector<HTMLButtonElement>('[data-testid="realtime-retry"]')!;
    // 前提：胶囊没有跟着按钮一起长到 44px
    expect(notice.getBoundingClientRect().height).toBeLessThan(44);
    expectTouchTarget(button);

    await tapAt(button, 0, 20);
    expect(retryNow).toHaveBeenCalledTimes(1);
  });
});
