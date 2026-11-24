import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideIcons } from '@ng-icons/core';
import { lucideSend } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmInput } from '@spartan-ng/helm/input';

import { expectTouchTarget } from '../../../../../../shared/utils/coarse-pointer.testing';
import { SettingTestPanel } from './setting-test-panel';

type Shape = 'button' | 'input' | 'expandable';

/** 三种形态各用一份投影内容：一个按钮、一项输入加按钮、多项输入的表单。 */
@Component({
  imports: [SettingTestPanel, HlmButton, HlmInput],
  providers: [provideIcons({ lucideSend })],
  template: `
    <div [style.width.px]="width()">
      @switch (shape()) {
        @case ('button') {
          <app-setting-test-panel icon="lucideSend" heading="Test export" description="Checks it">
            <button hlmBtn variant="outline" data-testid="run">Test</button>
          </app-setting-test-panel>
        }
        @case ('input') {
          <app-setting-test-panel icon="lucideSend" heading="Send a test email">
            <input hlmInput class="@md/test-panel:w-56" aria-label="Recipient" data-testid="to" />
            <button hlmBtn variant="outline" data-testid="run">Send</button>
          </app-setting-test-panel>
        }
        @case ('expandable') {
          <app-setting-test-panel
            icon="lucideSend"
            heading="Try a name"
            description="Screens one name"
            [expandable]="true"
            [(expanded)]="expanded"
          >
            <input hlmInput aria-label="Name" data-testid="name" />
            <input hlmInput aria-label="Country" data-testid="country" />
          </app-setting-test-panel>
        }
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class Host {
  readonly shape = signal<Shape>('button');
  readonly width = signal(720);
  readonly expanded = signal(false);
}

/**
 * 测试区：配置块下方的带边框容器，只管布局与展开。投影内容在两种宽度下的排法、展开触发器的
 * `aria-expanded` 与容器内展开都要成立；请求与提示由使用方负责，不在这里测。
 */
describe('SettingTestPanel', () => {
  let fixture: ComponentFixture<Host>;

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function byTestId(id: string): HTMLElement | null {
    return root().querySelector<HTMLElement>(`[data-testid="${id}"]`);
  }

  async function render(shape: Shape, width = 720): Promise<void> {
    fixture.componentInstance.shape.set(shape);
    fixture.componentInstance.width.set(width);
    await fixture.whenStable();
  }

  beforeEach(async () => {
    fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
  });

  afterEach(() => fixture.destroy());

  it('labels the bordered group with its heading and projects a single action', async () => {
    await render('button');

    const panel = byTestId('setting-test-panel')!;
    expect(panel.getAttribute('role')).toBe('group');
    expect(panel.dataset['variant']).toBe('outline');
    const title = root().querySelector<HTMLElement>(`#${panel.getAttribute('aria-labelledby')}`);
    expect(title?.textContent?.trim()).toBe('Test export');
    expect(panel.textContent).toContain('Checks it');
    expect(byTestId('setting-test-panel-actions')!.contains(byTestId('run'))).toBe(true);
    // 不可展开时没有展开触发器
    expect(root().querySelector('[aria-expanded]')).toBeNull();
  });

  it('puts the input beside the button when the panel is wide', async () => {
    await render('input', 720);

    const input = byTestId('to')!.getBoundingClientRect();
    const button = byTestId('run')!.getBoundingClientRect();
    expect(Math.abs(input.top - button.top)).toBeLessThan(1);
    expect(input.right).toBeLessThanOrEqual(button.left);
    // 操作区接在说明右侧，与标题同一行
    const title = root().querySelector('[data-slot="item-title"]')!.getBoundingClientRect();
    expect(input.top).toBeLessThan(title.bottom);
  });

  // 手机宽度（390 视口里的面板约 358px）：输入与按钮各占满一行，排在说明下方
  it('stacks the input and the button full width when the panel is narrow', async () => {
    await render('input', 358);

    const actions = byTestId('setting-test-panel-actions')!.getBoundingClientRect();
    const input = byTestId('to')!.getBoundingClientRect();
    const button = byTestId('run')!.getBoundingClientRect();
    const heading = root().querySelector('[data-slot="item-title"]')!.getBoundingClientRect();
    expect(input.top).toBeGreaterThanOrEqual(heading.bottom);
    expect(button.top).toBeGreaterThanOrEqual(input.bottom);
    expect(Math.abs(input.width - actions.width)).toBeLessThan(1);
    expect(Math.abs(button.width - actions.width)).toBeLessThan(1);
  });

  it('collapses to a toggle row and expands the form inside the same container', async () => {
    await render('expandable', 358);

    const toggle = byTestId('setting-test-panel-toggle') as HTMLButtonElement;
    expect(toggle.tagName).toBe('BUTTON');
    expect(toggle.type).toBe('button');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.textContent).toContain('Try a name');
    expect(byTestId('setting-test-panel-body')).toBeNull();
    expect(byTestId('name')).toBeNull();
    // 整行是自写的点按目标
    expectTouchTarget(toggle);

    toggle.click();
    await fixture.whenStable();

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(fixture.componentInstance.expanded()).toBe(true);
    const body = byTestId('setting-test-panel-body')!;
    expect(toggle.getAttribute('aria-controls')).toBe(body.id);
    expect(body.getAttribute('role')).toBe('region');
    expect(root().querySelector(`#${body.getAttribute('aria-labelledby')}`)?.textContent).toContain(
      'Try a name',
    );
    expect(body.contains(byTestId('name'))).toBe(true);
    expect(body.contains(byTestId('country'))).toBe(true);
    // 表单在同一个带边框的容器里展开
    expect(byTestId('setting-test-panel')!.contains(body)).toBe(true);

    toggle.click();
    await fixture.whenStable();
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(byTestId('setting-test-panel-body')).toBeNull();
  });

  it('lets the caller collapse the panel through the two-way binding', async () => {
    await render('expandable');
    fixture.componentInstance.expanded.set(true);
    await fixture.whenStable();
    expect(byTestId('setting-test-panel-body')).not.toBeNull();

    fixture.componentInstance.expanded.set(false);
    await fixture.whenStable();
    expect(byTestId('setting-test-panel-body')).toBeNull();
    expect(byTestId('setting-test-panel-toggle')!.getAttribute('aria-expanded')).toBe('false');
  });
});
