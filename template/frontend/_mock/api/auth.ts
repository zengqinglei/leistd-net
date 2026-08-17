import {
  ChangePasswordInputDto,
  UpdateCurrentUserInputDto,
  UserOutputDto,
  RegisterInputDto,
  SecurityConfigOutputDto,
  CaptchaOutputDto,
  SendEmailCodeInputDto,
  EmailVerificationChallengeOutputDto,
} from '../../src/app/features/account/models/account.dto';
import { MockException, MockRequest } from '../core/models';
//#if (TenancyEnabled)
import { TENANTS } from '../data/tenant';
//#endif
import { USERS, toUserOutput } from '../data/user';
import { MOCK_SESSION_USER_ID, setMockSessionUserId } from '../utils/current-user';

const CAPTCHA_LETTERS = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
const CAPTCHA_DIGITS = '23456789';
const CAPTCHA_CHARACTERS = CAPTCHA_LETTERS + CAPTCHA_DIGITS;
const captchaStore = new Map<string, string>();
const EMAIL_VERIFICATION_ENABLED = false;
const EMAIL_CODE = '123456';
const EMAIL_CODE_EXPIRY_SECONDS = 300;
const EMAIL_CODE_RETRY_SECONDS = 60;
const EMAIL_CODE_MAX_ATTEMPTS = 5;

interface EmailVerificationChallenge {
  scope: string;
  purpose: 'registration-email';
  email: string;
  code: string;
  expiresAt: number;
  remainingAttempts: number;
}

const emailChallengeStore = new Map<string, EmailVerificationChallenge>();
const emailRateLimitStore = new Map<string, number>();

function ensureUsernameAvailable(username: string, currentUserId: string): void {
  const exists = USERS.some((user) => user.username === username && user.id !== currentUserId);
  if (exists) {
    throw new MockException(400, { code: 40011, message: 'Username already exists' });
  }
}

function ensureEmailAvailable(email: string, currentUserId: string): void {
  const exists = USERS.some((user) => user.email === email && user.id !== currentUserId);
  if (exists) {
    throw new MockException(400, { code: 40012, message: 'Email is already in use' });
  }
}

//#if (TenancyEnabled)
/** 复刻后端行为：X-Tenant-Id 指向已停用租户时登录被 403 拒绝。 */
function ensureTenantActive(req: MockRequest): void {
  const tenantId = req.headers.get('X-Tenant-Id');
  if (!tenantId) {
    return;
  }
  const tenant = TENANTS.find((t) => t.id === tenantId);
  if (tenant && !tenant.isActive) {
    throw new MockException(403, { code: 40300, message: 'Tenant is deactivated' });
  }
}

//#endif
function sessionLogin(usernameOrEmail: string, password: string): 'ok' {
  const user = USERS.find((u) => u.username === usernameOrEmail || u.email === usernameOrEmail);

  if (user && user.password === password) {
    setMockSessionUserId(user.id);
    return 'ok';
  }

  throw new MockException(401, { code: 40100, message: 'Incorrect username or password' });
}

function getCurrentUser(_req: MockRequest): UserOutputDto {
  if (!MOCK_SESSION_USER_ID) {
    throw new MockException(401, { code: 40101, message: 'Not authenticated' });
  }
  const user = USERS.find((u) => u.id === MOCK_SESSION_USER_ID) ?? USERS[0];
  return toUserOutput(user);
}

function updateCurrentUser(req: MockRequest): UserOutputDto {
  const user = USERS.find((u) => u.id === MOCK_SESSION_USER_ID) ?? USERS[0];
  const body = req.body as UpdateCurrentUserInputDto;

  const username = body.username.trim();
  const email = body.email.trim();

  if (!username) {
    throw new MockException(400, { code: 40013, message: 'Username is required' });
  }

  if (!email) {
    throw new MockException(400, { code: 40014, message: 'Email is required' });
  }

  ensureUsernameAvailable(username, user.id);
  ensureEmailAvailable(email, user.id);

  user.username = username;
  user.email = email;
  user.displayName = body.displayName?.trim() || undefined;
  user.phoneNumber = body.phoneNumber?.trim() || undefined;
  user.avatar = body.avatar?.trim() || undefined;

  return toUserOutput(user);
}

function changePassword(req: MockRequest): 'ok' {
  const user = USERS.find((u) => u.id === MOCK_SESSION_USER_ID) ?? USERS[0];
  const body = req.body as ChangePasswordInputDto;

  if (user.password !== body.currentPassword) {
    throw new MockException(400, { code: 40001, message: 'Current password is incorrect' });
  }

  if (body.newPassword !== body.confirmPassword) {
    throw new MockException(400, { code: 40002, message: 'The new passwords do not match' });
  }

  if (body.currentPassword === body.newPassword) {
    throw new MockException(400, {
      code: 40003,
      message: 'The new password must be different from the current password',
    });
  }

  user.password = body.newPassword;
  return 'ok';
}

function logout(): 'ok' {
  setMockSessionUserId(null);
  return 'ok';
}

function getSecurityConfig(): SecurityConfigOutputDto {
  return { enableEmailVerification: EMAIL_VERIFICATION_ENABLED };
}

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
      code: 40015,
      message: 'The captcha is incorrect, please try again',
    });
  }
}

function sendEmailCode(req: MockRequest): EmailVerificationChallengeOutputDto {
  const body = req.body as SendEmailCodeInputDto;
  validateCaptcha(body.captchaToken, body.captchaCode);

  const email = normalizeEmail(body.email);
  ensureEmailAvailable(email, '');
  const scope = getRequestScope(req);
  const rateKey = `${scope}:${email}`;
  const now = Date.now();
  if ((emailRateLimitStore.get(rateKey) ?? 0) > now) {
    throw new MockException(400, {
      code: 40016,
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

function register(req: MockRequest): 'ok' {
  const body = req.body as RegisterInputDto;

  const username = body.username.trim();
  const email = normalizeEmail(body.email);

  ensureUsernameAvailable(username, '');
  ensureEmailAvailable(email, '');

  if (EMAIL_VERIFICATION_ENABLED) {
    validateEmailChallenge(req, email, body);
  } else {
    validateCaptcha(body.captchaToken, body.captchaCode);
  }

  // 模拟写入用户
  const newUser = {
    id: `user_${Date.now()}`,
    username: username,
    email: email,
    password: body.password,
    //#if (IncludeRoles)
    roles: ['User'],
    //#endif
    isActive: true,
    isSuperAdmin: false,
    isEmailVerified: false,
    creationTime: new Date().toISOString(),
  };
  USERS.push(newUser as any);

  // 注册完可按需设置登录态，这里选择不自动登录
  return 'ok';
}

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

function invalidEmailChallenge(): MockException {
  return new MockException(400, {
    code: 40017,
    message: 'The email verification code is incorrect or has expired',
  });
}

//#if (TenancyEnabled)
function getRequestScope(req: MockRequest): string {
  return req.headers.get('X-Tenant-Id') ?? 'host';
}
//#else
function getRequestScope(_req: MockRequest): string {
  return 'host';
}
//#endif

function normalizeEmail(email: string): string {
  return email.trim().toLowerCase();
}

function getExternalLoginUrl(provider: string): { loginUrl: string; state: string } {
  const state = Math.random().toString(36).substring(7);
  const redirectUri = encodeURIComponent(`${window.location.origin}/#/auth/external-callback`);

  const urls: Record<string, string> = {
    github: `https://github.com/login/oauth/authorize?client_id=mock_client_id&redirect_uri=${redirectUri}&state=${state}&scope=user:email`,
    google: `https://accounts.google.com/o/oauth2/v2/auth?client_id=mock_client_id&redirect_uri=${redirectUri}&state=${state}&response_type=code&scope=email%20profile`,
  };

  const loginUrl = urls[provider];
  if (!loginUrl) {
    throw new MockException(400, {
      code: 40020,
      message: `Unsupported login provider: ${provider}`,
    });
  }

  return { loginUrl, state };
}

function externalLoginCallback(): 'ok' {
  // Mock: 直接登录为第一个测试用户
  setMockSessionUserId(USERS[0].id);
  return 'ok';
}

export const AUTH_API = {
  'POST /api/v1/auth/register': (req: MockRequest) => register(req),
  'GET /api/v1/auth/security-config': () => getSecurityConfig(),
  'GET /api/v1/auth/captcha': () => getCaptcha(),
  'POST /api/v1/auth/send-email-code': (req: MockRequest) => sendEmailCode(req),
  'POST /api/v1/auth/logout': () => logout(),
  'POST /api/v1/auth/session-login': (req: MockRequest) => {
    //#if (TenancyEnabled)
    ensureTenantActive(req);
    //#endif
    return sessionLogin(req.body.usernameOrEmail, req.body.password);
  },
  'GET /api/v1/auth/me': (req: MockRequest) => getCurrentUser(req),
  'PUT /api/v1/auth/me': (req: MockRequest) => updateCurrentUser(req),
  'POST /api/v1/auth/change-password': (req: MockRequest) => changePassword(req),
  'GET /api/v1/external-auth/:provider/login-url': (req: MockRequest) =>
    getExternalLoginUrl(req.params.provider),
  'POST /api/v1/external-auth/:provider/callback': () => externalLoginCallback(),
};
