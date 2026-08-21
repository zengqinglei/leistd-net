//#if (MultiTenancy)
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  CreateTenantInputDto,
  GetTenantsInputDto,
  TenantBriefOutputDto,
  TenantOutputDto,
  UpdateTenantInputDto,
} from '../../../shared/dtos/tenant.dto';
import { PagedResultDto } from '../../../shared/models/paged-result.dto';

@Injectable({ providedIn: 'root' })
export class TenantService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/tenants';

  getTenants(input: GetTenantsInputDto): Observable<PagedResultDto<TenantOutputDto>> {
    let params = new HttpParams();
    if (input.offset !== undefined) params = params.set('offset', input.offset.toString());
    if (input.limit !== undefined) params = params.set('limit', input.limit.toString());
    if (input.keyword) params = params.set('keyword', input.keyword);
    return this.http.get<PagedResultDto<TenantOutputDto>>(this.baseUrl, { params });
  }

  getTenant(id: string): Observable<TenantOutputDto> {
    return this.http.get<TenantOutputDto>(`${this.baseUrl}/${id}`);
  }

  /** 匿名按名称解析租户（登录页租户选择用），404 表示不存在。 */
  getByName(name: string): Observable<TenantBriefOutputDto> {
    return this.http.get<TenantBriefOutputDto>(
      `${this.baseUrl}/by-name/${encodeURIComponent(name)}`,
    );
  }

  createTenant(data: CreateTenantInputDto): Observable<TenantOutputDto> {
    return this.http.post<TenantOutputDto>(this.baseUrl, data);
  }

  updateTenant(id: string, data: UpdateTenantInputDto): Observable<TenantOutputDto> {
    return this.http.put<TenantOutputDto>(`${this.baseUrl}/${id}`, data);
  }

  setActivation(id: string, isActive: boolean): Observable<TenantOutputDto> {
    return this.http.put<TenantOutputDto>(`${this.baseUrl}/${id}/activation`, { isActive });
  }

  deleteTenant(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
//#endif
