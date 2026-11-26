import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBan, lucideHourglass, lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
//#if (!IncludeLocalization)

import { englishText } from '../../../shared/utils/english-text';
//#endif

@Component({
  selector: 'app-sign-in-failed',
  // prettier-ignore
  imports: [
    NgIcon, HlmButton, RouterLink, ...HlmEmptyImports,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideBan, lucideHourglass, lucideTriangleAlert })],
  templateUrl: './sign-in-failed.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SignInFailed {
  private readonly params = toSignal(inject(ActivatedRoute).queryParamMap, { requireSync: true });
  protected readonly reason = computed(() => {
    const reason = this.params().get('reason');
    return reason === 'expired' || reason === 'denied' ? reason : 'error';
  });
  //#if (!IncludeLocalization)
  protected readonly t = englishText(ENGLISH);
  //#endif
}
//#if (!IncludeLocalization)

const ENGLISH: Record<string, string> = {
  'signInFailed.expiredTitle': 'Sign-in timed out',
  'signInFailed.expiredDescription':
    'You stayed on the sign-in page too long and the sign-in did not finish. Please sign in again.',
  'signInFailed.deniedTitle': 'Sign-in was not completed',
  'signInFailed.deniedDescription':
    'The sign-in was cancelled or refused. Sign in again to continue.',
  'signInFailed.errorTitle': 'Something went wrong while signing in',
  'signInFailed.errorDescription':
    'The sign-in did not finish. Please sign in again. If it keeps failing, contact your administrator.',
  'signInFailed.signInAgain': 'Sign in again',
  'signInFailed.backHome': 'Back to home',
};
//#endif
