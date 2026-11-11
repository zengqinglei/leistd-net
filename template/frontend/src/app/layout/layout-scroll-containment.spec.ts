import { ChangeDetectionStrategy, Component, CUSTOM_ELEMENTS_SCHEMA, Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, RouterOutlet } from '@angular/router';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { page } from 'vitest/browser';

import { DefaultLayout } from './default/default-layout';
import { WorkspaceLayout } from './workspace/workspace-layout';

/** 很长的页面，末尾是一组只给读屏用的文字（如上传控件的隐藏标签）。 */
@Component({
  template: `
    <div data-testid="tall-content" style="height: 3000px"></div>
    @for (label of labels; track label) {
      <span class="sr-only" data-testid="sr-only-label">Upload {{ label }}</span>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class TallPage {
  protected readonly labels = Array.from({ length: 12 }, (_, index) => index + 1);
}

/**
 * 布局的滚动区必须是定位元素：`sr-only` 是绝对定位，包含块是最近的定位祖先。滚动区不是定位元素时，
 * 包含块落到更外层，`overflow-auto` 裁不住它，整个文档被撑高，底部多出一截空白。
 * 布局里除滚动区以外的部件（页头、侧栏、命令面板等）与本断言无关，按自定义元素渲染。
 */
describe('Layout scroll containment', () => {
  async function render(
    layout: Type<unknown>,
    imports: (Type<unknown> | readonly Type<unknown>[])[],
  ): Promise<HTMLElement> {
    await page.viewport(1280, 800);
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'tall', component: TallPage }])],
    });
    TestBed.overrideComponent(layout, {
      set: { imports, schemas: [CUSTOM_ELEMENTS_SCHEMA] },
    });
    const fixture = TestBed.createComponent(layout);
    await TestBed.inject(Router).navigateByUrl('/tall');
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function expectContained(host: HTMLElement): void {
    const labels = [...host.querySelectorAll<HTMLElement>('[data-testid="sr-only-label"]')];
    const scroller = host
      .querySelector<HTMLElement>('[data-testid="tall-content"]')!
      .parentElement!.closest<HTMLElement>('.overflow-auto')!;

    expect(labels).toHaveLength(12);
    // 内容在滚动区里滚，文档本身不比视口高
    expect(scroller.scrollHeight).toBeGreaterThan(scroller.clientHeight);
    expect(document.documentElement.scrollHeight).toBeLessThanOrEqual(window.innerHeight);
    // 绝对定位的标签以滚动区为包含块
    for (const label of labels) {
      expect(label.offsetParent).toBe(scroller);
    }
  }

  it('keeps screen-reader-only content inside the sidebar layout scroll area', async () => {
    expectContained(await render(DefaultLayout, [RouterOutlet, ...HlmSidebarImports]));
  });

  it('keeps screen-reader-only content inside the workspace layout scroll area', async () => {
    expectContained(await render(WorkspaceLayout, [RouterOutlet]));
  });
});
