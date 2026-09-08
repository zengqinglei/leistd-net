import { TestBed } from '@angular/core/testing';
//#if (IncludeLocalization)
import { TranslocoService } from '@jsverse/transloco';
//#endif
import { toast } from '@spartan-ng/brain/sonner';

import { SecretRevealDialog } from './secret-reveal-dialog';

describe('SecretRevealDialog', () => {
  let writeText: jasmine.Spy;

  beforeEach(() => {
    writeText = jasmine.createSpy('writeText').and.resolveTo(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText },
      configurable: true,
    });

    TestBed.configureTestingModule({
      imports: [SecretRevealDialog],
      // prettier-ignore
      providers: [
        //#if (IncludeLocalization)
        { provide: TranslocoService, useValue: { translate: (key: string) => key } },
        //#endif
      ],
    });
  });

  function createDialog(secret = 'super-secret'): SecretRevealDialog {
    const fixture = TestBed.createComponent(SecretRevealDialog);
    fixture.componentRef.setInput('secret', secret);
    return fixture.componentInstance;
  }

  it('mirrors the dialog open/closed state into the visible model', () => {
    const dialog = createDialog();

    dialog.onStateChange('open');
    expect(dialog.visible()).toBeTrue();

    dialog.onStateChange('closed');
    expect(dialog.visible()).toBeFalse();
  });

  it('copies the secret to the clipboard and notifies on success', async () => {
    const successSpy = spyOn(toast, 'success');
    const dialog = createDialog();

    dialog.copy();

    expect(writeText).toHaveBeenCalledWith('super-secret');
    await new Promise((resolve) => setTimeout(resolve));
    expect(successSpy).toHaveBeenCalled();
  });

  it('does nothing when there is no secret to copy', () => {
    const dialog = createDialog('');

    dialog.copy();

    expect(writeText).not.toHaveBeenCalled();
  });
});
