import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { NavigationProgress } from './navigation-progress';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../i18n/transloco.testing';
//#endif
import { NavigationProgressService } from '../../services/navigation-progress-service';

/** 在真实浏览器 DOM 验证无障碍与两种挂载位置，假时钟只推进进度延迟，不替换渲染调度。 */
describe('NavigationProgress', () => {
  let fixture: ComponentFixture<NavigationProgress>;
  let service: NavigationProgressService;
  const progressbar = () =>
    (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[role="progressbar"]');

  beforeEach(async () => {
    TestBed.configureTestingModule({
      // prettier-ignore
      providers: [
        provideRouter([]),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
      ],
    });
    fixture = TestBed.createComponent(NavigationProgress);
    service = TestBed.inject(NavigationProgressService);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => vi.useRealTimers());

  async function show() {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const release = service.begin();
    vi.advanceTimersByTime(299);
    fixture.detectChanges();
    expect(progressbar()).toBeNull();
    vi.advanceTimersByTime(1);
    vi.useRealTimers();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return release;
  }

  it('removes the progressbar from the DOM while idle', () => {
    expect(progressbar()).toBeNull();
  });

  it('shows a named indeterminate bar after the delay and removes it on release', async () => {
    const release = await show();
    expect(progressbar()).not.toBeNull();
    //#if (IncludeLocalization)
    expect(progressbar()!.getAttribute('aria-label')).toBe('app.navigation.loading');
    //#else
    expect(progressbar()!.getAttribute('aria-label')).toBe('Loading...');
    //#endif
    expect(progressbar()!.hasAttribute('aria-valuenow')).toBe(false);
    const host = getComputedStyle(fixture.nativeElement as HTMLElement);
    expect(host.position).toBe('fixed');
    expect(host.top).toBe('0px');
    expect(Number(host.zIndex)).toBeGreaterThan(30);
    const style = getComputedStyle(progressbar()!);
    expect(style.position).toBe('absolute');
    expect(style.height).toBe('2px');
    expect(style.pointerEvents).toBe('none');
    release();
    fixture.detectChanges();
    expect(progressbar()).toBeNull();
  });

  it('yields the viewport bar to a content-area host and takes over again once it is gone', async () => {
    const content = TestBed.createComponent(NavigationProgress);
    content.componentRef.setInput('placement', 'content');
    content.detectChanges();
    const release = await show();
    content.detectChanges();
    expect(progressbar()).toBeNull();
    const contentBar = (content.nativeElement as HTMLElement).querySelector('[role="progressbar"]');
    expect(contentBar).not.toBeNull();
    const host = getComputedStyle(content.nativeElement as HTMLElement);
    expect(host.position).toBe('relative');
    expect(host.height).toBe('0px');
    content.destroy();
    fixture.detectChanges();
    expect(progressbar()).not.toBeNull();
    release();
  });
});
