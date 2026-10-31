import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HlmCommandImports } from '@spartan-ng/helm/command';
import { userEvent } from 'vitest/browser';

import { ImeGuard } from './ime-guard';

@Component({
  imports: [ImeGuard, ...HlmCommandImports],
  template: `<div
      appImeGuard
      tabindex="0"
      (keydown.enter)="submitted = submitted + 1"
      (keydown.escape)="cancelled = cancelled + 1"
    >
      <input id="ime-business" />
    </div>
    <hlm-command
      ><hlm-command-input /><hlm-command-list
        ><button hlm-command-item value="Target" (selected)="selected = selected + 1">
          Target
        </button></hlm-command-list
      ></hlm-command
    >`,
})
class ImeHost {
  submitted = 0;
  cancelled = 0;
  selected = 0;
}

describe('IME keyboard guards', () => {
  it.each([{ isComposing: true }, { keyCode: 229 }])(
    'preserves composition defaults and skips business Enter and Escape for %j',
    async (state) => {
      const fixture = TestBed.createComponent(ImeHost);
      await fixture.whenStable();
      const input = fixture.nativeElement.querySelector('#ime-business') as HTMLInputElement;
      for (const key of ['Enter', 'Escape']) {
        const event = new KeyboardEvent('keydown', {
          key,
          bubbles: true,
          cancelable: true,
          ...state,
        });
        input.dispatchEvent(event);
        expect(event.defaultPrevented).toBe(false);
      }
      expect(fixture.componentInstance.submitted).toBe(0);
      expect(fixture.componentInstance.cancelled).toBe(0);
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      expect(fixture.componentInstance.submitted).toBe(1);
    },
  );

  it('guards the actual Brain command root while preserving normal Enter selection', async () => {
    const fixture = TestBed.createComponent(ImeHost);
    await fixture.whenStable();
    const input = fixture.nativeElement.querySelector(
      '[data-slot="command-input"]',
    ) as HTMLInputElement;
    input.focus();
    await userEvent.keyboard('{ArrowDown}');
    input.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, isComposing: true }),
    );
    input.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, keyCode: 229 }),
    );
    expect(fixture.componentInstance.selected).toBe(0);
    await userEvent.keyboard('{Enter}');
    expect(fixture.componentInstance.selected).toBe(1);
  });
});
