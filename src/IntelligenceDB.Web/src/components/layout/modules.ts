import {
  BarChart3,
  Bell,
  Briefcase,
  Database,
  FileText,
  Gauge,
  HeartPulse,
  LayoutDashboard,
  ListChecks,
  Server,
  Settings,
  ShieldCheck,
  Workflow,
  type LucideIcon,
} from "lucide-react";

export interface ModuleInfo {
  path: string;
  label: string;
  icon: LucideIcon;
  /** Phase in which the module is delivered. Modules with a later phase show what they will do. */
  phase: number;
  summary: string;
  features: string[];
}

/** Sidebar order follows the spec. */
export const modules: ModuleInfo[] = [
  {
    path: "/",
    label: "Dashboard",
    icon: LayoutDashboard,
    phase: 1,
    summary: "Health of every server at a glance.",
    features: [],
  },
  {
    path: "/servers",
    label: "Servers",
    icon: Server,
    phase: 1,
    summary: "Add, test, group and tag monitored servers.",
    features: [],
  },
  {
    path: "/databases",
    label: "Databases",
    icon: Database,
    phase: 2,
    summary: "Size, growth, files, backups and DBCC CHECKDB status of every database.",
    features: [
      "Data and log file sizes, free space and auto-growth settings with growth trend charts",
      "Recovery model, compatibility level, owner, state, VLF count and log reuse wait",
      "Last full / differential / log backup, backup history and missing-backup alerts",
      "Last successful DBCC CHECKDB date",
      "Disk / volume free space where the platform allows it",
    ],
  },
  {
    path: "/jobs",
    label: "Jobs",
    icon: Briefcase,
    phase: 3,
    summary: "SQL Agent jobs: status, history and control.",
    features: [
      "All jobs with owner, category, schedules, last / next run, duration and outcome",
      "Job history with step output and error messages",
      "Duration trend and 'running longer than usual' detection",
      "Start, stop, enable, disable and schedule changes with T-SQL preview and audit",
      "IntelligenceDB scheduler for Azure SQL Database (which has no SQL Agent)",
    ],
  },
  {
    path: "/indexes",
    label: "Indexes",
    icon: ListChecks,
    phase: 4,
    summary: "Fragmentation, usage, missing, unused and duplicate indexes.",
    features: [
      "Fragmentation with page-count filter, size and usage statistics",
      "Missing index suggestions with impact score (and a warning not to create them blindly)",
      "Unused, duplicate and overlapping indexes; statistics age",
      "Exact REBUILD / REORGANIZE / CREATE / DROP / UPDATE STATISTICS T-SQL, ONLINE where the edition allows",
    ],
  },
  {
    path: "/performance",
    label: "Performance",
    icon: Gauge,
    phase: 4,
    summary: "Top queries, plan regressions, live sessions, blocking and deadlocks.",
    features: [
      "Top queries by CPU, duration, reads and executions (plan cache + Query Store)",
      "Plan regression detection with Query Store",
      "Execution plan download and summary",
      "Live sessions, blocking chains with head blocker, deadlock history",
    ],
  },
  {
    path: "/security",
    label: "Security",
    icon: ShieldCheck,
    phase: 6,
    summary: "Logins, roles, sysadmins, orphaned users and permission reports.",
    features: [
      "Logins, server roles, database users and roles",
      "sysadmin members, orphaned users, logins with weak settings",
      "Permission report per user",
    ],
  },
  {
    path: "/health-checks",
    label: "Health Checks",
    icon: HeartPulse,
    phase: 6,
    summary: "Best-practice rule engine with severity, explanation and fix.",
    features: [
      "Built-in rules for configuration, backups, CHECKDB, security, tempdb and more",
      "Optional deployment of community tools (Ola Hallengren, First Responder Kit) after confirmation",
    ],
  },
  {
    path: "/automation",
    label: "Automation",
    icon: Workflow,
    phase: 5,
    summary: "Policies, maintenance windows, approvals and the kill switch.",
    features: [
      "Per action and server group: Off / Recommend / Require approval / Automatic",
      "Maintenance windows, thresholds, dry-run preview of the exact T-SQL",
      "Global and per-server kill switch; destructive actions always need approval",
    ],
  },
  {
    path: "/alerts",
    label: "Alerts",
    icon: Bell,
    phase: 3,
    summary: "Alert rules, channels, de-duplication and snooze.",
    features: [
      "Job failed / long running, missing backup, disk low, blocking, deadlock, high CPU, unreachable, CHECKDB overdue",
      "In-app, email, Microsoft Teams and Slack channels",
    ],
  },
  {
    path: "/reports",
    label: "Reports",
    icon: BarChart3,
    phase: 6,
    summary: "Scheduled health summaries as email, PDF and Excel.",
    features: ["Daily / weekly health summary per server", "PDF and Excel export"],
  },
  {
    path: "/audit",
    label: "Audit Log",
    icon: FileText,
    phase: 5,
    summary: "Who did what, when, on which server, with the exact T-SQL.",
    features: [
      "Every action is already recorded from Phase 1 (logins, server changes, connection tests)",
      "Searchable audit viewer with T-SQL, result, duration and error",
    ],
  },
  {
    path: "/settings",
    label: "Settings",
    icon: Settings,
    phase: 1,
    summary: "Your account, two-factor authentication and users.",
    features: [],
  },
];

export const CURRENT_PHASE = 2;
