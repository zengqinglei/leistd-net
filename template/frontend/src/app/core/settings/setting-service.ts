import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { SettingScope } from './setting-groups';
// prettier-ignore
import {
  //#if (ExternalLogin)
  ExternalLoginStatusOutputDto,
  //#endif
  SetSettingInputDto,
  SettingOutputDto,
} from './setting.dto';

@Injectable({ providedIn: 'root' })
export class SettingService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/settings';

  /** 读取当前生效的设置值。 */
  getSettings(): Observable<SettingOutputDto[]> {
    return this.http.get<SettingOutputDto[]>(this.baseUrl);
  }

  /** 写入当前用户自己的偏好。 */
  setForCurrentUser(data: SetSettingInputDto): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/current-user`, data);
  }

  /** 写入当前租户的默认值；与用户偏好分成两个端点，因为两者授权要求不同。 */
  setForCurrentTenant(data: SetSettingInputDto): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/current-tenant`, data);
  }

  /**
   * 设置页写入一行：按作用域选端点，设置分区不自己选。系统作用域里进程级设置（`allowsTenantScope` 为假）
   * 同样走当前租户端点：宿主上下文下"当前租户"就是宿主那一行。另设宿主写入端点的项目在这里按设置层级分流。
   */
  saveSetting(
    setting: SettingOutputDto,
    scope: SettingScope,
    value: string | null,
  ): Observable<void> {
    const data = { name: setting.name, value };
    return scope === 'account' ? this.setForCurrentUser(data) : this.setForCurrentTenant(data);
  }
  //#if (LocalIdentity)

  /** 用当前生效的发信参数发一封测试邮件（宿主）。 */
  sendTestEmail(to: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/email/test`, { to });
  }
  //#endif
  //#if (ExternalLogin)

  /** 外部登录各提供方在本实例上的生效状态（宿主）。 */
  getExternalLoginStatus(): Observable<ExternalLoginStatusOutputDto> {
    return this.http.get<ExternalLoginStatusOutputDto>(`${this.baseUrl}/external-login/status`);
  }
  //#endif
}
