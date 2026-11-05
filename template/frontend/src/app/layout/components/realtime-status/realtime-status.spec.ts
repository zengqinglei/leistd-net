import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif

import { RealtimeStatus } from './realtime-status';
//#if (IncludeLocalization)
import enTexts from '../../../../../public/i18n/en.json';
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
import {
  type RealtimeConnectionStatus,
  SignalRService,
} from '../../../core/services/signalr-service';

/**
 * 实时连接提示条：正常时不显示，重连持续 2 秒以上与断开时显示。假计时器只推进本组件的显示延时
 * （setTimeout），渲染调度照常。文案逐字写出、不从词条推导：多语言形态渲染 `public/i18n` 的英文词条。
 */
describe('RealtimeStatus', () => {
  let fixture: ComponentFixture<RealtimeStatus>;
  let status: ReturnType<typeof signal<RealtimeConnectionStatus>>;
  let connect: ReturnType<typeof vi.fn<() => Promise<void>>>;

  const host = () => fixture.nativeElement as HTMLElement;
  const notice = () => host().querySelector<HTMLElement>('[data-testid="realtime-status-notice"]');

  beforeEach(async () => {
    status = signal<RealtimeConnectionStatus>('connected');
    connect = vi.fn(() => Promise.resolve());
    TestBed.configureTestingModule({
      // prettier-ignore
      providers: [
        { provide: SignalRService, useValue: { connectionStatus: status, connect } },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });
    //#if (IncludeLocalization)
    TestBed.inject(TranslocoService).setTranslation(enTexts, 'en');
    //#endif
    fixture = TestBed.createComponent(RealtimeStatus);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => vi.useRealTimers());

  async function render(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('shows nothing while connected or not yet connecting', async () => {
    expect(notice()).toBeNull();
    status.set('idle');
    await render();
    expect(notice()).toBeNull();
    expect(host().getBoundingClientRect().height).toBe(0);
  });

  // 随内容一起插入的 live region 多数读屏不播报：状态区要在变化之前就在，提示条插进同一个元素
  it('announces the notice inside a status region that exists before the change', async () => {
    expect(host().getAttribute('role')).toBe('status');
    expect(host().isConnected).toBe(true);

    status.set('disconnected');
    await render();

    expect(host().contains(notice())).toBe(true);
    expect(host().getAttribute('role')).toBe('status');
    // hlm-alert 自带的 alert 角色去掉，只由宿主播报一次
    expect(notice()!.hasAttribute('role')).toBe(false);
  });

  it('shows the reconnecting notice only after two seconds of reconnecting', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    status.set('reconnecting');
    fixture.detectChanges();

    vi.advanceTimersByTime(1999);
    fixture.detectChanges();
    expect(notice()).toBeNull();

    vi.advanceTimersByTime(1);
    vi.useRealTimers();
    await render();
    expect(notice()!.textContent!.trim()).toBe('Live updates interrupted. Reconnecting…');
    expect(notice()!.querySelector('button')).toBeNull();
  });

  it('shows nothing when a reconnect recovers within two seconds', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    status.set('reconnecting');
    fixture.detectChanges();
    vi.advanceTimersByTime(1000);
    status.set('connected');
    fixture.detectChanges();

    vi.advanceTimersByTime(5000);
    vi.useRealTimers();
    await render();
    expect(notice()).toBeNull();
  });

  it('restarts the delay when a new reconnect begins', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    status.set('reconnecting');
    fixture.detectChanges();
    vi.advanceTimersByTime(1500);
    status.set('connected');
    fixture.detectChanges();
    status.set('reconnecting');
    fixture.detectChanges();

    vi.advanceTimersByTime(1500);
    fixture.detectChanges();
    expect(notice()).toBeNull();
    vi.advanceTimersByTime(500);
    vi.useRealTimers();
    await render();
    expect(notice()).not.toBeNull();
  });

  it('shows a disconnected notice at once and reconnects on request until connected', async () => {
    status.set('disconnected');
    await render();

    expect(notice()!.querySelector('[hlmAlertTitle]')!.textContent!.trim()).toBe(
      'Live updates disconnected. This page may be out of date.',
    );
    const button = notice()!.querySelector<HTMLButtonElement>('[hlmAlertAction] button')!;
    expect(button.textContent!.trim()).toBe('Reconnect');

    button.click();
    expect(connect).toHaveBeenCalledTimes(1);

    status.set('connected');
    await render();
    expect(notice()).toBeNull();
  });
});
