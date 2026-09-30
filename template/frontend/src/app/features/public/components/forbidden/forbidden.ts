import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
//#if (IncludeLocalization)
import { TranslocoDirective } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideShieldOff } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
//#if (!IncludeLocalization)

import { englishText } from '../../../../shared/utils/english-text';
//#endif

/**
 * 403 页面：已登录但缺少所需权限。
 *
 * 与 401 的登录跳转严格区分——未认证应去登录，已认证但无权限应停在这里，
 * 否则用户会陷入"登录成功又被弹回登录页"的循环。
 */
@Component({
  selector: 'app-forbidden',
  standalone: true,
  // prettier-ignore
  imports: [
    RouterLink,
    NgIcon,
    HlmButton,
    //#if (IncludeLocalization)
    TranslocoDirective,
    //#endif
  ],
  providers: [provideIcons({ lucideShieldOff })],
  templateUrl: './forbidden.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
//#if (IncludeLocalization)
export class Forbidden {}
//#else
export class Forbidden {
  protected readonly t = englishText(ENGLISH);
}
//#endif
//#if (!IncludeLocalization)

/** 不含本地化时的界面文案，与 `en.json` 同步。 */
const ENGLISH: Record<string, string> = {
  'forbidden.title': 'Access denied',
  'forbidden.description':
    'You are signed in, but you do not have permission to view this page. Ask an administrator to grant it.',
  'forbidden.backToWorkspace': 'Back to workspace',
  'forbidden.backHome': 'Go home',
};
//#endif
