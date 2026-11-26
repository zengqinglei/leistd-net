import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideClock, lucideLogOut } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { catchError, distinctUntilChanged, map, of, switchMap } from 'rxjs';

//#if (!IncludeLocalization)
import { englishText } from '../../../../shared/utils/english-text';
//#endif
import { AccountService } from '../../services/account-service';

@Component({
  selector: 'app-identity-expired',
  // prettier-ignore
  imports: [
    NgIcon, HlmButton, RouterLink, ...HlmEmptyImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideClock, lucideLogOut })],
  templateUrl: './identity-expired.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IdentityExpired {
  private readonly route = inject(ActivatedRoute);
  private readonly account = inject(AccountService);
  protected readonly logout = this.route.snapshot.data['logout'] === true;
  protected readonly application = toSignal(
    this.route.queryParamMap.pipe(
      map((params) => params.get('client_id')),
      distinctUntilChanged(),
      switchMap((clientId) =>
        clientId
          ? this.account.getInteractionApplication(clientId).pipe(catchError(() => of(null)))
          : of(null),
      ),
    ),
    { initialValue: null },
  );
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
}
//#if (!IncludeLocalization)

const ENGLISH: Record<string, string> = {
  'identityExpiry.loginTitle': 'This sign-in request has expired',
  'identityExpiry.loginDescription':
    'You stayed on this page too long, so this sign-in request is no longer valid. Go back to {{app}} and sign in again.',
  'identityExpiry.loginDescriptionUnknown':
    'This sign-in request is no longer valid. Go back to the app you were using and sign in again.',
  'identityExpiry.logoutTitle': 'This sign-out request has expired',
  'identityExpiry.logoutDescription':
    'You may already be signed out. If you are still signed in, go back to {{app}} and sign out again.',
  'identityExpiry.logoutDescriptionUnknown':
    'You may already be signed out. If you are still signed in, go back to the app you were using and sign out again.',
  'identityExpiry.signInAgain': 'Sign in to {{app}} again',
  'identityExpiry.backToApp': 'Back to {{app}}',
  'identityExpiry.backHome': 'Back to Identity home',
};
//#endif
