import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { userEvent } from 'vitest/browser';

import { DialogClose } from './dialog-close';
import { ConfirmService } from '../../feedback/confirm-service';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../i18n/transloco.testing';
//#endif

//#if (IncludeLocalization)
const continueEditing = 'common.continueEditing';
//#else
const continueEditing = 'Continue editing';
//#endif

/** 业务弹窗的标准接法：静态 disableClose、禁外点，退出统一经 `app-dialog-close`。 */
@Component({
  imports: [...HlmDialogImports, DialogClose],
  template: `
    <hlm-dialog
      disableClose
      [closeOnOutsidePointerEvents]="false"
      [state]="open() ? 'open' : 'closed'"
      (stateChanged)="open.set($event === 'open')"
    >
      <hlm-dialog-content *hlmDialogPortal [showCloseButton]="false">
        <h2 hlmDialogTitle>Edit</h2>
        <app-dialog-close
          [dirty]="dirty()"
          [busy]="busy()"
          [closeOnBackdrop]="closeOnBackdrop()"
          (dismissed)="open.set(false)"
        />
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
class BusinessDialogHost {
  readonly open = signal(true);
  readonly dirty = signal(false);
  readonly busy = signal(false);
  readonly closeOnBackdrop = signal(true);
}

/** 抽屉同一接法：Brain 抽屉也由 CDK Dialog 渲染，内容注入器里同样有 DialogRef。 */
@Component({
  imports: [...HlmSheetImports, DialogClose],
  template: `
    <hlm-sheet
      side="right"
      disableClose
      [closeOnOutsidePointerEvents]="false"
      [state]="open() ? 'open' : 'closed'"
      (stateChanged)="open.set($event === 'open')"
    >
      <hlm-sheet-content *hlmSheetPortal [showCloseButton]="false">
        <h2 hlmSheetTitle>Edit</h2>
        <app-dialog-close (dismissed)="open.set(false)" />
      </hlm-sheet-content>
    </hlm-sheet>
  `,
})
class BusinessSheetHost {
  readonly open = signal(true);
}

/**
 * 遮罩点击与 ✕、Esc、取消同走 `requestClose()`：经 CDK `DialogRef.backdropClick` 接入，
 * brain 在 disableClose 时丢弃遮罩事件，所以不订阅就点不关。
 */
describe('DialogClose', () => {
  let fixture: ComponentFixture<BusinessDialogHost>;
  let host: BusinessDialogHost;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      // prettier-ignore
      providers: [
        provideZonelessChangeDetection(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });
    fixture = TestBed.createComponent(BusinessDialogHost);
    host = fixture.componentInstance;
  });

  afterEach(() => fixture.destroy());

  async function render(): Promise<void> {
    await fixture.whenStable();
    await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).not.toBeNull());
  }

  /** 最上层遮罩；点左上角，避开居中的弹窗本体。 */
  async function clickTopBackdrop(): Promise<void> {
    const backdrops = document.querySelectorAll<HTMLElement>('.cdk-overlay-backdrop');
    await userEvent.click(backdrops[backdrops.length - 1], { position: { x: 4, y: 4 } });
    await fixture.whenStable();
  }

  function discardConfirmation(): HTMLElement | null {
    return document.querySelector<HTMLElement>('[role="alertdialog"]');
  }

  /** 关闭要等退场动画；一帧不等就断言"还开着"证明不了什么，所以等足再查。 */
  async function settle(): Promise<void> {
    for (let frame = 0; frame < 30; frame++) {
      await new Promise((resolve) => requestAnimationFrame(resolve));
    }
    await fixture.whenStable();
  }

  it('closes a clean dialog when its backdrop is clicked', async () => {
    await render();
    await clickTopBackdrop();
    expect(host.open()).toBe(false);
    await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  });

  it('closes a clean sheet when its backdrop is clicked', async () => {
    fixture.destroy();
    const sheet = TestBed.createComponent(BusinessSheetHost);
    await sheet.whenStable();
    await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).not.toBeNull());
    const backdrops = document.querySelectorAll<HTMLElement>('.cdk-overlay-backdrop');
    await userEvent.click(backdrops[backdrops.length - 1], { position: { x: 4, y: 4 } });
    await sheet.whenStable();
    expect(sheet.componentInstance.open()).toBe(false);
    sheet.destroy();
  });

  it('asks before discarding changes and stays open when editing continues', async () => {
    host.dirty.set(true);
    await render();
    await clickTopBackdrop();
    await vi.waitFor(() => expect(discardConfirmation()).not.toBeNull());
    expect(host.open()).toBe(true);

    const keepEditing = Array.from(discardConfirmation()!.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === continueEditing,
    )!;
    await userEvent.click(keepEditing);
    await vi.waitFor(() => expect(discardConfirmation()).toBeNull());
    await settle();
    expect(host.open()).toBe(true);
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();
  });

  it('ignores the backdrop while a write is in flight', async () => {
    host.busy.set(true);
    await render();
    await clickTopBackdrop();
    await settle();
    expect(host.open()).toBe(true);
    expect(discardConfirmation()).toBeNull();
  });

  // 确认身份这类要求明确作答的对话框关掉遮罩出口，其余出口不变。
  it('ignores the backdrop when backdrop closing is turned off', async () => {
    host.closeOnBackdrop.set(false);
    await render();
    await clickTopBackdrop();
    await settle();
    expect(host.open()).toBe(true);
  });

  // 嵌套只处理最上层：点击落在上层确认框的遮罩。下层是无修改的业务弹窗，
  // 它若也收到 backdropClick 就会直接关闭，所以这条能区分"只有最上层收到"。
  it('only the topmost layer handles a backdrop click when a confirmation sits above', async () => {
    await render();
    const answer = TestBed.inject(ConfirmService).open({ message: 'Delete alice?' });
    await vi.waitFor(() => expect(discardConfirmation()).not.toBeNull());
    expect(document.querySelectorAll('.cdk-overlay-backdrop')).toHaveLength(2);

    await clickTopBackdrop();
    await settle();
    expect(discardConfirmation()).not.toBeNull();
    expect(host.open()).toBe(true);

    await userEvent.keyboard('{Escape}');
    await expect(answer).resolves.toBe(false);
    expect(host.open()).toBe(true);
  });

  // 放弃确认本身也是确认类对话框：点它的遮罩不作答，不会叠出第二个确认框。
  it('keeps a single discard confirmation when its own backdrop is clicked', async () => {
    host.dirty.set(true);
    await render();
    await clickTopBackdrop();
    await vi.waitFor(() => expect(discardConfirmation()).not.toBeNull());

    await clickTopBackdrop();
    await settle();
    expect(document.querySelectorAll('[role="alertdialog"]')).toHaveLength(1);
    expect(host.open()).toBe(true);

    await userEvent.keyboard('{Escape}');
    await vi.waitFor(() => expect(discardConfirmation()).toBeNull());
    expect(host.open()).toBe(true);
  });
});
