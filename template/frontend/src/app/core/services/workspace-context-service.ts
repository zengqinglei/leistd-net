//#if (TenantWorkspace)
import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, Injector, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { AuthService } from './auth-service';
import { AuthorizationService } from './authorization-service';
import { SessionContextService } from './session-context-service';
//#if (IncludeRealTime || IncludeNotifications)
import { SignalRService } from './signalr-service';
//#endif
import { WorkspaceOutputDto } from '../dtos/workspace.dto';
import { WORKSPACE_SCOPE } from '../interceptors/http-context-tokens';
import { EntryRouteService } from '../routing/entry-route-service';

/** 当前范围只存在本标签页内；存储只记最后一次成功的落点。 */
@Injectable({ providedIn: 'root' })
export class WorkspaceContextService {
  private readonly injector = inject(Injector);
  private get http() {
    return this.injector.get(HttpClient);
  }
  private readonly router = inject(Router);
  private get entry() {
    return this.injector.get(EntryRouteService);
  }
  private get session() {
    return this.injector.get(SessionContextService);
  }
  get auth() {
    return this.injector.get(AuthService);
  }
  private get authorization() {
    return this.injector.get(AuthorizationService);
  }
  readonly workspaces = signal<readonly WorkspaceOutputDto[]>([]);
  readonly target = signal<string | null | undefined>(undefined);
  readonly current = signal<string | null>(null);
  readonly filter = signal('');
  readonly actor = computed(() => {
    const scope = this.requestScope();
    return scope ? this.workspaces().find((workspace) => workspace.name === scope) : null;
  });
  readonly actorId = computed(() => this.actor()?.userId ?? this.auth.currentUser()?.id);
  readonly tenantId = computed(() => this.actor()?.tenantId ?? this.auth.currentUser()?.tenantId);
  private revision = 0;
  //#if (IncludeRealTime || IncludeNotifications)
  private get signalR() {
    return this.injector.get(SignalRService);
  }
  //#endif

  constructor() {
    this.router.events.pipe(takeUntilDestroyed()).subscribe((event) => {
      if (event instanceof NavigationEnd) {
        const routeName = this.nameFromUrl(event.urlAfterRedirects);
        const name =
          this.workspaces().find((item) => item.name.toLowerCase() === routeName?.toLowerCase())
            ?.name ?? routeName;
        this.current.set(name);
        if (name) localStorage.setItem('last-workspace', name);
      }
    });
  }

  clear(): void {
    this.revision++;
    this.workspaces.set([]);
    this.target.set(null);
    this.current.set(null);
    this.filter.set('');
  }

  nameFromUrl(url: string): string | null {
    const name = /^\/w\/([^/?#]+)/.exec(url)?.[1];
    return name ? decodeURIComponent(name) : null;
  }

  requestScope(): string | null {
    return this.target() === undefined
      ? this.nameFromUrl(this.entry.url())
      : (this.target() ?? null);
  }

  async refresh(): Promise<void> {
    const list = await firstValueFrom(
      this.http.get<WorkspaceOutputDto[]>('/api/v1/auth/me/workspaces', {
        context: new HttpContext().set(WORKSPACE_SCOPE, null),
      }),
    );
    this.workspaces.set(list);
  }

  async establish(name: string | null): Promise<boolean> {
    if (this.target() === name && this.authorization.loaded()) return true;
    const revision = ++this.revision;
    this.target.set(name);
    this.authorization.clear();
    //#if (IncludeRealTime || IncludeNotifications)
    await this.signalR.setWorkspace(name, !this.auth.currentUser()?.tenantId);
    //#endif
    await this.session.establish();
    return revision === this.revision;
  }

  path(route: string): string {
    const name = this.current();
    return name ? `/w/${encodeURIComponent(name)}${route}` : route;
  }

  switchTo(name: string | null): void {
    const suffix = this.router.url.replace(/^\/w\/[^/]+/, '').split(/[?#]/)[0];
    const destination = name
      ? `/w/${encodeURIComponent(
          name,
        )}${suffix.startsWith('/platform') || suffix.startsWith('/workspace') ? suffix : '/platform'}`
      : '/platform';
    void this.router.navigateByUrl(destination);
  }

  async recoverUnavailable(): Promise<void> {
    await this.refresh();
    this.switchTo(this.workspaces()[0]?.name ?? null);
  }
}
//#endif
