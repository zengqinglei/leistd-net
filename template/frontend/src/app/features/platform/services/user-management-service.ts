import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PagedResultDto } from '../../../shared/models/paged-result.dto';
import { RoleBriefDto } from '../models/role.dto';
import {
  CreateUserInputDto,
  GetUsersInputDto,
  //#if (LocalIdentity)
  ResetUserPasswordInputDto,
  //#endif
  UpdateUserInputDto,
  UpdateUserRolesInputDto,
  UserManagementOutputDto,
} from '../models/user-management.dto';

@Injectable({ providedIn: 'root' })
export class UserManagementService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/users';

  getUsers(input: GetUsersInputDto): Observable<PagedResultDto<UserManagementOutputDto>> {
    let params = new HttpParams();
    if (input.offset !== undefined) params = params.set('offset', input.offset.toString());
    if (input.limit !== undefined) params = params.set('limit', input.limit.toString());
    if (input.keyword) params = params.set('keyword', input.keyword);
    if (input.isActive !== undefined) params = params.set('isActive', input.isActive.toString());
    //#if (LocalIdentity)
    if (input.isEmailVerified !== undefined)
      params = params.set('isEmailVerified', input.isEmailVerified.toString());
    //#endif
    if (input.roles?.length) {
      for (const role of input.roles) {
        params = params.append('roles', role);
      }
    }
    if (input.sorting) params = params.set('sorting', input.sorting);
    return this.http.get<PagedResultDto<UserManagementOutputDto>>(this.baseUrl, { params });
  }

  getUser(id: string): Observable<UserManagementOutputDto> {
    return this.http.get<UserManagementOutputDto>(`${this.baseUrl}/${id}`);
  }

  createUser(data: CreateUserInputDto): Observable<UserManagementOutputDto> {
    return this.http.post<UserManagementOutputDto>(this.baseUrl, data);
  }

  updateUser(id: string, data: UpdateUserInputDto): Observable<UserManagementOutputDto> {
    return this.http.put<UserManagementOutputDto>(`${this.baseUrl}/${id}`, data);
  }

  enableUser(id: string): Observable<void> {
    return this.http.patch<void>(`${this.baseUrl}/${id}/enable`, {});
  }

  disableUser(id: string): Observable<void> {
    return this.http.patch<void>(`${this.baseUrl}/${id}/disable`, {});
  }
  //#if (LocalIdentity)
  resetPassword(id: string, data: ResetUserPasswordInputDto): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/reset-password`, data);
  }
  //#endif

  deleteUser(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  getUserRoles(id: string): Observable<RoleBriefDto[]> {
    return this.http.get<RoleBriefDto[]>(`${this.baseUrl}/${id}/roles`);
  }

  /** 角色分配独立于资料更新，需要 App.Users.ManageRoles。 */
  replaceUserRoles(id: string, data: UpdateUserRolesInputDto): Observable<RoleBriefDto[]> {
    return this.http.put<RoleBriefDto[]>(`${this.baseUrl}/${id}/roles`, data);
  }
}
