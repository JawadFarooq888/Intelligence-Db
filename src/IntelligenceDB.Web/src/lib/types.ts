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
  agentId: string | null;
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
  agentId: string | null;
  agentName: string | null;
  healthScore: number | null;
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
  healthScore: number | null;
  agentName: string | null;
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
  kind: "server" | "group" | "database";
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

// ---------------- Phase 2: monitoring ----------------

export type AdviceSeverity = "Info" | "Warning" | "Critical";

export interface AdviceItem {
  severity: AdviceSeverity;
  code: string;
  title: string;
  explanation: string;
  current: string | null;
  recommended: string | null;
  target: string | null;
}

export interface HealthDeduction {
  rule: string;
  points: number;
  message: string;
}

export interface HealthDto {
  score: number;
  deductions: HealthDeduction[];
  calculatedUtc: string;
}

export interface MetricPoint {
  time: string;
  sqlCpuPct: number | null;
  otherCpuPct: number | null;
  pageLifeExpectancy: number | null;
  memoryGrantsPending: number | null;
  totalServerMemoryMb: number | null;
  targetServerMemoryMb: number | null;
  batchRequestsPerSec: number | null;
  userConnections: number | null;
  blockedSessions: number | null;
}

export interface ConfigSettingDto {
  name: string;
  value: number;
  valueInUse: number;
  isAdvanced: boolean;
  isDynamic: boolean;
  description: string | null;
  pendingRestart: boolean;
}

export interface VolumeDto {
  mountPoint: string;
  label: string | null;
  totalMb: number;
  freeMb: number;
  freePct: number;
}

export interface CollectorStatusDto {
  collector: string;
  lastRunUtc: string;
  success: boolean;
  error: string | null;
  durationMs: number;
  source: string;
}

export interface ServerOverviewDto {
  health: HealthDto | null;
  latest: MetricPoint | null;
  configAdvice: AdviceItem[];
  settings: ConfigSettingDto[];
  volumes: VolumeDto[];
  databases: { total: number; online: number; withCritical: number; withWarning: number; totalDataMb: number; totalLogMb: number };
  collectors: CollectorStatusDto[];
  volumesSupported: boolean;
}

export interface WaitRowDto {
  waitType: string;
  category: string;
  explanation: string;
  waitTimeMs: number;
  signalWaitMs: number;
  waitingTasks: number;
  avgMsPerWait: number;
  percent: number;
}

export interface FileLatencyDto {
  databaseName: string;
  fileId: number;
  reads: number;
  writes: number;
  avgReadMs: number;
  avgWriteMs: number;
  mbRead: number;
  mbWritten: number;
}

export interface PerformanceDto {
  hours: number;
  bucketMinutes: number;
  series: MetricPoint[];
  waits: WaitRowDto[];
  files: FileLatencyDto[];
}

export interface DatabaseRowDto {
  id: number;
  serverId: number;
  serverName: string;
  name: string;
  state: string;
  recoveryModel: string;
  compatibilityLevel: number;
  owner: string | null;
  isReadOnly: boolean;
  dataSizeMb: number | null;
  dataUsedMb: number | null;
  logSizeMb: number | null;
  logUsedMb: number | null;
  vlfCount: number | null;
  logReuseWait: string | null;
  lastFullBackupUtc: string | null;
  lastDiffBackupUtc: string | null;
  lastLogBackupUtc: string | null;
  lastGoodCheckDbUtc: string | null;
  checkDbNever: boolean;
  criticalCount: number;
  warningCount: number;
  infoCount: number;
  lastSeenUtc: string;
  isDropped: boolean;
  backupHistoryAvailable: boolean;
}

export interface DatabaseFileDto {
  fileId: number;
  logicalName: string;
  physicalName: string | null;
  typeDesc: string;
  sizeMb: number;
  usedMb: number | null;
  maxSizeMb: number | null;
  isPercentGrowth: boolean;
  growthValue: number;
  volumeMountPoint: string | null;
}

export interface SizePointDto {
  day: string;
  dataSizeMb: number;
  dataUsedMb: number | null;
  logSizeMb: number;
  logUsedMb: number | null;
}

export interface BackupRowDto {
  id: number;
  backupType: string;
  startUtc: string;
  finishUtc: string;
  durationSeconds: number;
  sizeBytes: number;
  compressedBytes: number | null;
  isCopyOnly: boolean;
  physicalDevice: string | null;
}

export interface DatabaseDetailDto {
  database: DatabaseRowDto;
  files: DatabaseFileDto[];
  advice: AdviceItem[];
  sizeHistory: SizePointDto[];
  backups: BackupRowDto[];
}

export interface AgentDto {
  id: string;
  name: string;
  isEnabled: boolean;
  isOnline: boolean;
  createdUtc: string;
  lastSeenUtc: string | null;
  version: string | null;
  machineName: string | null;
  lastIp: string | null;
  keyPrefix: string;
  serverCount: number;
}

export interface AgentCreatedDto {
  agent: AgentDto;
  key: string;
}