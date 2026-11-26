import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';

//#if (IncludeLocalization)
import en from '../../../../../public/i18n/signInFailed/en.json';
import { provideTranslocoTesting } from '../../i18n/transloco.testing';
//#endif
import { SignInFailed } from './sign-in-failed';

describe('SignInFailed', () => {
  it.each([
    ['expired', 'Sign-in timed out', 'You stayed on the sign-in page too long'],
    ['denied', 'Sign-in was not completed', 'The sign-in was cancelled or refused.'],
    [
      'error',
      'Something went wrong while signing in',
      'If it keeps failing, contact your administrator.',
    ],
    [
      'unknown',
      'Something went wrong while signing in',
      'If it keeps failing, contact your administrator.',
    ],
  ] as const)('shows actionable copy and links for %s', async (reason, title, description) => {
    TestBed.configureTestingModule({
      imports: [SignInFailed],
      providers: [
        provideRouter([]),
        //#if (IncludeLocalization)
        ...provideTranslocoTesting(['en'], { getTranslation: () => of({ signInFailed: en }) }),
        //#endif
        {
          provide: ActivatedRoute,
          useValue: { queryParamMap: of(convertToParamMap({ reason })) },
        },
      ],
    });
    const fixture = TestBed.createComponent(SignInFailed);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;

    expect(page.querySelector('h1')?.textContent?.trim()).toBe(title);
    expect(page.querySelector('p')?.textContent).toContain(description);
    const links = Array.from(page.querySelectorAll('a'), (link) => [
      link.textContent?.trim(),
      link.getAttribute('href'),
    ]);
    expect(links).toEqual(
      reason === 'denied'
        ? [
            ['Sign in again', '/api/v1/auth/login'],
            ['Back to home', '/'],
          ]
        : [['Sign in again', '/api/v1/auth/login']],
    );
  });
});
