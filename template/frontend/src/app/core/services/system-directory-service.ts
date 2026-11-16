//#if (OpenIddictServer || RemoteTokenAuth)
import { HttpClient } from '@angular/common/http';
import { Injectable, effect, inject, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';

import { AuthService } from './auth-service';
//#if (IncludeLocalization)
import { LanguageService } from './language-service';
//#endif
import { LayoutService } from './layout-service';
//#if (TenantWorkspace)
import { WorkspaceContextService } from './workspace-context-service';
//#endif
import { SystemOutputDto } from '../../shared/dtos/system.dto';
import { FullPageNavigationService } from '../routing/full-page-navigation-service';

/** 品牌块与命令面板共用目录与跳转规则，目录随本人会话和语言重新读取。 */
@Injectable({ providedIn: 'root' })
export class SystemDirectoryService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly layout = inject(LayoutService);
  private readonly router = inject(Router);
  private readonly navigation = inject(FullPageNavigationService);
  //#if (IncludeLocalization)
  private readonly language = inject(LanguageService);
  //#endif
  //#if (TenantWorkspace)
  private readonly workspace = inject(WorkspaceContextService);
  //#endif
  readonly systems = signal<readonly SystemOutputDto[]>([]);

  constructor() {
    effect((onCleanup) => {
      const user = this.auth.currentUser();
      //#if (IncludeLocalization)
      this.language.activeLang();
      //#endif
      this.systems.set([]);
      if (!user) return;
      const subscription = untracked(() =>
        this.http.get<SystemOutputDto[]>('/api/v1/systems').subscribe({
          next: (systems) => this.systems.set(systems),
          error: () => this.systems.set([]),
        }),
      );
      onCleanup(() => subscription.unsubscribe());
    });
  }

  switchTo(system: SystemOutputDto): void {
    if (system.isCurrent) {
      void this.router.navigateByUrl(this.layout.homeRoute());
      return;
    }
    //#if (TenantWorkspace)
    let target = system.homeUrl;
    const workspace = this.workspace.current();
    if (workspace && !this.auth.currentUser()?.tenantId) {
      const url = new URL(target);
      url.pathname = `${url.pathname.replace(/\/$/, '')}/w/${encodeURIComponent(workspace)}/platform`;
      url.search = '';
      url.hash = '';
      target = url.href;
    }
    //#else
    const target = system.homeUrl;
    //#endif
    this.navigation.assign(target);
  }
}
//#endif
