import { TestBed } from '@angular/core/testing';

import { COPIED_FEEDBACK_MS, injectCopyToClipboard } from './clipboard';

describe('injectCopyToClipboard', () => {
  let writeText: jasmine.Spy;

  beforeEach(() => {
    writeText = jasmine.createSpy('writeText').and.resolveTo(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    jasmine.clock().install();
  });

  afterEach(() => jasmine.clock().uninstall());

  const create = () => TestBed.runInInjectionContext(() => injectCopyToClipboard());

  it('copies the text and shows the copied state for a short while', async () => {
    const clipboard = create();

    expect(await clipboard.copy('secret')).toBeTrue();
    expect(writeText).toHaveBeenCalledWith('secret');
    expect(clipboard.copied()).toBeTrue();

    jasmine.clock().tick(COPIED_FEEDBACK_MS);
    expect(clipboard.copied()).toBeFalse();
  });

  // 非安全上下文或权限被拒：不抛出、不显示已复制，调用方据返回值决定提示
  it('reports failure without throwing when the clipboard is unavailable', async () => {
    writeText.and.rejectWith(new DOMException('Denied', 'NotAllowedError'));
    const clipboard = create();

    expect(await clipboard.copy('secret')).toBeFalse();
    expect(clipboard.copied()).toBeFalse();
  });
});
