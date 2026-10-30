import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { WorkspaceNav } from './workspace-nav';
//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
import { requireFinePointer } from '../../../shared/utils/coarse-pointer.testing';
import { NavigationService } from '../../services/navigation-service';

@Component({
  imports: [WorkspaceNav],
  template: `<div class="flex w-[560px]"><app-workspace-nav /></div>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class Host {}

describe('WorkspaceNav on fine pointers', () => {
  beforeEach(() => requireFinePointer());

  it('keeps the compact sizes for a mouse', async () => {
    const menuGroups = signal([
      {
        label: 'Workspace',
        items: Array.from({ length: 6 }, (_, i) => ({
          label: `Navigation item ${i}`,
          icon: 'lucideGauge',
          route: `/workspace/item-${i}`,
        })),
      },
    ]);
    TestBed.configureTestingModule({
      // prettier-ignore
      providers: [
        provideRouter([]),
        { provide: NavigationService, useValue: { menuGroups, isItemActive: () => false } },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
      ],
    });
    const fixture = TestBed.createComponent(Host);
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    await fixture.whenStable();
    const more = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      '[data-testid="workspace-nav-more"]',
    )!;
    more.click();
    await fixture.whenStable();
    await Promise.all(document.getAnimations().map((animation) => animation.finished));
    const item = document.querySelector<HTMLElement>('[hlmDropdownMenuItem]')!;

    expect(more.getBoundingClientRect().height).toBeLessThan(44);
    expect(item.getBoundingClientRect().height).toBeLessThan(44);
  });
});
