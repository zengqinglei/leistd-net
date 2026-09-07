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
import { ensureAcceptablePassword } from '../data/password-policy';
import { TENANTS } from '../data/tenant';
import { USERS, toUserOutput } from '../data/user';
import {
  MOCK_SESSION_USER_ID,
  setMockSessionTenantKey,
  setMockSessionUserId,
} from '../utils/current-user';

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
    throw new MockException(400, { code: 'Error:BadRequest', message: 'Username already exists' });
  }
}

function ensureEmailAvailable(email: string, currentUserId: string): void {
  const exists = USERS.some((user) => user.email === email && user.id !== currentUserId);
  if (exists) {
    throw new MockException(400, { code: 'Error:BadRequest', message: 'Email is already in use' });
  }
}

/** 复刻后端行为：X-Tenant-Id 指向已停用租户时登录被 403 拒绝。 */
function ensureTenantActive(req: MockRequest): void {
  const tenantId = req.headers.get('X-Tenant-Id');
  if (!tenantId) {
    return;
  }
  const tenant = TENANTS.find((t) => t.id === tenantId);
  if (tenant && !tenant.isActive) {
    throw new MockException(403, { code: 'Error:Forbidden', message: 'Tenant is deactivated' });
  }
}

function sessionLogin(usernameOrEmail: string, password: string, tenantKey: string): 'ok' {
  const user = USERS.find((u) => u.username === usernameOrEmail || u.email === usernameOrEmail);

  if (user && user.password === password) {
    setMockSessionUserId(user.id);
    // 租户在登录这一刻定案，之后由会话（真实环境是 cookie 里的租户声明）说话；
    // 认证后的接口不再看 X-Tenant-Id，与后端的解析链一致。
    setMockSessionTenantKey(tenantKey);
    return 'ok';
  }

  throw new MockException(401, {
    code: 'Error:Unauthorized',
    message: 'Incorrect username or password',
  });
}

function getCurrentUser(_req: MockRequest): UserOutputDto {
  if (!MOCK_SESSION_USER_ID) {
    throw new MockException(401, { code: 'Error:Unauthorized', message: 'Not authenticated' });
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
    throw new MockException(400, { code: 'Error:BadRequest', message: 'Username is required' });
  }

  if (!email) {
    throw new MockException(400, { code: 'Error:BadRequest', message: 'Email is required' });
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
    throw new MockException(400, {
      code: 'Error:BadRequest',
      message: 'Current password is incorrect',
    });
  }

  if (body.newPassword !== body.confirmPassword) {
    throw new MockException(400, {
      code: 'Error:BadRequest',
      message: 'The new passwords do not match',
    });
  }

  if (body.currentPassword === body.newPassword) {
    throw new MockException(400, {
      code: 'Error:BadRequest',
      message: 'The new password must be different from the current password',
    });
  }

  ensureAcceptablePassword(body.newPassword, 'New password');

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
      code: 'Error:BadRequest',
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
      code: 'Error:BadRequest',
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
    code: 'Error:BadRequest',
    message: 'The email verification code is incorrect or has expired',
  });
}

function getRequestScope(req: MockRequest): string {
  return req.headers.get('X-Tenant-Id') ?? 'host';
}

function normalizeEmail(email: string): string {
  return email.trim().toLowerCase();
}
//#if (ExternalLogin)

function getExternalLoginUrl(provider: string): { loginUrl: string } {
  const state = Math.random().toString(36).substring(7);
  const redirectUri = encodeURIComponent(`${window.location.origin}/#/auth/external-callback`);

  const urls: Record<string, string> = {
    github: `https://github.com/login/oauth/authorize?client_id=mock_client_id&redirect_uri=${redirectUri}&state=${state}&scope=user:email`,
    google: `https://accounts.google.com/o/oauth2/v2/auth?client_id=mock_client_id&redirect_uri=${redirectUri}&state=${state}&response_type=code&scope=email%20profile`,
  };

  const loginUrl = urls[provider];
  if (!loginUrl) {
    throw new MockException(400, {
      code: 'Error:BadRequest',
      message: `Unsupported login provider: ${provider}`,
    });
  }

  // state 只编在 loginUrl 里，与真实后端一致：绑定靠 HttpOnly Cookie，不回传给客户端
  return { loginUrl };
}

function externalLoginCallback(req: MockRequest): 'ok' {
  // Mock: 直接登录为第一个测试用户。
  // 回调仍是匿名请求，会带上登录前选定的租户头（tenantInterceptor 给所有 /api/ 请求附加），
  // 真实后端在这一步进入该租户上下文并把租户写进认证主体——所以这里也要按头定案，
  // 固定成宿主会让后续所有设置读写落到错误的作用域。
  setMockSessionUserId(USERS[0].id);
  setMockSessionTenantKey(getRequestScope(req));
  return 'ok';
}
//#endif

export const AUTH_API = {
  'POST /api/v1/auth/register': (req: MockRequest) => register(req),
  'GET /api/v1/auth/security-config': () => getSecurityConfig(),
  'GET /api/v1/auth/captcha': () => getCaptcha(),
  'POST /api/v1/auth/send-email-code': (req: MockRequest) => sendEmailCode(req),
  'POST /api/v1/auth/logout': () => logout(),
  'POST /api/v1/auth/session-login': (req: MockRequest) => {
    ensureTenantActive(req);
    // 登录是匿名阶段，此时 X-Tenant-Id 决定「凭据在哪个租户内校验」——这是它唯一起作用的地方。
    return sessionLogin(
      req.body.usernameOrEmail,
      req.body.password,
      req.headers.get('X-Tenant-Id') ?? 'host',
    );
  },
  'GET /api/v1/auth/me': (req: MockRequest) => getCurrentUser(req),
  'PUT /api/v1/auth/me': (req: MockRequest) => updateCurrentUser(req),
  'POST /api/v1/auth/change-password': (req: MockRequest) => changePassword(req),
  //#if (ExternalLogin)
  'GET /api/v1/external-auth/:provider/login-url': (req: MockRequest) =>
    getExternalLoginUrl(req.params.provider),
  'POST /api/v1/external-auth/:provider/callback': (req: MockRequest) => externalLoginCallback(req),
  //#endif
};
