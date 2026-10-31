import { Component, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BrnDialogService } from '@spartan-ng/brain/dialog';

import { ConfirmService } from './confirm-service';
import { DialogClose } from '../components/dialog-close/dialog-close';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../i18n/transloco.testing';
//#endif

@Component({ template: '' })
class ExistingDialog {}

@Component({
  imports: [DialogClose],
  template: `<app-dialog-close
    #guard
    [dirty]="true"
    (keydown.escape)="guard.requestClose($event)"
  />`,
})
class DirtyCloseHost {}

/**
 * 经服务真实打开 `ConfirmDialog`，因此同时覆盖对话框组件本身（它只是服务的内容，不单独测）：
 * 文案落位、alertdialog 语义与按钮回传的结果。
 */
describe('ConfirmService', () => {
  let service: ConfirmService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      // prettier-ignore
      providers: [
        provideZonelessChangeDetection(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });
    service = TestBed.inject(ConfirmService);
  });

  /** 对话框渲染在 document 上的浮层里，打开是异步的。 */
  async function openedDialog(): Promise<HTMLElement> {
    await vi.waitFor(() => expect(document.querySelector('[role="alertdialog"]')).not.toBeNull());
    return document.querySelector<HTMLElement>('[role="alertdialog"]')!;
  }

  function button(dialog: HTMLElement, text: string): HTMLButtonElement {
    return Array.from(dialog.querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === text,
    )!;
  }

  // 两种打开方式各自生成默认 id：保留下一次服务打开会碰到的 id，确认框仍须一次打开。
  it('opens on the first request when another dialog occupies the next default id', async () => {
    const dialogs = TestBed.inject(BrnDialogService);
    const warmup = dialogs.open(ExistingDialog);
    const blocker = dialogs.open(ExistingDialog, undefined, undefined, {
      id: `brn-dialog-${warmup.dialogId + 2}`,
    });
    const result = service.open({ message: 'Discard changes?', cancelText: 'Keep editing' });
    const outcome = expect(result).resolves.toBe(false);
    const dialog = await openedDialog();
    button(dialog, 'Keep editing').click();
    await outcome;
    blocker.close();
    warmup.close();
  });

  it('keeps the discard confirmation open when Escape opened it', async () => {
    const fixture = TestBed.createComponent(DirtyCloseHost);
    document.body.append(fixture.nativeElement);
    await fixture.whenStable();
    const trigger = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    trigger.focus();
    trigger.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    await fixture.whenStable();
    const dialog = await openedDialog();
    expect(dialog.querySelector('[data-slot="dialog-content"]')?.getAttribute('data-state')).toBe(
      'open',
    );
    dialog.querySelector<HTMLButtonElement>('button')!.click();
    await vi.waitFor(() => expect(document.querySelector('[role="alertdialog"]')).toBeNull());
    fixture.destroy();
  });

  it('opens an alert dialog named by its header and described by its message', async () => {
    const result = service.open({
      message: 'Delete alice?',
      header: 'Delete user',
      cancelText: 'Cancel',
    });
    const dialog = await openedDialog();

    const labelledBy = document.getElementById(dialog.getAttribute('aria-labelledby')!);
    const describedBy = document.getElementById(dialog.getAttribute('aria-describedby')!);
    expect(labelledBy?.textContent?.trim()).toBe('Delete user');
    expect(describedBy?.textContent?.trim()).toBe('Delete alice?');

    button(dialog, 'Cancel').click();
    await result;
  });

  it('resolves true when confirmed and uses the given button texts', async () => {
    const result = service.open({
      message: 'Reset the secret?',
      confirmText: 'Reset',
      cancelText: 'Keep',
      variant: 'destructive',
    });
    const dialog = await openedDialog();
    expect(button(dialog, 'Keep')).toBeDefined();

    button(dialog, 'Reset').click();

    await expect(result).resolves.toBe(true);
  });

  // alert-dialog 语义：没有 ✕，只能显式选一个动作；取消即 false
  it('offers no close button and resolves false when cancelled', async () => {
    const result = service.open({ message: 'Leave the page?' });
    const dialog = await openedDialog();
    //#if (IncludeLocalization)
    // 测试不装词条，缺失的键原样渲染：正好能看出默认文案取的是哪一条
    const [header, confirmText, cancelText] = ['common.confirm', 'common.ok', 'common.cancel'];
    //#else
    const [header, confirmText, cancelText] = ['Please confirm', 'OK', 'Cancel'];
    //#endif

    expect(document.getElementById('confirm-dialog-title')?.textContent?.trim()).toBe(header);
    expect(dialog.querySelectorAll('button')).toHaveLength(2);
    expect(button(dialog, confirmText)).toBeDefined();

    button(dialog, cancelText).click();

    await expect(result).resolves.toBe(false);
  });
});
