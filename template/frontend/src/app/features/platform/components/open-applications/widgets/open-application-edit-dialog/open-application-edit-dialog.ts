//#if (IncludeLocalization)
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';
//#else
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  model,
  output,
  signal,
} from '@angular/core';
//#endif
import { form, required, disabled, validate, FormField } from '@angular/forms/signals';
//#if (IncludeLocalization)
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
//#endif
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleCheck } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmSeparator } from '@spartan-ng/helm/separator';
import { HlmSpinner } from '@spartan-ng/helm/spinner';

//#if (IncludeLocalization)
import { translationReady } from '../../../../../../core/i18n/translation-ready';
//#endif
import { DialogLoading } from '../../../../../../shared/components/dialog-loading/dialog-loading';
import {
  CreateOpenApplicationInputDto,
  OpenApplicationClientType,
  OpenApplicationConsentType,
  OpenApplicationOutputDto,
  OpenApplicationType,
  UpdateOpenApplicationInputDto,
} from '../../../../models/open-application.dto';
import { UriListEditor } from '../uri-list-editor/uri-list-editor';

type OpenApplicationTemplate = 'web' | 'desktop' | 'service';

interface OpenApplicationEditFormModel {
  clientId: string;
  displayName: string;
  applicationType: OpenApplicationType;
  clientType: OpenApplicationClientType;
  consentType: OpenApplicationConsentType;
  redirectUris: string[];
  postLogoutRedirectUris: string[];
  permissions: string[];
  requirements: string[];
}

const authorizationCodePermissions = [
  'ept:authorization',
  'ept:end_session',
  'ept:token',
  'gt:authorization_code',
  'gt:refresh_token',
  'rst:code',
  'scp:openid',
  'scp:profile',
  'scp:email',
  'scp:roles',
  'scp:offline_access',
];

@Component({
  selector: 'app-open-application-edit-dialog',
  imports: [
    FormField,
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmInput,
    HlmSpinner,
    HlmSeparator,
    ...HlmDialogImports,
    ...HlmFieldImports,
    ...HlmSelectImports,
    //#if (IncludeLocalization)
    TranslocoModule,
    //#endif
    DialogLoading,
    UriListEditor,
  ],
  providers: [provideIcons({ lucideCircleCheck })],
  templateUrl: './open-application-edit-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OpenApplicationEditDialog {
  //#if (IncludeLocalization)
  private readonly transloco = inject(TranslocoService);
  // 追踪「翻译就绪」：资源加载完成与语言切换时重算，含首帧避免裸键。
  private readonly translationReady = translationReady(this.transloco);
  readonly dialogHeader = () =>
    this.transloco.translate(
      this.isEditMode() ? 'openApp.dialog.editHeader' : 'openApp.dialog.createHeader',
    );
  readonly templatePlaceholder = () =>
    this.transloco.translate('openApp.field.templatePlaceholder');
  readonly displayNamePlaceholder = () =>
    this.transloco.translate('openApp.field.displayNamePlaceholder');
  readonly clientIdPlaceholder = () =>
    this.transloco.translate('openApp.field.clientIdPlaceholder');
  readonly redirectUriPlaceholder = () =>
    this.transloco.translate('openApp.redirectUri.placeholder');
  readonly addLabel = () => this.transloco.translate('common.add');
  readonly permissionsPlaceholder = () =>
    this.transloco.translate('openApp.permissions.placeholder');
  readonly requirementsPlaceholder = () =>
    this.transloco.translate('openApp.requirements.placeholder');
  readonly redirectUrisLabel = () => this.transloco.translate('openApp.field.redirectUris');
  readonly postLogoutUrisLabel = () =>
    this.transloco.translate('openApp.field.postLogoutRedirectUris');
  readonly redirectUriHint = () => this.transloco.translate('openApp.redirectUri.hint');
  readonly postLogoutUriHint = () => this.transloco.translate('openApp.postLogoutUri.hint');
  readonly redirectUriInvalid = () => this.transloco.translate('openApp.redirectUri.invalid');
  readonly redirectUriEmpty = () => this.transloco.translate('openApp.redirectUri.empty');
  readonly postLogoutUriEmpty = () => this.transloco.translate('openApp.postLogoutUri.empty');
  //#else
  readonly dialogHeader = () =>
    this.isEditMode() ? 'Edit Open Application' : 'New Open Application';
  readonly templatePlaceholder = () => 'Select a template to quickly fill in';
  readonly displayNamePlaceholder = () => 'e.g. My Desktop App';
  readonly clientIdPlaceholder = () => 'e.g. my-desktop-app';
  readonly redirectUriPlaceholder = () =>
    'https://example.com/callback or my-desktop-app://oauth/callback';
  readonly addLabel = () => 'Add';
  readonly permissionsPlaceholder = () => 'Select authorization capabilities';
  readonly requirementsPlaceholder = () => 'Select security requirements';
  readonly redirectUrisLabel = () => 'Redirect URIs';
  readonly postLogoutUrisLabel = () => 'Post Logout Redirect URIs';
  readonly redirectUriHint = () => 'Callback URI after sign-in completes';
  readonly postLogoutUriHint = () => 'Redirect URI after logout';
  readonly redirectUriInvalid = () => 'Please enter a valid absolute URI without a fragment.';
  readonly redirectUriEmpty = () => 'No Redirect URI configured yet';
  readonly postLogoutUriEmpty = () => 'No Post Logout Redirect URI configured yet';
  //#endif
  readonly visible = model(false);
  readonly loading = input(false);
  readonly saving = input(false);
  readonly application = input<OpenApplicationOutputDto | null>(null);
  readonly saved = output<CreateOpenApplicationInputDto | UpdateOpenApplicationInputDto>();

  protected readonly formModel = signal<OpenApplicationEditFormModel>(this.createEmptyModel());
  selectedTemplate = signal<OpenApplicationTemplate | null>(null);

  isEditMode = computed(() => !!this.application());
  isTemplateLocked = computed(() => !!this.selectedTemplate() && !this.isEditMode());
  isConfidentialClient = computed(() => this.formModel().clientType === 'confidential');
  isServiceType = computed(() => this.formModel().applicationType === 'service');

  //#if (IncludeLocalization)
  readonly applicationForm = form(this.formModel, (path) => {
    required(path.clientId, {
      message: this.transloco.translate('common.validation.required'),
      when: () => !this.isEditMode(),
    });
    // 编辑模式禁用 Client ID（不可改）。
    disabled(path.clientId, { when: () => this.isEditMode() });
    // 跨字段：Native / Public 客户端必须启用 PKCE。
    validate(path.requirements, (ctx) => {
      const requirements = ctx.value();
      const applicationType = ctx.valueOf(path.applicationType);
      const clientType = ctx.valueOf(path.clientType);
      if (
        (applicationType === 'native' || clientType === 'public') &&
        !requirements.includes('ft:pkce')
      ) {
        return {
          kind: 'pkceRequired',
          message: this.transloco.translate('openApp.requirements.pkceRequired'),
        };
      }
      return null;
    });
  });
  //#else
  readonly applicationForm = form(this.formModel, (path) => {
    required(path.clientId, {
      message: 'This field is required.',
      when: () => !this.isEditMode(),
    });
    // 编辑模式禁用 Client ID（不可改）。
    disabled(path.clientId, { when: () => this.isEditMode() });
    // 跨字段：Native / Public 客户端必须启用 PKCE。
    validate(path.requirements, (ctx) => {
      const requirements = ctx.value();
      const applicationType = ctx.valueOf(path.applicationType);
      const clientType = ctx.valueOf(path.clientType);
      if (
        (applicationType === 'native' || clientType === 'public') &&
        !requirements.includes('ft:pkce')
      ) {
        return { kind: 'pkceRequired', message: 'Native/Public clients must enable PKCE.' };
      }
      return null;
    });
  });
  //#endif

  //#if (IncludeLocalization)
  // 读一次 translationReady 建立依赖：资源就绪 / 语言切换时本 computed 重算，选项标签重新翻译。
  readonly templateOptions = computed(() => {
    this.translationReady();
    return [
      { label: this.transloco.translate('openApp.template.web'), value: 'web' as const },
      { label: this.transloco.translate('openApp.template.desktop'), value: 'desktop' as const },
      { label: this.transloco.translate('openApp.template.service'), value: 'service' as const },
    ];
  });

  readonly applicationTypeOptions = computed(() => {
    this.translationReady();
    return [
      { label: this.transloco.translate('openApp.appType.web'), value: 'web' as const },
      { label: this.transloco.translate('openApp.appType.native'), value: 'native' as const },
      { label: this.transloco.translate('openApp.appType.service'), value: 'service' as const },
    ];
  });

  readonly clientTypeOptions = computed(() => {
    this.translationReady();
    return [
      {
        label: this.transloco.translate('openApp.clientType.publicLabel'),
        value: 'public' as const,
      },
      {
        label: this.transloco.translate('openApp.clientType.confidentialLabel'),
        value: 'confidential' as const,
      },
    ];
  });

  readonly consentTypeOptions = computed(() => {
    this.translationReady();
    return [
      {
        label: this.transloco.translate('openApp.consentType.implicit'),
        value: 'implicit' as const,
      },
      {
        label: this.transloco.translate('openApp.consentType.explicit'),
        value: 'explicit' as const,
      },
      {
        label: this.transloco.translate('openApp.consentType.external'),
        value: 'external' as const,
      },
      {
        label: this.transloco.translate('openApp.consentType.systematic'),
        value: 'systematic' as const,
      },
    ];
  });

  readonly permissionOptions = computed(() => {
    this.translationReady();
    return [
      {
        label: this.transloco.translate('openApp.permission.authorizationEndpoint'),
        value: 'ept:authorization',
        group: 'Endpoints',
      },
      {
        label: this.transloco.translate('openApp.permission.tokenEndpoint'),
        value: 'ept:token',
        group: 'Endpoints',
      },
      {
        label: this.transloco.translate('openApp.permission.endSessionEndpoint'),
        value: 'ept:end_session',
        group: 'Endpoints',
      },
      {
        label: this.transloco.translate('openApp.permission.authorizationCode'),
        value: 'gt:authorization_code',
        group: 'Grant Types',
      },
      {
        label: this.transloco.translate('openApp.permission.refreshToken'),
        value: 'gt:refresh_token',
        group: 'Grant Types',
      },
      {
        label: this.transloco.translate('openApp.permission.clientCredentials'),
        value: 'gt:client_credentials',
        group: 'Grant Types',
      },
      {
        label: this.transloco.translate('openApp.permission.codeResponse'),
        value: 'rst:code',
        group: 'Response Types',
      },
      { label: 'openid', value: 'scp:openid', group: 'Scopes' },
      { label: 'profile', value: 'scp:profile', group: 'Scopes' },
      { label: 'email', value: 'scp:email', group: 'Scopes' },
      { label: 'roles', value: 'scp:roles', group: 'Scopes' },
      { label: 'offline_access', value: 'scp:offline_access', group: 'Scopes' },
    ];
  });

  readonly requirementOptions = computed(() => {
    this.translationReady();
    return [{ label: this.transloco.translate('openApp.requirement.forcePkce'), value: 'ft:pkce' }];
  });

  readonly permissionLabels = computed<Record<string, string>>(() => {
    this.translationReady();
    return {
      'ept:authorization': this.transloco.translate('openApp.permission.authorizationEndpoint'),
      'ept:token': this.transloco.translate('openApp.permission.tokenEndpoint'),
      'ept:end_session': this.transloco.translate('openApp.permission.endSessionEndpoint'),
      'gt:authorization_code': this.transloco.translate('openApp.permission.authorizationCodeFlow'),
      'gt:refresh_token': this.transloco.translate('openApp.permission.refreshToken'),
      'gt:client_credentials': this.transloco.translate('openApp.permission.clientCredentials'),
      'rst:code': this.transloco.translate('openApp.permission.codeResponse'),
      'scp:openid': this.transloco.translate('openApp.permission.scopeOpenid'),
      'scp:profile': this.transloco.translate('openApp.permission.scopeProfile'),
      'scp:email': this.transloco.translate('openApp.permission.scopeEmail'),
      'scp:roles': this.transloco.translate('openApp.permission.scopeRoles'),
      'scp:offline_access': this.transloco.translate('openApp.permission.scopeOfflineAccess'),
    };
  });

  readonly applicationTypeLabels = computed<Record<string, string>>(() => {
    this.translationReady();
    return {
      web: this.transloco.translate('openApp.appType.web'),
      native: this.transloco.translate('openApp.appType.native'),
      service: this.transloco.translate('openApp.appType.service'),
    };
  });

  readonly clientTypeLabels = computed<Record<string, string>>(() => {
    this.translationReady();
    return {
      public: this.transloco.translate('openApp.clientType.publicLabel'),
      confidential: this.transloco.translate('openApp.clientType.confidentialLabel'),
    };
  });

  readonly consentTypeLabels = computed<Record<string, string>>(() => {
    this.translationReady();
    return {
      implicit: this.transloco.translate('openApp.consentType.implicit'),
      explicit: this.transloco.translate('openApp.consentType.explicit'),
      external: this.transloco.translate('openApp.consentType.external'),
      systematic: this.transloco.translate('openApp.consentType.systematic'),
    };
  });
  //#else
  readonly templateOptions = computed(() => [
    { label: 'Web PKCE client', value: 'web' as const },
    { label: 'Desktop PKCE client', value: 'desktop' as const },
    { label: 'Service confidential client', value: 'service' as const },
  ]);

  readonly applicationTypeOptions = computed(() => [
    { label: 'Web', value: 'web' as const },
    { label: 'Desktop/Native', value: 'native' as const },
    { label: 'Service', value: 'service' as const },
  ]);

  readonly clientTypeOptions = computed(() => [
    { label: 'Public', value: 'public' as const },
    { label: 'Confidential', value: 'confidential' as const },
  ]);

  readonly consentTypeOptions = computed(() => [
    { label: 'Implicit consent', value: 'implicit' as const },
    { label: 'Explicit consent', value: 'explicit' as const },
    { label: 'External consent', value: 'external' as const },
    { label: 'Systematic consent', value: 'systematic' as const },
  ]);

  readonly permissionOptions = computed(() => [
    { label: 'Authorization endpoint', value: 'ept:authorization', group: 'Endpoints' },
    { label: 'Token endpoint', value: 'ept:token', group: 'Endpoints' },
    { label: 'End session endpoint', value: 'ept:end_session', group: 'Endpoints' },
    { label: 'Authorization code', value: 'gt:authorization_code', group: 'Grant Types' },
    { label: 'Refresh token', value: 'gt:refresh_token', group: 'Grant Types' },
    { label: 'Client credentials', value: 'gt:client_credentials', group: 'Grant Types' },
    { label: 'Code response', value: 'rst:code', group: 'Response Types' },
    { label: 'openid', value: 'scp:openid', group: 'Scopes' },
    { label: 'profile', value: 'scp:profile', group: 'Scopes' },
    { label: 'email', value: 'scp:email', group: 'Scopes' },
    { label: 'roles', value: 'scp:roles', group: 'Scopes' },
    { label: 'offline_access', value: 'scp:offline_access', group: 'Scopes' },
  ]);

  readonly requirementOptions = computed(() => [{ label: 'Force PKCE', value: 'ft:pkce' }]);

  readonly permissionLabels = computed<Record<string, string>>(() => ({
    'ept:authorization': 'Authorization endpoint',
    'ept:token': 'Token endpoint',
    'ept:end_session': 'End session endpoint',
    'gt:authorization_code': 'Authorization code flow',
    'gt:refresh_token': 'Refresh token',
    'gt:client_credentials': 'Client credentials',
    'rst:code': 'Code response',
    'scp:openid': 'Identity',
    'scp:profile': 'Profile',
    'scp:email': 'Email',
    'scp:roles': 'Roles',
    'scp:offline_access': 'Offline access',
  }));

  readonly applicationTypeLabels = computed<Record<string, string>>(() => ({
    web: 'Web',
    native: 'Desktop/Native',
    service: 'Service',
  }));

  readonly clientTypeLabels = computed<Record<string, string>>(() => ({
    public: 'Public',
    confidential: 'Confidential',
  }));

  readonly consentTypeLabels = computed<Record<string, string>>(() => ({
    implicit: 'Implicit consent',
    explicit: 'Explicit consent',
    external: 'External consent',
    systematic: 'Systematic consent',
  }));
  //#endif

  getPermissionLabel(value: string): string {
    return this.permissionLabels()[value] ?? value;
  }

  constructor() {
    // 同时依赖 visible 与 application：每次对话框打开都重置表单，避免新建模式残留上次输入
    // （application 信号从 null 到 null 不变化时 effect 不会重跑，需借 visible 触发）
    effect(() => {
      const application = this.application();
      if (!this.visible()) {
        return;
      }
      if (application) {
        this.formModel.set({
          clientId: application.clientId,
          displayName: application.displayName ?? '',
          applicationType: application.applicationType,
          clientType: application.clientType,
          consentType: application.consentType,
          redirectUris: [...application.redirectUris],
          postLogoutRedirectUris: [...application.postLogoutRedirectUris],
          permissions: [...application.permissions],
          requirements: [...application.requirements],
        });
      } else {
        this.formModel.set(this.createEmptyModel());
      }
    });
  }

  createEmptyModel(): OpenApplicationEditFormModel {
    return {
      clientId: '',
      displayName: '',
      applicationType: 'web',
      clientType: 'public',
      consentType: 'explicit',
      redirectUris: [],
      postLogoutRedirectUris: [],
      permissions: [...authorizationCodePermissions],
      requirements: ['ft:pkce'],
    };
  }

  applyTemplate(template: OpenApplicationTemplate | null | undefined) {
    if (!template) {
      this.selectedTemplate.set(null);
      return;
    }
    this.selectedTemplate.set(template);
    if (template === 'desktop') {
      this.formModel.update((model) => ({
        ...model,
        clientId: model.clientId || 'my-desktop-app',
        //#if (IncludeLocalization)
        displayName:
          model.displayName || this.transloco.translate('openApp.template.desktopDisplayName'),
        //#else
        displayName: model.displayName || 'My Desktop App',
        //#endif
        applicationType: 'native',
        clientType: 'public',
        consentType: 'explicit',
        redirectUris: ['my-desktop-app://oauth/callback'],
        postLogoutRedirectUris: ['my-desktop-app://oauth/logout-callback'],
        permissions: [...authorizationCodePermissions],
        requirements: ['ft:pkce'],
      }));
      return;
    }

    if (template === 'service') {
      this.formModel.update((model) => ({
        ...model,
        applicationType: 'service',
        clientType: 'confidential',
        consentType: 'systematic',
        redirectUris: [],
        postLogoutRedirectUris: [],
        permissions: ['ept:token', 'gt:client_credentials'],
        requirements: [],
      }));
      return;
    }

    this.formModel.update((model) => ({
      ...model,
      applicationType: 'web',
      clientType: 'public',
      consentType: 'explicit',
      permissions: [...authorizationCodePermissions],
      requirements: ['ft:pkce'],
    }));
  }

  onApplicationTypeChange(value: OpenApplicationType | null | undefined) {
    if (!value) {
      return;
    }
    this.formModel.update((model) => ({ ...model, applicationType: value }));
  }

  onConsentTypeChange(value: OpenApplicationConsentType | null | undefined) {
    if (!value) {
      return;
    }
    this.formModel.update((model) => ({ ...model, consentType: value }));
  }

  onClientTypeSelect(clientType: OpenApplicationClientType | null | undefined) {
    if (!clientType) {
      return;
    }
    this.formModel.update((model) => ({
      ...model,
      clientType,
      requirements:
        clientType === 'public'
          ? this.ensurePkce(model.requirements)
          : model.requirements.filter((r) => r !== 'ft:pkce'),
    }));
  }

  addRedirectUri(uri: string) {
    this.formModel.update((model) => ({
      ...model,
      redirectUris: model.redirectUris.includes(uri)
        ? model.redirectUris
        : [...model.redirectUris, uri],
    }));
  }

  addPostLogoutRedirectUri(uri: string) {
    this.formModel.update((model) => ({
      ...model,
      postLogoutRedirectUris: model.postLogoutRedirectUris.includes(uri)
        ? model.postLogoutRedirectUris
        : [...model.postLogoutRedirectUris, uri],
    }));
  }

  removeRedirectUri(uri: string) {
    this.formModel.update((model) => ({
      ...model,
      redirectUris: model.redirectUris.filter((item) => item !== uri),
    }));
  }

  removePostLogoutRedirectUri(uri: string) {
    this.formModel.update((model) => ({
      ...model,
      postLogoutRedirectUris: model.postLogoutRedirectUris.filter((item) => item !== uri),
    }));
  }

  /** 桥接 hlm-dialog 声明式 state 到对外 visible 契约。 */
  onDialogStateChange(state: BrnDialogState): void {
    this.visible.set(state === 'open');
    if (state === 'closed') {
      this.onHide();
    }
  }

  onHide() {
    this.visible.set(false);
    this.formModel.set(this.createEmptyModel());
    this.selectedTemplate.set(null);
  }

  save() {
    if (this.applicationForm().invalid()) {
      this.applicationForm().markAsTouched();
      return;
    }

    const model = this.formModel();
    if (this.isEditMode()) {
      this.saved.emit({
        displayName: model.displayName,
        applicationType: model.applicationType,
        clientType: model.clientType,
        consentType: model.consentType,
        redirectUris: model.redirectUris,
        postLogoutRedirectUris: model.postLogoutRedirectUris,
        permissions: model.permissions,
        requirements: model.requirements,
      });
      return;
    }

    this.saved.emit({
      clientId: model.clientId.trim(),
      displayName: model.displayName,
      applicationType: model.applicationType,
      clientType: model.clientType,
      consentType: model.consentType,
      redirectUris: model.redirectUris,
      postLogoutRedirectUris: model.postLogoutRedirectUris,
      permissions: model.permissions,
      requirements: model.requirements,
    });
  }

  private ensurePkce(requirements: string[]) {
    return requirements.includes('ft:pkce') ? requirements : [...requirements, 'ft:pkce'];
  }
}
