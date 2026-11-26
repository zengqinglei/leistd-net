import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';

//#if (IncludeLocalization)
import en from '../../../../../../public/i18n/identityExpiry/en.json';
import { provideTranslocoTesting } from '../../../../core/i18n/transloco.testing';
//#endif
import { AccountService } from '../../services/account-service';
import { IdentityExpired } from './identity-expired';

describe('IdentityExpired', () => {
  const application = { displayName: 'Example app', homeUrl: 'https://example.test/home' };

  async function render(logout: boolean, clientId: string | undefined, registered: boolean) {
    TestBed.configureTestingModule({
      imports: [IdentityExpired],
      providers: [
        provideRouter([]),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en'], { getTranslation: () => of({ identityExpiry: en }) }),
        //#endif
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { data: { logout } },
            queryParamMap: of(
              convertToParamMap({
                ...(clientId ? { client_id: clientId } : {}),
                homeUrl: 'https://unregistered.example.test',
              }),
            ),
          },
        },
        {
          provide: AccountService,
          useValue: { getInteractionApplication: () => of(registered ? application : null) },
        },
      ],
    });
    const fixture = TestBed.createComponent(IdentityExpired);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it.each([
    [
      false,
      'This sign-in request has expired',
      'Go back to Example app',
      'Sign in to Example app again',
    ],
    [true, 'This sign-out request has expired', 'go back to Example app', 'Back to Example app'],
  ] as const)(
    'links to the registered application on the expired request page (logout=%s)',
    async (logout, title, description, action) => {
      const page = await render(logout, 'example', true);

      expect(page.querySelector('h1')?.textContent?.trim()).toBe(title);
      expect(page.querySelector('p')?.textContent).toContain(description);
      expect(
        Array.from(page.querySelectorAll('a'), (link) => [
          link.textContent?.trim(),
          link.getAttribute('href'),
        ]),
      ).toEqual([
        [action, application.homeUrl],
        ['Back to Identity home', '/'],
      ]);
    },
  );

  it.each([
    [false, 'unknown', 'This sign-in request has expired', 'Go back to the app you were using'],
    [true, undefined, 'This sign-out request has expired', 'go back to the app you were using'],
  ] as const)(
    'offers only Identity home without a registered destination (logout=%s)',
    async (logout, clientId, title, description) => {
      const page = await render(logout, clientId, false);

      expect(page.querySelector('h1')?.textContent?.trim()).toBe(title);
      expect(page.querySelector('p')?.textContent).toContain(description);
      expect(
        Array.from(page.querySelectorAll('a'), (link) => [
          link.textContent?.trim(),
          link.getAttribute('href'),
        ]),
      ).toEqual([['Back to Identity home', '/']]);
    },
  );
});
