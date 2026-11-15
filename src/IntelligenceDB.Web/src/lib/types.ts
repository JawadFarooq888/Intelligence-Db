// Mirrors the API DTOs (enums are serialised as strings).

export type PlatformType =
  | "Unknown"
  | "OnPremises"
  | "AzureSqlManagedInstance"
  | "AzureSqlDatabase"
  | "AwsRds"
  | "GoogleCloudSql";

export type AuthType =
  | "SqlLogin"
  | "Windows"
  | "EntraPassword"
  | "EntraServicePrincipal"
  | "EntraManagedIdentity"
  | "EntraDefault";

export type ServerEnvironment = "Production" | "Test" | "Development" | "Other";
export type ConnectionStatus = "Unknown" | "Online" | "Unreachable" | "AuthFailed";
export type Role = "Admin" | "DBA" | "Operator" | "Viewer";

export interface UserDto {
  id: string;
  email: string;
  displayName: string;
  roles: Role[];
  twoFactorEnabled: boolean;
}

export interface AuthSession {
  accessToken: string;
  expiresUtc: string;
  user: UserDto;
}

export interface LoginResponse {
  requiresTwoFactor: boolean;
  twoFactorToken: string | null;
  session: AuthSession | null;
}

export interface CredentialInput {
  authType: AuthType;
  userName?: string | null;
  secret?: string | null;
  tenantId?: string | null;
  clientId?: string | null;
}

export interface CredentialDto {
  authType: AuthType;
  userName: string | null;
  hasSecret: boolean;
  tenantId: string | null;
  clientId: string | null;
}

export interface ServerUpsertRequest {
  displayName: string;
  host: string;
  port: number | null;
  instanceName: string | null;
  databaseName: string | null;
  declaredPlatform: PlatformType;
  environment: ServerEnvironment;
  groupId: number | null;
  tags: string[];
  isEnabled: boolean;
  encrypt: boolean;
  trustServerCertificate: boolean;
  connectTimeoutSeconds: number;
  collectionIntervalSeconds: number;
  monitoring: CredentialInput;
  admin: CredentialInput | null;
  removeAdmin: boolean;
  rowVersion?: string | null;
}

export interface CapabilityDto {
  key: string;
  title: string;
  description: string;
  isSupported: boolean;
  reason: string | null;
  alternative: string | null;
}

export interface ServerSummaryDto {
  id: number;
  displayName: string;
  host: string;
  port: number | null;
  instanceName: string | null;
  databaseName: string | null;
  declaredPlatform: PlatformType;
  detectedPlatform: PlatformType;
  effectivePlatform: PlatformType;
  environment: ServerEnvironment;
  groupId: number | null;
  groupName: string | null;
  tags: string[];
  isEnabled: boolean;
  lastStatus: ConnectionStatus;
  lastCheckedUtc: string | null;
  lastError: string | null;
  versionLabel: string | null;
  edition: string | null;
  hasAdminLogin: boolean;
}

export interface ServerInfoDto {
  productVersion: string;
  majorVersion: number;
  productLevel: string | null;
  productUpdateLevel: string | null;
  versionLabel: string;
  edition: string;
  engineEdition: number;
  hostPlatform: string | null;
  cpuCount: number | null;
  physicalMemoryMb: number | null;
  sqlStartTimeUtc: string | null;
  collation: string | null;
  serverName: string | null;
  isSysadmin: boolean;
  detectedUtc: string;
}

export interface ServerDetailDto {
  server: ServerSummaryDto;
  encrypt: boolean;
  trustServerCertificate: boolean;
  connectTimeoutSeconds: number;
  collectionIntervalSeconds: number;
  monitoring: CredentialDto;
  admin: CredentialDto | null;
  info: ServerInfoDto | null;
  permissions: string[];
  capabilities: CapabilityDto[];
  platformWarning: string | null;
  rowVersion: string;
}

export interface TestConnectionResultDto {
  success: boolean;
  status: ConnectionStatus;
  error: string | null;
  elapsedMs: number;
  detectedPlatform: PlatformType | null;
  platformWarning: string | null;
  versionLabel: string | null;
  edition: string | null;
  isSysadmin: boolean | null;
  permissions: string[];
  capabilities: CapabilityDto[];
  adminTested: boolean;
  adminSuccess: boolean;
  adminError: string | null;
}

export interface ServerGroupDto {
  id: number;
  name: string;
  description: string | null;
  serverCount: number;
}

export interface ServerCardDto {
  id: number;
  displayName: string;
  host: string;
  platform: PlatformType;
  environment: ServerEnvironment;
  groupName: string | null;
  isEnabled: boolean;
  status: ConnectionStatus;
  lastCheckedUtc: string | null;
  lastError: string | null;
  versionLabel: string | null;
  edition: string | null;
  cpuCount: number | null;
  physicalMemoryMb: number | null;
  sqlStartTimeUtc: string | null;
  issues: string[];
}

export interface DashboardDto {
  totalServers: number;
  online: number;
  unreachable: number;
  notChecked: number;
  disabled: number;
  servers: ServerCardDto[];
}

export interface SearchHitDto {
  kind: "server" | "group";
  id: number;
  title: string;
  subtitle: string | null;
  url: string;
}

export interface UserAdminDto {
  id: string;
  email: string;
  displayName: string;
  role: Role | "";
  allServerGroups: boolean;
  groupIds: number[];
  twoFactorEnabled: boolean;
  isLockedOut: boolean;
  createdUtc: string;
  lastLoginUtc: string | null;
}

export interface TwoFactorStatusDto {
  enabled: boolean;
  recoveryCodesLeft: number;
}

export interface TwoFactorSetupDto {
  sharedKey: string;
  authenticatorUri: string;
}
