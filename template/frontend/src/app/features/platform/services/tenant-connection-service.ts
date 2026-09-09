import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { TenantConnectionOutputDto } from '../../../shared/dtos/tenant-connection.dto';

@Injectable({ providedIn: 'root' })
export class TenantConnectionService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/tenant-connections';

  getConnection(tenantId: string): Observable<TenantConnectionOutputDto> {
    return this.http.get<TenantConnectionOutputDto>(`${this.baseUrl}/${tenantId}`);
  }
}
