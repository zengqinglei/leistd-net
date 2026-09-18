import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, tap } from 'rxjs';

import { AuthService } from '../../../core/services/auth-service';
//#if (ExternalLogin)
import { SessionLoginOutputDto, UserOutputDto } from '../../../shared/dtos/auth.dto';
//#else
import { UserOutputDto } from '../../../shared/dtos/auth.dto';
//#endif
import {
  ChangePasswordInputDto,
  //#if (ExternalLogin)
  ExternalLoginCallbackInputDto,
  ExternalLoginsOutputDto,
  ExternalLoginUrlOutputDto,
  //#endif
  RegisterInputDto,
  SetAvatarInputDto,
  UpdateCurrentUserInputDto,
  EmailVerificationInputDto,
  SecurityConfigOutputDto,
  CaptchaOutputDto,
  SendEmailCodeInputDto,
  EmailVerificationChallengeOutputDto,
  UserSessionOutputDto,
  DisableTwoFactorInputDto,
  TwoFactorLoginInputDto,
  TwoFactorRecoveryCodesOutputDto,
  TwoFactorSetupOutputDto,
  TwoFactorStatusOutputDto,
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

  /** 设置或清除自己的头像；成功后用服务端返回的资料刷新当前用户（头像地址带新的版本号）。 */
  setAvatar(data: SetAvatarInputDto): Observable<UserOutputDto> {
    return this.http
      .put<UserOutputDto>('/api/v1/auth/me/avatar', data)
      .pipe(tap((user) => this.authService.setCurrentUser(user)));
  }

  /** 给自己当前的邮箱发验证码。 */
  sendCurrentEmailCode(): Observable<EmailVerificationChallengeOutputDto> {
    return this.http.post<EmailVerificationChallengeOutputDto>(
      '/api/v1/auth/me/email-verification',
      {},
    );
  }

  /** 用验证码确认自己当前的邮箱。 */
  confirmCurrentEmail(data: EmailVerificationInputDto): Observable<UserOutputDto> {
    return this.http
      .post<UserOutputDto>('/api/v1/auth/me/email-verification/confirm', data)
      .pipe(tap((user) => this.authService.setCurrentUser(user)));
  }

  changePassword(data: ChangePasswordInputDto): Observable<void> {
    return this.http.post('/api/v1/auth/change-password', data).pipe(map(() => undefined));
  }

  /** 登录第二步：提交验证码或恢复码，通过后下发会话。 */
  completeTwoFactorLogin(data: TwoFactorLoginInputDto): Observable<void> {
    return this.http.post('/api/v1/auth/two-factor', data).pipe(map(() => undefined));
  }

  getTwoFactorStatus(): Observable<TwoFactorStatusOutputDto> {
    return this.http.get<TwoFactorStatusOutputDto>('/api/v1/auth/me/two-factor');
  }

  /** 开始设置：生成待启用的密钥（几分钟内有效）。 */
  beginTwoFactorSetup(): Observable<TwoFactorSetupOutputDto> {
    return this.http.post<TwoFactorSetupOutputDto>('/api/v1/auth/me/two-factor/setup', {});
  }

  /** 用验证码确认并启用，返回一次性展示的恢复码。 */
  enableTwoFactor(code: string): Observable<TwoFactorRecoveryCodesOutputDto> {
    return this.http.post<TwoFactorRecoveryCodesOutputDto>('/api/v1/auth/me/two-factor/enable', {
      code,
    });
  }

  disableTwoFactor(data: DisableTwoFactorInputDto): Observable<void> {
    return this.http.post('/api/v1/auth/me/two-factor/disable', data).pipe(map(() => undefined));
  }

  regenerateRecoveryCodes(code: string): Observable<TwoFactorRecoveryCodesOutputDto> {
    return this.http.post<TwoFactorRecoveryCodesOutputDto>(
      '/api/v1/auth/me/two-factor/recovery-codes',
      { code },
    );
  }

  /** 自己仍然有效的登录会话，当前设备在前。 */
  getSessions(): Observable<UserSessionOutputDto[]> {
    return this.http.get<UserSessionOutputDto[]>('/api/v1/auth/me/sessions');
  }

  /** 让自己的某台设备退出登录。 */
  revokeSession(id: string): Observable<void> {
    return this.http.delete(`/api/v1/auth/me/sessions/${id}`).pipe(map(() => undefined));
  }

  /** 让除当前设备以外的全部设备退出登录，返回退出的个数。 */
  revokeOtherSessions(): Observable<number> {
    return this.http.post<number>('/api/v1/auth/me/sessions/revoke-others', {});
  }
  //#if (ExternalLogin)

  /** 本人的外部账号绑定情况。 */
  getExternalLogins(): Observable<ExternalLoginsOutputDto> {
    return this.http.get<ExternalLoginsOutputDto>('/api/v1/external-auth/links');
  }

  /** "绑定外部账号"的授权地址（与登录用的互不通用）。 */
  getExternalLinkUrl(provider: string): Observable<ExternalLoginUrlOutputDto> {
    return this.http.get<ExternalLoginUrlOutputDto>(`/api/v1/external-auth/${provider}/link-url`);
  }

  /** 外部授权回来后完成绑定。 */
  linkExternalLogin(provider: string, data: ExternalLoginCallbackInputDto): Observable<void> {
    return this.http
      .post(`/api/v1/external-auth/${provider}/link`, data)
      .pipe(map(() => undefined));
  }

  unlinkExternalLogin(id: string): Observable<void> {
    return this.http.delete(`/api/v1/external-auth/links/${id}`).pipe(map(() => undefined));
  }

  getExternalLoginUrl(provider: 'github' | 'google'): Observable<ExternalLoginUrlOutputDto> {
    return this.http.get<ExternalLoginUrlOutputDto>(`/api/v1/external-auth/${provider}/login-url`);
  }

  /** 外部登录回调；已启用两步验证时不下发会话，返回第二步凭据。 */
  externalLoginCallback(
    provider: string,
    data: ExternalLoginCallbackInputDto,
  ): Observable<SessionLoginOutputDto> {
    return this.http.post<SessionLoginOutputDto>(
      `/api/v1/external-auth/${provider}/callback`,
      data,
    );
  }
  //#endif
}
