# Deploying IntelligenceDB on AWS

This guide puts the whole portal on **one EC2 server** with Docker:

```
Internet ──HTTPS──► Caddy (automatic Let's Encrypt certificate)
                      └─► api     portal: web app + REST API + agent endpoints
                          worker  collects servers the portal can reach directly
                          sql     SQL Server Express: repository database (not reachable from outside)

Office network: Agent (Windows service) ──outbound HTTPS──► https://your-domain/api/agent/...
```

Time needed: about 30–45 minutes. Cost: roughly the price of one `t3.medium` EC2 instance, an Elastic IP
and 30 GB of disk. Check the AWS pricing calculator for your region.

---

## 1. Before you start

- An AWS account.
- A domain or subdomain you control, e.g. `monitor.yourcompany.com`. HTTPS needs it, and agents only
  connect over HTTPS.

## 2. Create the server (EC2)

1. AWS Console → **EC2 → Launch instance**.
2. **Name:** `intelligencedb`.
3. **AMI:** Ubuntu Server 24.04 LTS (x86_64).
4. **Instance type:** `t3.medium` (2 vCPU, 4 GB RAM). Use `t3.large` for more than ~50 monitored servers.
5. **Key pair:** create one and download the `.pem` file (needed for SSH).
6. **Network settings → security group:**
   | Type | Port | Source |
   |---|---|---|
   | SSH | 22 | **My IP** only |
   | HTTP | 80 | Anywhere (needed for the certificate) |
   | HTTPS | 443 | Anywhere |
   Do **not** open 1433: the repository SQL Server stays inside Docker.
7. **Storage:** 30 GB gp3.
8. **Launch instance**.
9. **EC2 → Elastic IPs → Allocate**, then **Associate** it with the instance, so the IP never changes.

## 3. Point the domain to the server

At your DNS provider (or Route 53) create an **A record**:
`monitor.yourcompany.com` → the Elastic IP. Wait until `ping monitor.yourcompany.com` shows that IP.

## 4. Install Docker and the code

Connect with SSH (Windows PowerShell works too):

```bash
ssh -i intelligencedb.pem ubuntu@<ELASTIC-IP>
```

Then on the server:

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker ubuntu
exit     # log out and SSH in again so the docker group applies
```

```bash
git clone https://github.com/JawadFarooq888/Intelligence-Db.git
cd Intelligence-Db/deploy
cp .env.example .env
openssl rand -base64 48      # copy the output for JWT_SIGNING_KEY
nano .env                    # set DOMAIN, ADMIN_EMAIL, ADMIN_PASSWORD, SQL_SA_PASSWORD, JWT_SIGNING_KEY
```

## 5. Start IntelligenceDB

```bash
docker compose up -d --build
```

The first build takes 5–10 minutes. Check that everything runs:

```bash
docker compose ps           # all services "running"
docker compose logs -f api  # Ctrl+C to leave
```

Open **https://monitor.yourcompany.com** and sign in with `ADMIN_EMAIL` / `ADMIN_PASSWORD`.
Then change the password and turn on two-factor authentication (Settings).

## 6. Connect servers

**Cloud servers (RDS, Azure SQL, ...):** Servers → Add server → *Connect via: Directly from the portal*.
Allow the EC2 Elastic IP in the database's security group or firewall (port 1433).

**Servers inside an office or data centre:** use an agent (no inbound firewall rule needed):

1. Portal → **Settings → Agents → Add agent** → copy the command shown.
2. GitHub → **Actions** → latest green run → **Artifacts** → download `intelligencedb-agent-win-x64`.
3. On one Windows machine in that network: unzip, open PowerShell **as Administrator** in the folder, run
   `Set-ExecutionPolicy -Scope Process Bypass`, then paste the command.
4. When the agent shows **Online**, add servers with *Connect via: Agent: …*.

## 7. Update to a new version

```bash
cd ~/Intelligence-Db && git pull
cd deploy && docker compose up -d --build
```

The API applies database changes (migrations) automatically on start.

## 8. Back up the repository database

The repository holds the inventory, encrypted credentials, keys and the audit log. Daily backup example:

```bash
cd ~/Intelligence-Db/deploy
docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$(grep SQL_SA_PASSWORD .env | cut -d= -f2-)" \
  -Q "BACKUP DATABASE IntelligenceDB_Repo TO DISK = N'/var/opt/mssql/data/IntelligenceDB_Repo.bak' WITH INIT, COMPRESSION, CHECKSUM"
docker compose cp sql:/var/opt/mssql/data/IntelligenceDB_Repo.bak ./IntelligenceDB_Repo.bak
# optional: copy it to S3
# aws s3 cp ./IntelligenceDB_Repo.bak s3://your-bucket/intelligencedb/$(date +%F).bak
```

Note: SQL Server Express does not support `COMPRESSION`; remove it if the backup fails with that message.
Also take EC2 volume snapshots (EC2 → Elastic Block Store → Lifecycle Manager).

## 9. Optional: Amazon RDS for the repository

To let AWS manage the repository database (automatic backups, patching):

1. Create **RDS → SQL Server** (Express or Standard), not publicly accessible, in the same VPC as the EC2
   instance. Allow port 1433 from the EC2 security group.
2. In `.env` set
   `REPO_CONNECTION=Server=<rds-endpoint>,1433;Database=IntelligenceDB_Repo;User Id=<master user>;Password=<password>;Encrypt=True;TrustServerCertificate=True`
3. Start without the local SQL container: `docker compose up -d --build api worker caddy`.

## Troubleshooting

| Problem | Fix |
|---|---|
| Browser shows a certificate error | DNS does not point to the Elastic IP yet, or port 80 is closed. `docker compose logs caddy` shows the reason. |
| "This site can't be reached" | Security group must allow 80 and 443. |
| Login page loads but sign-in fails right after first start | Wait a minute: the API creates the database on the first start (`docker compose logs api`). |
| Agent stays "Offline" | The agent machine must reach `https://your-domain` (proxy/firewall for outbound HTTPS). See Windows Event Viewer → Application. |
| Worker shows no data for a server | Portal → server → Overview → *Collection status* shows the error per collector. |
