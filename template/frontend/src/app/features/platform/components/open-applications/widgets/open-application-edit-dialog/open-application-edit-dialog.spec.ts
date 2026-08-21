//#if (IdentityService)
import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
//#if (IncludeLocalization)
import { provideTransloco, Translation, TranslocoLoader } from '@jsverse/transloco';
import { Observable, of } from 'rxjs';
//#endif

import { OpenApplicationEditDialog } from './open-application-edit-dialog';

/**
 * 下拉触发器与选项文案一致性回归。
 *
 * Select 的触发器渲染的是 `itemToString(value)`，不传就退化成把值本身字符串化——
 * 下拉里写着"桌面/原生"，选完输入框里却是 `native`，同一个东西两个说法。
 * 这类不一致编译期与 lint 都发现不了，只有把选项点开、选中、再读触发器才暴露得出来。
 */
//#if (IncludeLocalization)
class EmptyTranslationLoader implements TranslocoLoader {
  getTranslation(): Observable<Translation> {
    return of({});
  }
}

//#endif
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
        // 用真实 Transloco 配空词条：模板里同时有 `| transloco` 管道与 translate() 调用，
        // 手写桩要把 config/langChanges$/scope 解析全补齐才跑得起来，不如让它自己跑。
        // 词条缺失时返回键本身，正好满足本组用例——只关心两处显示是不是同一个字符串。
        provideTransloco({
          config: { availableLangs: ['en'], defaultLang: 'en', reRenderOnLangChange: false },
          loader: EmptyTranslationLoader,
        }),
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
