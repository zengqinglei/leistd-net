import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

//#if (IncludeLocalization)
import { provideTranslocoTesting } from '../../../core/i18n/transloco.testing';
//#endif
import { CommandPaletteService } from '../../../core/services/command-palette-service';
//#if (OpenIddictServer || RemoteTokenAuth)
import { SystemDirectoryService } from '../../../core/services/system-directory-service';
//#endif
import { MenuGroup, NavigationService } from '../../services/navigation-service';
import { CommandPalette } from './command-palette';

/**
 * 命令面板：列出当前区已按权限裁剪的菜单与另一区的入口，⌘K / Ctrl+K 开合，选中即跳转并关闭。
 * 菜单裁剪由 NavigationService 负责（见 default-sidebar.spec），这里用桩给定输入。
 */
describe('CommandPalette', () => {
  const groups: MenuGroup[] = [
    { label: 'Work', items: [{ label: 'Dashboard', icon: 'lucideGauge', route: '/platform' }] },
    {
      label: 'Identity & access',
      items: [{ label: 'User management', icon: 'lucideUsers', route: '/platform/users' }],
    },
  ];
  const areas = [
    { route: '/workspace/dashboard', icon: 'lucideHouse', label: 'Workspace', current: false },
    { route: '/platform', icon: 'lucideCog', label: 'Administration', current: true },
  ];

  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        //#if (OpenIddictServer || RemoteTokenAuth)
        { provide: SystemDirectoryService, useValue: { systems: signal([]), switchTo: vi.fn() } },
        //#endif
        {
          provide: NavigationService,
          useValue: { menuGroups: signal(groups), areaOptions: signal(areas), goToArea: vi.fn() },
        },
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(),
        //#endif
      ],
    });
    const fixture = TestBed.createComponent(CommandPalette);
    fixture.detectChanges();
    return { fixture, palette: TestBed.inject(CommandPaletteService) };
  }

  const press = (init: KeyboardEventInit) =>
    document.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, ...init }));

  /** 面板渲染在 document 上的浮层里。 */
  const optionTexts = () =>
    [...document.querySelectorAll('[data-slot="command-item"]')].map((item) =>
      item.textContent?.trim(),
    );

  afterEach(() => TestBed.inject(CommandPaletteService).close());

  it('toggles with Ctrl+K or Cmd+K and ignores a plain K', () => {
    const { palette } = setup();

    press({ key: 'k' });
    expect(palette.isOpen()).toBe(false);

    press({ key: 'k', ctrlKey: true });
    expect(palette.isOpen()).toBe(true);

    press({ key: 'K', metaKey: true });
    expect(palette.isOpen()).toBe(false);
  });

  it('lists the menu of the current area followed by the other area only', async () => {
    const { fixture, palette } = setup();

    palette.open();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(optionTexts()).toEqual(['Dashboard', 'User management', 'Workspace']);
  });
  //#if (OpenIddictServer || RemoteTokenAuth)

  it('offers systems in their own group and delegates switching before closing', async () => {
    const { fixture, palette } = setup();
    const directory = TestBed.inject(SystemDirectoryService);
    const systems = [
      {
        key: 'identity',
        displayName: 'Identity',
        homeUrl: 'https://identity.test/',
        isCurrent: true,
      },
      { key: 'orders', displayName: 'Orders', homeUrl: 'https://orders.test/', isCurrent: false },
    ];
    vi.spyOn(directory, 'systems').mockReturnValue(systems);
    palette.open();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(optionTexts()).toEqual([
      'Dashboard',
      'User management',
      'Workspace',
      'Identity',
      'Orders',
    ]);
    const orders = [...document.querySelectorAll<HTMLElement>('[data-slot="command-item"]')].find(
      (item) => item.textContent?.trim() === 'Orders',
    );
    orders?.click();
    expect(directory.switchTo).toHaveBeenCalledWith(systems[1]);
    expect(palette.isOpen()).toBe(false);
  });
  //#endif

  it('navigates to the selected entry and closes', async () => {
    const { fixture, palette } = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    palette.open();
    fixture.detectChanges();
    await fixture.whenStable();
    const users = [...document.querySelectorAll<HTMLElement>('[data-slot="command-item"]')].find(
      (item) => item.textContent?.includes('User management'),
    );
    users?.click();
    fixture.detectChanges();

    expect(navigate).toHaveBeenCalledWith('/platform/users');
    expect(palette.isOpen()).toBe(false);
  });
});
