//#if (LocalIdentity)
import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OpenApplicationEditDialog } from './open-application-edit-dialog';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../../../../core/i18n/transloco.testing';
//#endif

/**
 * 下拉触发器与选项文案一致性回归。
 *
 * Select 的触发器渲染的是 `itemToString(value)`，不传就退化成把值本身字符串化——
 * 下拉里写着"桌面/原生"，选完输入框里却是 `native`，同一个东西两个说法。
 * 这类不一致编译期与 lint 都发现不了，只有把选项点开、选中、再读触发器才暴露得出来。
 */
@Component({
  imports: [OpenApplicationEditDialog],
  template: ` <app-open-application-edit-dialog [(visible)]="visible" /> `,
})
class HostComponent {
  readonly visible = signal(true);
}

describe('OpenApplicationEditDialog', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      // prettier-ignore
      providers: [
        provideZonelessChangeDetection(),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en']),
        //#endif
      ],
    });

    fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
  });

  afterEach(() => fixture.destroy());

  async function selectFirstOtherOption(triggerId: string): Promise<{
    optionText: string;
    triggerText: string;
  }> {
    const trigger = document.getElementById(triggerId);
    expect(trigger).withContext(`${triggerId} should be rendered`).not.toBeNull();

    trigger!.click();
    await fixture.whenStable();

    const options = Array.from(document.querySelectorAll<HTMLElement>('[role="option"]'));
    expect(options.length).withContext(`${triggerId} should offer options`).toBeGreaterThan(1);

    const option =
      options.find((item) => item.getAttribute('aria-selected') !== 'true') ?? options[0];
    const optionText = option.textContent?.trim() ?? '';

    option.click();
    await fixture.whenStable();

    return {
      optionText,
      triggerText: document.getElementById(triggerId)?.textContent?.trim() ?? '',
    };
  }

  for (const triggerId of [
    'application-type',
    'application-client-type',
    'application-consent-type',
  ]) {
    it(`shows the picked option's own label in ${triggerId}`, async () => {
      const { optionText, triggerText } = await selectFirstOtherOption(triggerId);

      expect(optionText).not.toBe('');
      expect(triggerText).toBe(optionText);
    });
  }
});
//#endif
