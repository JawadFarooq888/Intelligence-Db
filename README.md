# IntelligenceDB

A web portal to monitor and safely administer many SQL Server instances: on-premises, Azure SQL Managed Instance,
Azure SQL Database, Amazon RDS and Google Cloud SQL.

> **Status: Phase 1 of 7.** Phase 1 covers sign-in, roles, two-factor authentication, adding / testing / grouping
> servers, capability detection, encrypted credentials, the login scripts and a basic dashboard.
> The design and the full phase plan are in [docs/PHASE-0-DESIGN.md](docs/PHASE-0-DESIGN.md).

---

## 1. Quick start in GitHub Codespaces (nothing to install on your PC)

The repository contains a dev container (`.devcontainer/`) with the .NET 10 SDK, Node 22 and a
**SQL Server 2022 Developer** container. That SQL Server holds the IntelligenceDB repository database **and** is your first
server to monitor.

1. Push this folder to a GitHub repository (see "Pushing to GitHub" below).
2. On GitHub: **Code → Codespaces → Create codespace on main**.
   Choose a **4-core** machine (SQL Server needs about 2 GB of RAM).
3. Wait until the setup script finishes (first time: about 5 minutes). It restores packages, creates the first
   database migration and installs the web packages.
4. In the Codespace terminal run:

   ```bash
   bash scripts/dev.sh
   ```

5. When port **5173** opens in the browser (or open it from the **Ports** tab), sign in with the development admin:

   | Email | Password |
   |---|---|
   | `admin@intelligencedb.local` | `Admin!Dev2026pass` |

6. **Commit the generated migration** (only the first time) so it is not re-created:

   ```bash
   git add src/IntelligenceDB.Infrastructure/Repository/Migrations
   git commit -m "Add initial repository migration"
   git push
   ```

### Pushing to GitHub

```bash
cd E:\IntellingenceDb
git init -b main
git add .
git commit -m "IntelligenceDB phase 1"
git remote add origin https://github.com/<you>/intelligencedb.git
git push -u origin main
```

---

## 2. What to test in Phase 1

| # | Test | Expected result |
|---|---|---|
| 1 | Sign in as the admin | Dashboard opens. No servers yet. |
| 2 | **Servers → Add server**: Display name `DEV-SQL`, Platform *SQL Server (on-premises / VM)*, Host `sql`, Port `1433`, tick **Trust server certificate**, SQL authentication, login `sa`, password `IntelliDb!Dev2026` → **Test connection** | "Connected", *SQL Server 2022 … Developer Edition*, all monitoring features ticked, control actions marked "No admin login is configured", and a note that `sa` is more than monitoring needs. |
| 3 | **Add server** | Server page opens with version, CPUs, memory, uptime, features and permissions. |
| 4 | Create a real least-privilege login: on the server page open **Login scripts → Monitoring → SQL Server (on-premises)**, copy it, replace the password, run it against `sql` (VS Code "SQL Server" extension is pre-installed; connect to `sql` as `sa`). Then **Edit** the server, change the login to `IntelligenceDB_Monitor` and **Test connection**. | Connected. *Monitoring login is sysadmin: No (good)*. Performance views, msdb, Agent, backups, disk space all supported. |
| 5 | Edit again, set the monitoring password to something wrong, **Test connection** | "Login failed …" with advice; the password is never shown. |
| 6 | Add a server with Host `nowhere.invalid` | Saved, status *Unreachable*, clear message. The dashboard keeps working. |
| 7 | Add a server with Platform *Azure SQL Database* but no database | Validation error "Azure SQL Database needs the database name". |
| 8 | **Servers → Groups**: create `Finance`, assign a server to it | Group filter and global search (Ctrl+K) find it. |
| 9 | **Settings → Users → Add user** with role *Viewer*, limited to group `Finance`. Sign in as that user in a private window. | Viewer sees only Finance servers; no Add/Edit/Delete buttons. |
| 10 | **Settings → Two-factor authentication**: scan the QR code, enter the code. Sign out and back in. | A 6-digit code is asked for. Recovery codes work once each. |
| 11 | Wrong password 5 times | Account locked for 15 minutes (an Admin can unlock it in Settings → Users). |
| 12 | Dashboard → **Check all now** | All servers re-tested in parallel. |
| 13 | Servers → **Export CSV** | CSV opens in Excel. |
| 14 | Sidebar modules with a **P2-P6** badge | They state which phase delivers them and what they will do (no fake data). |

### Automated tests

```bash
dotnet test IntelligenceDB.slnx
```

- **Unit tests**: capability detection, platform detection, input validation, connection-string safety.
- **Integration tests** connect to a real SQL Server: in the Codespace they use the `sql` container
  (`IDB_TEST_SQL_CONNECTION`); elsewhere they start a SQL Server 2022 container with Testcontainers (needs Docker).

GitHub Actions (`.github/workflows/ci.yml`) builds with **warnings as errors**, runs all tests and builds the web app on every push.

---

## 3. Running on your own machine (without Codespaces)

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), Node.js 22+, and a SQL Server 2016+
instance for the repository database (SQL Server Developer edition is free).

```powershell
# 1. Repository connection and first admin (PowerShell; use 'export' on Linux/macOS)
$env:ConnectionStrings__Repository = "Server=localhost;Database=IntelligenceDB_Repo;Integrated Security=True;TrustServerCertificate=True"
$env:Jwt__SigningKey = "<random text, at least 32 characters>"
$env:Bootstrap__AdminEmail = "you@example.com"
$env:Bootstrap__AdminPassword = "<12+ chars, upper, lower, digit, symbol>"

# 2. Tools, packages and (first time only) the initial migration
dotnet tool restore
dotnet ef migrations add InitialCreate --project src/IntelligenceDB.Infrastructure --startup-project src/IntelligenceDB.Api --output-dir Repository/Migrations

# 3. API (creates the database and tables on first start)
dotnet run --project src/IntelligenceDB.Api --launch-profile http

# 4. Web app, in a second terminal
cd src/IntelligenceDB.Web
npm install
npm run dev        # open http://localhost:5173
```

If the API login may not create databases, run [sql/repository/00-create-database.sql](sql/repository/00-create-database.sql)
first (it creates the database and a dedicated `IntelligenceDB_App` login).

Docker Compose for production and the Windows / IIS guide are delivered in Phase 7.

---

## 4. Logins on the monitored servers

IntelligenceDB uses two separate logins per server:

| Login | Used for | Required |
|---|---|---|
| **Monitoring** | Reading DMVs, metadata, msdb job and backup history. Read-only. | Yes |
| **Admin** | Only actions a user confirms or approves (start/stop jobs, kill session, index maintenance). Every use is audited. | No |

Ready-made scripts (also shown in the app under **Server → Login scripts**):

| Platform | Monitoring | Admin |
|---|---|---|
| SQL Server on-premises / VM | [monitoring-login/onprem.sql](sql/monitoring-login/onprem.sql) | [admin-login/onprem.sql](sql/admin-login/onprem.sql) |
| Azure SQL Managed Instance | [monitoring-login/azure-mi.sql](sql/monitoring-login/azure-mi.sql) | [admin-login/azure-mi.sql](sql/admin-login/azure-mi.sql) |
| Azure SQL Database | [monitoring-login/azure-sqldb.sql](sql/monitoring-login/azure-sqldb.sql) | [admin-login/azure-sqldb.sql](sql/admin-login/azure-sqldb.sql) |
| Amazon RDS for SQL Server | [monitoring-login/aws-rds.sql](sql/monitoring-login/aws-rds.sql) | [admin-login/aws-rds.sql](sql/admin-login/aws-rds.sql) |
| Google Cloud SQL for SQL Server | [monitoring-login/gcp-cloudsql.sql](sql/monitoring-login/gcp-cloudsql.sql) | [admin-login/gcp-cloudsql.sql](sql/admin-login/gcp-cloudsql.sql) |

After running a script, use **Test connection**: IntelligenceDB lists every feature as available or not, with the reason
and what to do instead. Features that do not work on a platform (for example SQL Agent on Azure SQL Database) are disabled
in the UI, never left to fail.

**Authentication types:** SQL authentication, Windows authentication (IntelligenceDB must run on a domain-joined Windows
host), and Microsoft Entra ID (password, service principal, managed identity, default credential chain).

---

## 5. Security notes

- Server passwords and client secrets are encrypted with ASP.NET Core Data Protection before they are stored.
  They are never logged and never sent back to the browser (forms show "unchanged").
- The Data Protection keys are stored in the repository database (`ops.DataProtectionKeys`). In production protect them
  with a certificate (`DataProtection__CertificatePath` / `DataProtection__CertificatePassword`) or, on Windows,
  `DataProtection__UseDpapi=true`. Azure Key Vault key protection is added in Phase 7.
- Portal sign-in: ASP.NET Core Identity, 15-minute JWT access tokens kept in memory, rotating refresh tokens in an
  HttpOnly / Secure / SameSite=Strict cookie (re-use of an old token signs the user out everywhere), lockout after
  5 failed attempts, optional TOTP two-factor authentication, rate limiting on sign-in.
- Roles: **Admin** (everything), **DBA** (servers + approved actions), **Operator** (job control, Phase 3), **Viewer** (read-only).
  Non-admin users can be limited to server groups.
- All repository access is parameterised (EF Core / Dapper). Target-server input only goes into connection strings
  built with `SqlConnectionStringBuilder`, never into T-SQL text.
- Every sign-in, server change and connection test is written to the audit log (`audit.AuditLog`).
- **Change the development passwords** in `.devcontainer/docker-compose.yml` before using anything outside the dev container.

---

## 6. Backing up the repository database

`IntelligenceDB_Repo` contains your server inventory, encrypted credentials, the Data Protection keys and the audit log.
Back it up like any production database, for example:

```sql
BACKUP DATABASE [IntelligenceDB_Repo]
TO DISK = N'D:\Backups\IntelligenceDB_Repo.bak'
WITH COMPRESSION, CHECKSUM, INIT;
```

If you protect the keys with a certificate, back up the certificate (with its password) separately: without it the stored
server passwords cannot be decrypted after a restore.

---

## 7. Project layout

```
src/IntelligenceDB.Domain          entities and enums (no dependencies)
src/IntelligenceDB.Application     use cases, capability rules, validation
src/IntelligenceDB.Infrastructure  EF Core repository, Data Protection, SQL Server probe
src/IntelligenceDB.Api             ASP.NET Core API, Identity + JWT, controllers
src/IntelligenceDB.Web             React + TypeScript + Vite + Tailwind + shadcn/ui
tests/                             unit and integration tests
sql/                               repository, monitoring-login and admin-login scripts
```

The background collector (`IntelligenceDB.Worker`) is added in Phase 2.
