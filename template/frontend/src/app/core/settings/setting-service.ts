import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { SetSettingInputDto, SettingOutputDto } from './setting.dto';

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

  /**
   * 写入当前租户的默认值。
   *
   * 与用户偏好分成两个端点而不是一个带 scope 参数的端点：两者授权要求不同，
   * 合成一个会让这层差异藏进请求体。
   */
  setForCurrentTenant(data: SetSettingInputDto): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/current-tenant`, data);
  }
}
