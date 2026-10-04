//#if (IncludeMultiTenancy)
import { TENANT_HEADER } from '../../src/app/core/services/tenant-protocol';
//#endif
import {
  ChangePasswordInputDto,
  UpdateCurrentUserInputDto,
  RegisterInputDto,
  //#if (Email)
  SecurityConfigOutputDto,
  //#endif
  CaptchaOutputDto,
  //#if (Email)
  SendEmailCodeInputDto,
  //#endif
  //#if (Email)
  EmailVerificationChallengeOutputDto,
  //#endif
  //#if (Email)
  EmailVerificationInputDto,
  //#endif
  SetAvatarInputDto,
  UserSessionOutputDto,
  TwoFactorRecoveryCodesOutputDto,
  TwoFactorSetupOutputDto,
  TwoFactorStatusOutputDto,
  //#if (OpenIddictServer)
  LogoutConfirmationOutputDto,
  //#endif
} from '../../src/app/features/account/models/account.dto';
//#if (ExternalLogin)
import { SessionLoginOutputDto, UserOutputDto } from '../../src/app/shared/dtos/auth.dto';
//#else
import { UserOutputDto } from '../../src/app/shared/dtos/auth.dto';
//#endif
import { MockException, MockRequest } from '../core/models';
import { ensureAcceptablePassword } from '../data/password-policy';
//#if (IncludeMultiTenancy)
import { TENANTS } from '../data/tenant';
//#endif
import { MockUser, USERS, toUserOutput } from '../data/user';
import {
  MOCK_SESSION_USER_ID,
  getMockSessionTenantKey,
  setMockSessionTenantKey,
  setMockSessionUserId,
} from '../utils/current-user';

const CAPTCHA_LETTERS = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
const CAPTCHA_DIGITS = '23456789';
const CAPTCHA_CHARACTERS = CAPTCHA_LETTERS + CAPTCHA_DIGITS;
const captchaStore = new Map<string, string>();
//#if (Email)
const EMAIL_VERIFICATION_ENABLED = false;
const EMAIL_CODE = '123456';
const EMAIL_CODE_EXPIRY_SECONDS = 300;
const EMAIL_CODE_RETRY_SECONDS = 60;
const EMAIL_CODE_MAX_ATTEMPTS = 5;

interface EmailVerificationChallenge {
  scope: string;
  purpose: 'registration-email' | 'account-email';
  email: string;
  code: string;
  expiresAt: number;
  remainingAttempts: number;
}

const emailChallengeStore = new Map<string, EmailVerificationChallenge>();
const emailRateLimitStore = new Map<string, number>();

//#endif
function ensureUsernameAvailable(username: string, currentUserId: string): void {
  const exists = USERS.some((user) => user.username === username && user.id !== currentUserId);
  if (exists) {
    throw new MockException(409, {
      code: 'User:UsernameTaken',
      message: 'Username already exists',
    });
  }
}

function ensureEmailAvailable(email: string, currentUserId: string): void {
  const exists = USERS.some((user) => user.email === email && user.id !== currentUserId);
  if (exists) {
    throw new MockException(409, { code: 'User:EmailTaken', message: 'Email is already in use' });
  }
}
//#if (IncludeMultiTenancy)

/** 复刻后端行为：租户提示头指向已停用租户时登录被 403 拒绝。 */
function ensureTenantActive(req: MockRequest): void {
  const tenantId = req.headers.get(TENANT_HEADER);
  if (!tenantId) {
    return;
  }
  const tenant = TENANTS.find((t) => t.id === tenantId);
  if (tenant && !tenant.isActive) {
    throw new MockException(403, { code: 'Tenant:NotActive', message: 'Tenant is deactivated' });
  }
}
//#endif

function sessionLogin(usernameOrEmail: string, password: string, tenantKey: string): 'ok' {
  const user = USERS.find((u) => u.username === usernameOrEmail || u.email === usernameOrEmail);

  if (user && user.password === password) {
    setMockSessionUserId(user.id);
    // 租户在登录这一刻定案，之后由会话（真实环境是 cookie 里的租户声明）说话；
    // 认证后的接口不再看租户提示头，与后端的解析链一致。
    setMockSessionTenantKey(tenantKey);
    return 'ok';
  }

  throw new MockException(401, {
    code: 'Auth:InvalidCredentials',
    message: 'Incorrect username or password',
  });
}

/**
 * 当前认证主体——受保护端点的唯一入口。
 *
 * 没有会话、或会话里的 ID 匹配不到 Mock 用户，一律 401。**不能回落到 USERS[0]**：
 * 那会让匿名的资料修改与改密码"成功"，改掉的还是默认用户，于是 Mock 证明了一个
 * 生产环境不存在的行为——真后端在这两个端点上都是 401。
 * 展示用的 persona 回落只属于 Resource 形态的权限演示路径，不能进数据修改路径。
 */
function requireCurrentMockUser(): MockUser {
  const user = MOCK_SESSION_USER_ID ? USERS.find((u) => u.id === MOCK_SESSION_USER_ID) : undefined;
  if (!user) {
    throw new MockException(401, { message: 'Not authenticated' });
  }
  getMockSessionTenantKey();
  return user;
}

function getCurrentUser(_req: MockRequest): UserOutputDto {
  return toUserOutput(requireCurrentMockUser());
}

function updateCurrentUser(req: MockRequest): UserOutputDto {
  // 先确立主体再读 body：匿名写入不得在失败前碰到任何用户数据
  const user = requireCurrentMockUser();
  const body = req.body as UpdateCurrentUserInputDto;

  const username = body.username.trim();
  const email = body.email.trim();

  if (!username) {
    throw new MockException(400, { message: 'Username is required' });
  }

  if (!email) {
    throw new MockException(400, { message: 'Email is required' });
  }

  ensureUsernameAvailable(username, user.id);
  ensureEmailAvailable(email, user.id);

  // 换了邮箱即回到未验证，与后端 User.UpdateProfile 同一规则
  if (normalizeEmail(user.email) !== normalizeEmail(email)) {
    user.isEmailVerified = false;
  }

  user.username = username;
  user.email = email;
  user.displayName = body.displayName?.trim() || undefined;
  user.phoneNumber = body.phoneNumber?.trim() || undefined;

  return toUserOutput(user);
}

function setCurrentUserAvatar(req: MockRequest): UserOutputDto {
  const user = requireCurrentMockUser();
  const body = req.body as SetAvatarInputDto;
  user.avatar = body.avatar?.trim() || undefined;
  return toUserOutput(user);
}
//#if (Email)

function sendCurrentUserEmailCode(req: MockRequest): EmailVerificationChallengeOutputDto {
  const user = requireCurrentMockUser();
  if (user.isEmailVerified) {
    throw new MockException(409, {
      code: 'Auth:EmailAlreadyVerified',
      message: 'This email address has already been verified.',
    });
  }

  const challengeId = crypto.randomUUID();
  emailChallengeStore.set(challengeId, {
    scope: getRequestScope(req),
    purpose: 'account-email',
    email: normalizeEmail(user.email),
    code: EMAIL_CODE,
    expiresAt: Date.now() + EMAIL_CODE_EXPIRY_SECONDS * 1000,
    remainingAttempts: EMAIL_CODE_MAX_ATTEMPTS,
  });

  return {
    challengeId,
    expiresInSeconds: EMAIL_CODE_EXPIRY_SECONDS,
    retryAfterSeconds: EMAIL_CODE_RETRY_SECONDS,
  };
}
//#endif
//#if (Email)

function confirmCurrentUserEmail(req: MockRequest): UserOutputDto {
  const user = requireCurrentMockUser();
  const body = req.body as EmailVerificationInputDto;
  const challenge = emailChallengeStore.get(body.challengeId);
  if (
    !challenge ||
    challenge.purpose !== 'account-email' ||
    challenge.email !== normalizeEmail(user.email) ||
    challenge.code !== body.code.trim()
  ) {
    throw new MockException(400, {
      code: 'Auth:EmailCodeInvalid',
      message: 'The email verification code is incorrect or has expired.',
    });
  }

  emailChallengeStore.delete(body.challengeId);
  user.isEmailVerified = true;
  return toUserOutput(user);
}
//#endif

function changePassword(req: MockRequest): 'ok' {
  const user = requireCurrentMockUser();
  const body = req.body as ChangePasswordInputDto;

  if (user.password !== body.currentPassword) {
    throw new MockException(400, {
      code: 'Security:CurrentPasswordIncorrect',
      message: 'Current password is incorrect',
    });
  }

  if (body.newPassword !== body.confirmPassword) {
    throw new MockException(400, {
      message: 'The new passwords do not match',
    });
  }

  if (body.currentPassword === body.newPassword) {
    throw new MockException(400, {
      message: 'The new password must be different from the current password',
    });
  }

  ensureAcceptablePassword(body.newPassword, 'New password');

  user.password = body.newPassword;
  // 与服务端一致：改密码后其他设备退出登录
  mockSessions = mockSessions.filter((s) => s.isCurrent);
  return 'ok';
}

/**
 * 当前会话之外预置两台"别的设备"，让登录设备一节在 mock 下有内容可看、可撤销。
 * 模块级状态：撤销后在本次页面生命周期内保持，刷新即复原。
 */
const MOCK_SESSION_NOW = Date.now();
let mockSessions: UserSessionOutputDto[] = [
  {
    id: 'mock-session-current',
    creationTime: new Date(MOCK_SESSION_NOW - 2 * 3600_000).toISOString(),
    lastSeenTime: new Date(MOCK_SESSION_NOW).toISOString(),
    ipAddress: '127.0.0.1',
    userAgent: typeof navigator === 'undefined' ? null : navigator.userAgent,
    isCurrent: true,
  },
  {
    id: 'mock-session-phone',
    creationTime: new Date(MOCK_SESSION_NOW - 3 * 86400_000).toISOString(),
    lastSeenTime: new Date(MOCK_SESSION_NOW - 5 * 3600_000).toISOString(),
    ipAddress: '203.0.113.24',
    userAgent:
      'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1',
    isCurrent: false,
  },
  {
    id: 'mock-session-laptop',
    creationTime: new Date(MOCK_SESSION_NOW - 6 * 86400_000).toISOString(),
    lastSeenTime: new Date(MOCK_SESSION_NOW - 26 * 3600_000).toISOString(),
    ipAddress: '198.51.100.7',
    userAgent:
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 Edg/128.0.2739.42',
    isCurrent: false,
  },
];

/**
 * 两步验证：mock 下验证码固定为 {@link MOCK_TWO_FACTOR_CODE}，只演示设置与管理界面；
 * 登录不走第二步（mock 没有服务端会话可言）。
 */
const MOCK_TWO_FACTOR_CODE = '123456';
let mockTwoFactor = { enabled: false, recoveryCodesLeft: 0 };

function mockRecoveryCodes(): TwoFactorRecoveryCodesOutputDto {
  const codes = Array.from(
    { length: 10 },
    (_, i) => `mock-${String(i).padStart(4, '0')}-code-demo`,
  );
  mockTwoFactor = { enabled: true, recoveryCodesLeft: codes.length };
  return { recoveryCodes: codes };
}

function ensureMockTwoFactorCode(code: string | undefined): void {
  if (code !== MOCK_TWO_FACTOR_CODE) {
    throw new MockException(400, {
      code: 'Auth:TwoFactorCodeInvalid',
      message: `The verification code is incorrect (mock code: ${MOCK_TWO_FACTOR_CODE}).`,
    });
  }
}

function getTwoFactorStatus(): TwoFactorStatusOutputDto {
  requireCurrentMockUser();
  return { ...mockTwoFactor, requiredByPolicy: false };
}

function beginTwoFactorSetup(): TwoFactorSetupOutputDto {
  const user = requireCurrentMockUser();
  const secret = 'JBSWY3DPEHPK3PXP';
  return {
    secret,
    otpAuthUri: `otpauth://totp/Mock:${encodeURIComponent(user.username)}?secret=${secret}&issuer=Mock`,
  };
}

function enableTwoFactor(req: MockRequest): TwoFactorRecoveryCodesOutputDto {
  requireCurrentMockUser();
  ensureMockTwoFactorCode(req.body?.code);
  mockSessions = mockSessions.filter((s) => s.isCurrent);
  return mockRecoveryCodes();
}

function disableTwoFactor(req: MockRequest): 'ok' {
  const user = requireCurrentMockUser();
  if (req.body?.password !== user.password) {
    throw new MockException(400, {
      code: 'Security:CurrentPasswordIncorrect',
      message: 'The current password is incorrect.',
    });
  }
  ensureMockTwoFactorCode(req.body?.code);
  mockTwoFactor = { enabled: false, recoveryCodesLeft: 0 };
  mockSessions = mockSessions.filter((s) => s.isCurrent);
  return 'ok';
}

function regenerateRecoveryCodes(req: MockRequest): TwoFactorRecoveryCodesOutputDto {
  requireCurrentMockUser();
  ensureMockTwoFactorCode(req.body?.code);
  return mockRecoveryCodes();
}

function getSessions(): UserSessionOutputDto[] {
  requireCurrentMockUser();
  return mockSessions;
}

function revokeSession(req: MockRequest): 'ok' {
  requireCurrentMockUser();
  const id = String(req.params.id);
  if (mockSessions.some((s) => s.id === id && s.isCurrent)) {
    throw new MockException(409, {
      code: 'Auth:CannotRevokeCurrentSession',
      message: 'Use sign-out to end the current session.',
    });
  }
  mockSessions = mockSessions.filter((s) => s.id !== id);
  return 'ok';
}

function revokeOtherSessions(): number {
  requireCurrentMockUser();
  const count = mockSessions.filter((s) => !s.isCurrent).length;
  mockSessions = mockSessions.filter((s) => s.isCurrent);
  return count;
}

function logout(): 'ok' {
  setMockSessionUserId(null);
  return 'ok';
}
//#if (OpenIddictServer)

function getLogoutConfirmation(req: MockRequest): LogoutConfirmationOutputDto {
  // Mock 没有协议端点：只要求确认页的两个引用都在，便于在开发态预览确认页
  return req.queryParams['request_uri'] && req.queryParams['confirmation']
    ? {
        isValid: true,
        applicationName: 'Mock application',
        antiforgeryFieldName: '__RequestVerificationToken',
        antiforgeryToken: 'mock-antiforgery-token',
      }
    : { isValid: false };
}
//#endif
//#if (Email)

function getSecurityConfig(): SecurityConfigOutputDto {
  return { enableEmailVerification: EMAIL_VERIFICATION_ENABLED, emailVerificationAvailable: true };
}
//#endif

function getCaptcha(): CaptchaOutputDto {
  const bgColors = ['#f0fdf4', '#f8fafc', '#fffbeb', '#fef2f2', '#f0f9ff'];
  const bg = bgColors[Math.floor(Math.random() * bgColors.length)];
  const lineY = Math.floor(Math.random() * 40);
  const angle = Math.floor(Math.random() * 20) - 10;
  const code = generateCaptchaCode(4);
  const token = Math.random().toString(36).substring(7);

  captchaStore.set(token, code);

  const svgContent = `<svg xmlns="http://www.w3.org/2000/svg" width="130" height="44"><rect width="100%" height="100%" fill="${bg}"/><line x1="0" y1="${lineY}" x2="130" y2="${44 - lineY}" stroke="#94a3b8" stroke-width="2" opacity="0.6"/><text x="50%" y="50%" font-size="24" font-family="monospace" fill="#0f172a" font-weight="bold" font-style="italic" textLength="88" lengthAdjust="spacingAndGlyphs" dominant-baseline="central" text-anchor="middle" transform="rotate(${angle}, 65, 22)">${code}</text></svg>`;

  const fakeImage = `data:image/svg+xml;base64,${btoa(svgContent)}`;

  return {
    captchaToken: token,
    captchaImageBase64: fakeImage,
  };
}

function generateCaptchaCode(length: number): string {
  const chars = [
    CAPTCHA_LETTERS[Math.floor(Math.random() * CAPTCHA_LETTERS.length)],
    CAPTCHA_DIGITS[Math.floor(Math.random() * CAPTCHA_DIGITS.length)],
    ...Array.from(
      { length: length - 2 },
      () => CAPTCHA_CHARACTERS[Math.floor(Math.random() * CAPTCHA_CHARACTERS.length)],
    ),
  ];

  for (let i = chars.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [chars[i], chars[j]] = [chars[j], chars[i]];
  }

  return chars.join('');
}

function validateCaptcha(captchaToken: string | undefined, captchaCode: string | undefined): void {
  const code = captchaToken ? captchaStore.get(captchaToken) : undefined;
  captchaStore.delete(captchaToken ?? '');

  if (!code || !captchaCode || code.toLowerCase() !== captchaCode.trim().toLowerCase()) {
    throw new MockException(400, {
      code: 'Auth:CaptchaInvalid',
      message: 'The captcha is incorrect, please try again',
    });
  }
}
//#if (Email)

function sendEmailCode(req: MockRequest): EmailVerificationChallengeOutputDto {
  const body = req.body as SendEmailCodeInputDto;
  validateCaptcha(body.captchaToken, body.captchaCode);

  const email = normalizeEmail(body.email);
  ensureEmailAvailable(email, '');
  const scope = getRequestScope(req);
  const rateKey = `${scope}:${email}`;
  const now = Date.now();
  if ((emailRateLimitStore.get(rateKey) ?? 0) > now) {
    throw new MockException(429, {
      code: 'Auth:EmailCodeSendTooFrequent',
      message: 'Verification codes are being sent too frequently',
    });
  }

  const challengeId = crypto.randomUUID();
  emailRateLimitStore.set(rateKey, now + EMAIL_CODE_RETRY_SECONDS * 1000);
  emailChallengeStore.set(challengeId, {
    scope,
    purpose: 'registration-email',
    email,
    code: EMAIL_CODE,
    expiresAt: now + EMAIL_CODE_EXPIRY_SECONDS * 1000,
    remainingAttempts: EMAIL_CODE_MAX_ATTEMPTS,
  });

  return {
    challengeId,
    expiresInSeconds: EMAIL_CODE_EXPIRY_SECONDS,
    retryAfterSeconds: EMAIL_CODE_RETRY_SECONDS,
  };
}
//#endif

function register(req: MockRequest): 'ok' {
  const body = req.body as RegisterInputDto;

  const username = body.username.trim();
  const email = normalizeEmail(body.email);

  ensureUsernameAvailable(username, '');
  ensureEmailAvailable(email, '');

  //#if (Email)
  if (EMAIL_VERIFICATION_ENABLED) {
    validateEmailChallenge(req, email, body);
  } else {
    validateCaptcha(body.captchaToken, body.captchaCode);
  }
  //#else
  validateCaptcha(body.captchaToken, body.captchaCode);
  //#endif

  ensureAcceptablePassword(body.password, 'Password');

  // 模拟写入用户
  const newUser = {
    id: `user_${Date.now()}`,
    username: username,
    email: email,
    password: body.password,
    roles: ['User'],
    isActive: true,
    isSuperAdmin: false,
    isEmailVerified: false,
    creationTime: new Date().toISOString(),
  };
  USERS.push(newUser as any);

  // 注册完可按需设置登录态，这里选择不自动登录
  return 'ok';
}
//#if (Email)

function validateEmailChallenge(req: MockRequest, email: string, body: RegisterInputDto): void {
  const verification = body.emailVerification;
  const challenge = verification ? emailChallengeStore.get(verification.challengeId) : undefined;

  if (
    !verification ||
    !challenge ||
    challenge.scope !== getRequestScope(req) ||
    challenge.purpose !== 'registration-email' ||
    challenge.email !== email
  ) {
    throw invalidEmailChallenge();
  }

  if (challenge.expiresAt <= Date.now()) {
    emailChallengeStore.delete(verification.challengeId);
    throw invalidEmailChallenge();
  }

  if (challenge.code !== verification.code?.trim()) {
    challenge.remainingAttempts--;
    if (challenge.remainingAttempts <= 0) {
      emailChallengeStore.delete(verification.challengeId);
    }
    throw invalidEmailChallenge();
  }

  emailChallengeStore.delete(verification.challengeId);
}
//#endif
//#if (Email)

function invalidEmailChallenge(): MockException {
  return new MockException(400, {
    code: 'Auth:EmailCodeInvalid',
    message: 'The email verification code is incorrect or has expired',
  });
}
//#endif
function getRequestScope(_req: MockRequest): string {
  //#if (IncludeMultiTenancy)
  return _req.headers.get(TENANT_HEADER) ?? 'host';
  //#else
  return 'host';
  //#endif
}

function normalizeEmail(email: string): string {
  return email.trim().toLowerCase();
}
//#if (ExternalLogin)

/** mock 部署"已配置"的提供商：登录页入口与绑定列表读同一份。 */
const MOCK_EXTERNAL_PROVIDERS = ['github', 'google'];

/** mock 下的外部账号绑定：只演示列表与解绑；绑定要跳真实的提供商，mock 走不通。 */
let mockExternalLinks: {
  id: string;
  provider: string;
  providerAccountLabel: string;
  creationTime: string;
}[] = [
  {
    id: 'mock-link-github',
    provider: 'github',
    providerAccountLabel: 'octocat',
    creationTime: '2026-06-01T00:00:00Z',
  },
];

function externalLoginCallback(req: MockRequest): SessionLoginOutputDto {
  setMockSessionUserId(USERS[0].id);
  setMockSessionTenantKey(getRequestScope(req));
  return {};
}

function getExternalLinks() {
  const user = requireCurrentMockUser();
  return {
    hasPassword: !!user.password,
    providers: MOCK_EXTERNAL_PROVIDERS.map((provider) => ({
      provider,
      link: mockExternalLinks.find((l) => l.provider === provider) ?? null,
    })),
  };
}

function unlinkExternalLogin(req: MockRequest): 'ok' {
  requireCurrentMockUser();
  mockExternalLinks = mockExternalLinks.filter((l) => l.id !== String(req.params.id));
  return 'ok';
}
//#endif

export const AUTH_API = {
  'POST /api/v1/auth/register': (req: MockRequest) => register(req),
  //#if (Email)
  'GET /api/v1/auth/security-config': () => getSecurityConfig(),
  //#endif
  //#if (OpenIddictServer)
  'GET /api/v1/auth/logout-confirmation': (req: MockRequest) => getLogoutConfirmation(req),
  //#endif
  'GET /api/v1/auth/captcha': () => getCaptcha(),
  //#if (Email)
  'POST /api/v1/auth/send-email-code': (req: MockRequest) => sendEmailCode(req),
  //#endif
  'POST /api/v1/auth/logout': () => logout(),
  'POST /api/v1/auth/session-login': (req: MockRequest) => {
    //#if (IncludeMultiTenancy)
    ensureTenantActive(req);
    //#endif
    // 登录是匿名阶段，此时租户提示头决定「凭据在哪个租户内校验」——这是它唯一起作用的地方。
    return sessionLogin(req.body.usernameOrEmail, req.body.password, getRequestScope(req));
  },
  'GET /api/v1/auth/me': (req: MockRequest) => getCurrentUser(req),
  'PUT /api/v1/auth/me': (req: MockRequest) => updateCurrentUser(req),
  'PUT /api/v1/auth/me/avatar': (req: MockRequest) => setCurrentUserAvatar(req),
  //#if (Email)
  'POST /api/v1/auth/me/email-verification': (req: MockRequest) => sendCurrentUserEmailCode(req),
  //#endif
  //#if (Email)
  'POST /api/v1/auth/me/email-verification/confirm': (req: MockRequest) =>
    confirmCurrentUserEmail(req),
  //#endif
  'GET /api/v1/auth/me/two-factor': () => getTwoFactorStatus(),
  'POST /api/v1/auth/me/two-factor/setup': () => beginTwoFactorSetup(),
  'POST /api/v1/auth/me/two-factor/enable': (req: MockRequest) => enableTwoFactor(req),
  'POST /api/v1/auth/me/two-factor/disable': (req: MockRequest) => disableTwoFactor(req),
  'POST /api/v1/auth/me/two-factor/recovery-codes': (req: MockRequest) =>
    regenerateRecoveryCodes(req),
  'GET /api/v1/auth/me/sessions': () => getSessions(),
  'DELETE /api/v1/auth/me/sessions/:id': (req: MockRequest) => revokeSession(req),
  'POST /api/v1/auth/me/sessions/revoke-others': () => revokeOtherSessions(),
  'POST /api/v1/auth/change-password': (req: MockRequest) => changePassword(req),
  //#if (ExternalLogin)
  'POST /api/v1/external-auth/:provider/complete': (req: MockRequest) => externalLoginCallback(req),
  'GET /api/v1/external-auth/providers': () => ({ providers: MOCK_EXTERNAL_PROVIDERS }),
  'GET /api/v1/external-auth/links': () => getExternalLinks(),
  'DELETE /api/v1/external-auth/links/:id': (req: MockRequest) => unlinkExternalLogin(req),
  //#endif
};
