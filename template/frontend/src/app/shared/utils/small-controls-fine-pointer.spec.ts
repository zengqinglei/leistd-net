import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';
import { HlmRadioGroupImports } from '@spartan-ng/helm/radio-group';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';

import { MIN_TOUCH_TARGET_PX, requireFinePointer } from './coarse-pointer.testing';

@Component({
  imports: [HlmCheckboxImports, HlmSwitchImports, HlmRadioGroupImports],
  template: `<div class="flex flex-col items-start gap-12 p-12">
    <hlm-checkbox aria-label="checkbox" />
    <hlm-switch aria-label="switch" />
    <hlm-radio-group>
      <hlm-radio value="a" aria-label="a"><hlm-radio-indicator indicator /></hlm-radio>
    </hlm-radio-group>
  </div>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class Host {}

describe('small controls on fine pointers', () => {
  beforeEach(() => requireFinePointer());

  it('keeps the visual size and has no hit area on fine pointers', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const checkbox = root.querySelector('[role="checkbox"]')!;
    const toggle = root.querySelector('[role="switch"]')!;
    const indicator = root.querySelector('[data-slot="radio-group-indicator"]')!;

    expect(checkbox.getBoundingClientRect().width).toBe(16);
    expect(indicator.getBoundingClientRect().width).toBe(16);
    expect(toggle.getBoundingClientRect().width).toBeLessThan(MIN_TOUCH_TARGET_PX);
    expect(getComputedStyle(checkbox, '::after').content).toBe('none');
  });
});
