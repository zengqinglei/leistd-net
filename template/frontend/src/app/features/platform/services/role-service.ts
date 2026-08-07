import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PagedResultDto } from '../../../shared/models/paged-result.dto';
import {
  CreateRoleInputDto,
  GetRolesInputDto,
  RoleBriefDto,
  RoleOutputDto,
  UpdateRoleInputDto,
} from '../models/role.dto';

@Injectable({ providedIn: 'root' })
export class RoleService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/roles';

  getRoles(input: GetRolesInputDto): Observable<PagedResultDto<RoleOutputDto>> {
    let params = new HttpParams();
    if (input.offset !== undefined) params = params.set('offset', input.offset.toString());
    if (input.limit !== undefined) params = params.set('limit', input.limit.toString());
    if (input.keyword) params = params.set('keyword', input.keyword);
    if (input.sorting) params = params.set('sorting', input.sorting);
    return this.http.get<PagedResultDto<RoleOutputDto>>(this.baseUrl, { params });
  }

  /** 角色选项来自后端，前端不再保留任何硬编码角色列表。 */
  getOptions(): Observable<RoleBriefDto[]> {
    return this.http.get<RoleBriefDto[]>(`${this.baseUrl}/options`);
  }

  createRole(data: CreateRoleInputDto): Observable<RoleOutputDto> {
    return this.http.post<RoleOutputDto>(this.baseUrl, data);
  }

  updateRole(id: string, data: UpdateRoleInputDto): Observable<RoleOutputDto> {
    return this.http.put<RoleOutputDto>(`${this.baseUrl}/${id}`, data);
  }

  deleteRole(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
