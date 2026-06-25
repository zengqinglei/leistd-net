import { OpenApplicationOutputDto } from '../../src/app/features/platform/models/open-application.dto';

export interface MockOpenApplication extends OpenApplicationOutputDto {
  clientSecret?: string;
}

export const OPEN_APPLICATIONS: MockOpenApplication[] = [
  {
    id: 'myproject-web',
    clientId: 'myproject-web',
    displayName: 'MyProject Web',
    applicationType: 'web',
    clientType: 'public',
    consentType: 'explicit',
    redirectUris: ['http://localhost:4200/auth/callback'],
    postLogoutRedirectUris: ['http://localhost:4200/auth/logout-callback'],
    permissions: [
      'ept:authorization',
      'ept:end_session',
      'ept:token',
      'gt:authorization_code',
      'gt:refresh_token',
      'rst:code',
      'scp:openid',
      'scp:profile',
      'scp:email',
      'scp:roles',
      'scp:offline_access'
    ],
    requirements: ['ft:pkce'],
    settings: {},
    properties: {},
    hasClientSecret: false,
    creationTime: '2026-05-01T09:00:00Z'
  },
  {
    id: 'myproject-desktop',
    clientId: 'myproject-desktop',
    displayName: 'MyProject Desktop',
    applicationType: 'native',
    clientType: 'public',
    consentType: 'explicit',
    redirectUris: ['myproject-desktop://oauth/callback'],
    postLogoutRedirectUris: ['myproject-desktop://oauth/logout-callback'],
    permissions: [
      'ept:authorization',
      'ept:end_session',
      'ept:token',
      'gt:authorization_code',
      'gt:refresh_token',
      'rst:code',
      'scp:openid',
      'scp:profile',
      'scp:email',
      'scp:roles',
      'scp:offline_access'
    ],
    requirements: ['ft:pkce'],
    settings: {},
    properties: {},
    hasClientSecret: false,
    creationTime: '2026-05-02T10:30:00Z'
  },
  {
    id: 'myproject-service',
    clientId: 'myproject-service',
    displayName: 'MyProject Service Client',
    applicationType: 'service',
    clientType: 'confidential',
    consentType: 'systematic',
    redirectUris: [],
    postLogoutRedirectUris: [],
    permissions: ['ept:token', 'gt:client_credentials'],
    requirements: [],
    settings: {},
    properties: {},
    hasClientSecret: true,
    creationTime: '2026-05-03T14:15:00Z',
    clientSecret: 'mock-service-secret'
  }
];
