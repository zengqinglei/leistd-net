import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BrnDialogService } from '@spartan-ng/brain/dialog';
import { HlmDialogService } from '@spartan-ng/helm/dialog';
import { of, Subject, throwError } from 'rxjs';

import { ConfirmService } from './confirm-service';
import { DialogClose } from '../components/dialog-close/dialog-close';
import { ApplicationHttpError, applicationErrorMessage } from '../errors/application-http-error';
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
    const closeHost = fixture.nativeElement.querySelector('app-dialog-close') as HTMLElement;
    expect(getComputedStyle(closeHost).display).toBe('contents');
    expect(getComputedStyle(trigger).position).toBe('absolute');
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
    const content = dialog.querySelector<HTMLElement>('[data-slot="dialog-content"]')!;
    const body = content.querySelector<HTMLElement>('[data-dialog-body]')!;
    const footer = content.querySelector<HTMLElement>('[data-slot="dialog-footer"]')!;
    expect(getComputedStyle(content).maxWidth).toBe('512px');
    expect(getComputedStyle(content.querySelector('ng-icon')!).fontSize).toBe('32px');
    expect(body.contains(describedBy)).toBe(true);
    expect(getComputedStyle(body).gridTemplateColumns.split(' ')[0]).toBe('64px');
    expect(getComputedStyle(body).columnGap).toBe('24px');
    expect(body.contains(footer)).toBe(false);
    expect(getComputedStyle(footer).flexShrink).toBe('0');
    expect(getComputedStyle(footer).backgroundColor).toBe('rgba(0, 0, 0, 0)');
    expect(getComputedStyle(footer).borderTopWidth).toBe('0px');

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
  function actionDialog(action: () => import('rxjs').Observable<unknown>, owner?: DestroyRef) {
    return service.open({
      message: 'Write?',
      confirmText: 'Write',
      cancelText: 'Cancel',
      action,
      owner,
    });
  }

  it('waits inside the dialog, rejects duplicate confirmation/cancel/Escape and restores trigger focus on success', async () => {
    const trigger = document.createElement('button');
    document.body.append(trigger);
    trigger.focus();
    const request = new Subject<void>();
    const action = vi.fn(() => request);
    const result = actionDialog(action);
    const dialog = await openedDialog();
    const write = button(dialog, 'Write');
    write.focus();
    write.click();
    write.click();
    await vi.waitFor(() => expect(write.disabled).toBe(true));
    expect(write.querySelector('hlm-spinner')).not.toBeNull();
    expect(button(dialog, 'Cancel').disabled).toBe(true);
    expect(dialog.contains(document.activeElement)).toBe(true);
    button(dialog, 'Cancel').click();
    document.activeElement!.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    await Promise.resolve();
    expect(document.querySelector('[role="alertdialog"]')).toBe(dialog);
    expect(action).toHaveBeenCalledOnce();
    request.next();
    request.complete();
    await expect(result).resolves.toBe(true);
    expect(document.querySelector('[role="alertdialog"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);
    trigger.remove();
  });

  it('keeps a normalized failure in the body, restores confirmation focus, and retries a fresh action', async () => {
    const action = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new Error('Conflict. Refresh first.')))
      .mockReturnValueOnce(of(undefined));
    const result = actionDialog(action);
    const dialog = await openedDialog();
    button(dialog, 'Write').click();
    await vi.waitFor(() =>
      expect(dialog.querySelector('[role="alert"]')?.textContent).toContain(
        'Conflict. Refresh first.',
      ),
    );
    expect(
      dialog.querySelector('[data-dialog-body]')?.contains(dialog.querySelector('[role="alert"]')),
    ).toBe(true);
    await vi.waitFor(() => expect(document.activeElement).toBe(button(dialog, 'Write')));
    const description = dialog.querySelector<HTMLElement>('#confirm-dialog-description')!;
    const alert = dialog.querySelector<HTMLElement>('[role="alert"]')!;
    expect(alert.parentElement).toBe(description.parentElement);
    expect(getComputedStyle(description.parentElement!).gap).toBe('8px');
    expect(button(dialog, 'Write').disabled).toBe(false);
    expect(button(dialog, 'Write').querySelector('hlm-spinner')).toBeNull();
    button(dialog, 'Write').click();
    await expect(result).resolves.toBe(true);
    expect(action).toHaveBeenCalledTimes(2);
  });

  it.each([0, 500])(
    'preserves uncertain write guidance for status %s and lets the user cancel',
    async (status) => {
      const error = ApplicationHttpError.from(
        new HttpErrorResponse({
          status,
          error: { detail: 'Private server details', traceId: 'trace-123' },
        }),
        { method: 'DELETE' },
      );
      const action = vi.fn(() => throwError(() => error));
      const result = actionDialog(action);
      const dialog = await openedDialog();
      button(dialog, 'Write').click();
      await vi.waitFor(() =>
        expect(dialog.querySelector('[role="alert"]')?.textContent?.trim()).toBe(
          applicationErrorMessage(error),
        ),
      );
      expect(dialog.textContent).not.toContain('Private server details');
      expect(action).toHaveBeenCalledOnce();
      button(dialog, 'Cancel').click();
      await expect(result).resolves.toBe(false);
    },
  );

  it('catches synchronous action errors and permits Escape after failure', async () => {
    const result = actionDialog(() => {
      throw new Error('Cannot start');
    });
    const dialog = await openedDialog();
    button(dialog, 'Write').click();
    await vi.waitFor(() => expect(button(dialog, 'Write').disabled).toBe(false));
    await vi.waitFor(() => expect(dialog.querySelector('[role="alert"]')).not.toBeNull());
    button(dialog, 'Write').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    await expect(result).resolves.toBe(false);
  });

  it('allows programmatic parent close during a request and stops its subscription', async () => {
    const open = vi.spyOn(TestBed.inject(HlmDialogService), 'open');
    const request = new Subject<void>();
    const result = actionDialog(() => request);
    const dialog = await openedDialog();
    button(dialog, 'Write').click();
    expect(request.observed).toBe(true);
    open.mock.results[0].value.close();
    await expect(result).resolves.toBe(false);
    expect(request.observed).toBe(false);
  });

  it('force closes and stops the request when the calling component is destroyed', async () => {
    const fixture = TestBed.createComponent(ExistingDialog);
    const owner = fixture.debugElement.injector.get(DestroyRef);
    const request = new Subject<void>();
    const result = actionDialog(() => request, owner);
    const dialog = await openedDialog();
    button(dialog, 'Write').click();
    expect(request.observed).toBe(true);
    fixture.destroy();
    await expect(result).resolves.toBe(false);
    expect(request.observed).toBe(false);
    expect(document.querySelector('[role="alertdialog"]')).toBeNull();
    await expect(actionDialog(() => of(undefined), owner)).resolves.toBe(false);
  });

  it('keeps Escape cancellation for legacy calls without action', async () => {
    const result = service.open({ message: 'Leave?' });
    const dialog = await openedDialog();
    dialog
      .querySelector('button')!
      .dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
      );
    await expect(result).resolves.toBe(false);
  });
});
