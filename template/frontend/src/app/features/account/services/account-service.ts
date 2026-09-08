import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, tap } from 'rxjs';

import { AuthService } from '../../../core/services/auth-service';
import { UserOutputDto } from '../../../shared/dtos/auth.dto';
import {
  ChangePasswordInputDto,
  //#if (ExternalLogin)
  ExternalLoginCallbackInputDto,
  ExternalLoginUrlOutputDto,
  //#endif
  RegisterInputDto,
  UpdateCurrentUserInputDto,
  SecurityConfigOutputDto,
  CaptchaOutputDto,
  SendEmailCodeInputDto,
  EmailVerificationChallengeOutputDto,
} from '../models/account.dto';

@Injectable({ providedIn: 'root' })
export class AccountService {
  private http = inject(HttpClient);
  private authService = inject(AuthService);

  getSecurityConfig(): Observable<SecurityConfigOutputDto> {
    return this.http.get<SecurityConfigOutputDto>('/api/v1/auth/security-config');
  }

  getCaptcha(): Observable<CaptchaOutputDto> {
    return this.http.get<CaptchaOutputDto>('/api/v1/auth/captcha');
  }

  sendEmailCode(data: SendEmailCodeInputDto): Observable<EmailVerificationChallengeOutputDto> {
    return this.http.post<EmailVerificationChallengeOutputDto>(
      '/api/v1/auth/send-email-code',
      data,
    );
  }

  register(data: RegisterInputDto): Observable<void> {
    return this.http.post('/api/v1/auth/register', data).pipe(map(() => undefined));
  }

  updateCurrentUser(data: UpdateCurrentUserInputDto): Observable<UserOutputDto> {
    return this.http
      .put<UserOutputDto>('/api/v1/auth/me', data)
      .pipe(tap((user) => this.authService.setCurrentUser(user)));
  }

  changePassword(data: ChangePasswordInputDto): Observable<void> {
    return this.http.post('/api/v1/auth/change-password', data).pipe(map(() => undefined));
  }
  //#if (ExternalLogin)

  getExternalLoginUrl(provider: 'github' | 'google'): Observable<ExternalLoginUrlOutputDto> {
    return this.http.get<ExternalLoginUrlOutputDto>(`/api/v1/external-auth/${provider}/login-url`);
  }

  externalLoginCallback(provider: string, data: ExternalLoginCallbackInputDto): Observable<void> {
    return this.http.post<void>(`/api/v1/external-auth/${provider}/callback`, data);
  }
  //#endif
}
