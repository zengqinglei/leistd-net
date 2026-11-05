import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';

import { FullPageNavigationService } from './full-page-navigation-service';

/**
 * 整页离开与往返缓存兜底。`location` 的方法不可替换，用替身文档承接跳转：窗口是独立的事件目标，
//#if (RemoteTokenAuth || OpenIddictServer)
 * 表单仍由真实文档创建（提交被拦下）。
//#else
 * 不影响真实页面。
//#endif
 */
describe('FullPageNavigationService', () => {
  let view: EventTarget;
  let location: {
    replace: ReturnType<typeof vi.fn>;
    assign: ReturnType<typeof vi.fn>;
    reload: ReturnType<typeof vi.fn>;
  };
  let service: FullPageNavigationService;

  beforeEach(() => {
    view = new EventTarget();
    location = { replace: vi.fn(), assign: vi.fn(), reload: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        {
          provide: DOCUMENT,
          useValue: {
            defaultView: view,
            location,
            body: document.body,
            createElement: (tag: string) => document.createElement(tag),
          },
        },
      ],
    });
    service = TestBed.inject(FullPageNavigationService);
  });

  function restore(persisted: boolean): void {
    view.dispatchEvent(new PageTransitionEvent('pageshow', { persisted }));
  }

  it('replaces the current history entry', () => {
    service.replace('/auth/login?returnUrl=%2Fworkspace');

    expect(location.replace).toHaveBeenCalledExactlyOnceWith('/auth/login?returnUrl=%2Fworkspace');
  });

  // 从往返缓存恢复的是离开前那一刻的页面：守卫不会重跑，只能整页重载
  it('reloads a page restored from the back/forward cache after it left', () => {
    service.replace('/auth/login');

    restore(true);

    expect(location.reload).toHaveBeenCalledOnce();
  });

  it('keeps a restored page that never left', () => {
    restore(true);

    expect(location.reload).not.toHaveBeenCalled();
  });

  // 普通加载（非缓存恢复）本来就是新页面
  it('does not reload on a regular page show after leaving', () => {
    service.replace('/auth/login');

    restore(false);

    expect(location.reload).not.toHaveBeenCalled();
  });

  it('stops listening once the injector is destroyed', () => {
    service.replace('/auth/login');

    TestBed.resetTestingModule();
    restore(true);

    expect(location.reload).not.toHaveBeenCalled();
  });
  //#if (ExternalLogin)

  // 主动离开保留历史：在提供方页面后退能回来，回来时同样重载成可操作的页面
  it('keeps the history entry when leaving on purpose and reloads on return', () => {
    service.assign('/external/provider');

    expect(location.assign).toHaveBeenCalledExactlyOnceWith('/external/provider');
    expect(location.replace).not.toHaveBeenCalled();
    restore(true);
    expect(location.reload).toHaveBeenCalledOnce();
  });
  //#endif
  //#if (RemoteTokenAuth || OpenIddictServer)

  it('posts a form with the hidden fields in order and reloads on return', () => {
    const submit = vi
      .spyOn(HTMLFormElement.prototype, 'submit')
      .mockImplementation(() => undefined);

    service.post('/sign-out', { request: 'r1', proof: 'p1' });

    expect(submit).toHaveBeenCalledOnce();
    const form = submit.mock.contexts[0] as HTMLFormElement;
    expect(form.method).toBe('post');
    expect(form.getAttribute('action')).toBe('/sign-out');
    expect(
      [...form.querySelectorAll('input')].map((input) => [input.type, input.name, input.value]),
    ).toEqual([
      ['hidden', 'request', 'r1'],
      ['hidden', 'proof', 'p1'],
    ]);
    form.remove();
    restore(true);
    expect(location.reload).toHaveBeenCalledOnce();
  });
  //#endif
});
