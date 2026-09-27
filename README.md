# Aros

A personal Chinese-learning platform: an AI tutor that teaches lessons, and trainers that drill
what the lessons taught — vocabulary in six directions, sentences by ear, grammar patterns from
tiles — all scheduled by what has been missed and when.

ASP.NET Core 9 API, Vue 3 front end, PostgreSQL 17. It runs as two Windows services on one
machine on the local network and is reachable from nowhere else.

---

## Server Requirements

### Software to Install

| Software | Version | Install |
|---|---|---|
| .NET SDK | 9.x | `winget install Microsoft.DotNet.SDK.9` |
| EF Core tools | 9.x | `dotnet tool install --global dotnet-ef` |
| Node.js | 20+ LTS | `winget install OpenJS.NodeJS.LTS` |
| PostgreSQL | 17 | `winget install PostgreSQL.PostgreSQL.17` |
| nginx | 1.29+ | `winget install nginxinc.nginx` |
| NSSM | latest | `winget install NSSM.NSSM` |
| Git | latest | `winget install Git.Git` |
| restic | 0.19+ | `winget install restic.restic`, then copy the exe to `C:\Aros\tools\restic.exe` |

restic is copied to a fixed path on purpose: the backup runs as a service, and a service does
not have the PATH an interactive shell has.

---

### PostgreSQL Setup

1. Set a password for the `postgres` user
2. Create the database:
```sql
CREATE DATABASE aros;
```
3. Connection string format:
```
Host=127.0.0.1;Port=5432;Database=aros;Username=postgres;Password=<password>;SSL Mode=Disable
```

---

### Configuration Files

**`src/Aros.Api/appsettings.json`** is **not in git** and must be written by hand on each
server. It holds two API keys and the database password, which is why it is gitignored, why it
is in the backup, and why it is never logged.

```json
{
  "ConnectionStrings": {
    "Default": "Host=127.0.0.1;Port=5432;Database=aros;Username=postgres;Password=<password>;SSL Mode=Disable"
  },
  "Tts": {
    "ApiKey": "<narakeet-api-key>"
  },
  "Ai": {
    "ApiKey": "<openai-api-key>",
    "Model": "<model-id>",
    "FallbackModel": "<model-id>",
    "MaxOutputTokens": 16000,
    "DailyTokenBudget": 200000,
    "MaxRequestsPerHour": 120
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

What each section is for, and what happens without it:

| Setting | Required | Without it |
|---|---|---|
| `ConnectionStrings:Default` | yes | nothing starts |
| `Tts:ApiKey` | for audio | no clip can be synthesised; already-cached audio still plays |
| `Ai:ApiKey`, `Ai:Model` | for the tutor | the tutor refuses, naming the missing setting. There is no default model: a hard-coded id goes stale and becomes a 404 on the first call of the day |
| `Ai:FallbackModel` | no | used only when the primary answers "model not found", so a rename survives |
| `Ai:DailyTokenBudget`, `Ai:MaxRequestsPerHour` | no | these bound a *bug* — a retry storm, a loop — rather than a stranger; the service is closed, so nobody else can spend it |

`Syllabus:Level` is the HSK level the tutor works towards, default 1. The word list for every
level ships with the API, so raising the goal is this one number.

`Tts:Voice`, `Tts:MediaPath` (default `C:\Aros\media\tts`) and the `Backup` section all have
working defaults and are only worth setting to move something off its usual path.

The cached audio under `C:\Aros\media` is deliberately outside the deploy folder, so a redeploy
cannot wipe it. Each clip cost a paid synthesis; it is content-addressed and would regenerate,
but not for free.

---

### SSL Certificate

Generate a self-signed certificate (replace IP with the server's local IP):
```bash
mkdir C:\Aros\ssl
openssl req -x509 -newkey rsa:2048 -keyout C:/Aros/ssl/key.pem -out C:/Aros/ssl/cert.pem -days 3650 -nodes -subj "/CN=<local-ip>" -addext "subjectAltName=IP:<local-ip>,IP:127.0.0.1"
```

Install the cert on client devices to avoid browser warnings.

---

### nginx

nginx serves the built front end and passes `/api/` to the API. Replace the `http` block in
`conf/nginx.conf` inside the winget package directory:

```nginx
http {
    include       mime.types;
    default_type  application/octet-stream;
    sendfile      on;
    keepalive_timeout 65;

    server {
        listen 80;
        return 301 https://$host$request_uri;
    }

    server {
        listen 443 ssl;
        root C:/Aros/www;
        index index.html;

        ssl_certificate     C:/Aros/ssl/cert.pem;
        ssl_certificate_key C:/Aros/ssl/key.pem;
        ssl_protocols       TLSv1.2 TLSv1.3;
        ssl_ciphers         HIGH:!aNULL:!MD5;

        # A hash-router app: every path that is not a file is index.html
        location / {
            try_files $uri $uri/ /index.html;
        }

        location /api/ {
            proxy_pass http://127.0.0.1:5000;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
        }
    }
}
```

The API listens on Kestrel's default `http://localhost:5000` and is never exposed directly —
only nginx talks to it, and only over the loopback address.

---

### First Deploy

Write `src/Aros.Api/appsettings.json` first: the next command reads the connection string from
it.

```bash
# 1. Create the schema
dotnet ef database update --project src/Aros.Api

# 2. Publish the API
dotnet publish src/Aros.Api/Aros.Api.csproj -c Release -r win-x64 --self-contained false -o C:/Aros/api

# 3. Copy the config (not in git, and not copied by the deploy hook either)
copy src\Aros.Api\appsettings.json C:\Aros\api\appsettings.json

# 4. Build and deploy the front end
cd src/Aros.UI
npm install
npm run build -- --outDir C:/Aros/www --emptyOutDir
```

Restoring a backup instead of starting empty? See **Backups** below — `restore.ps1 -Apply` does
steps 1 and 3 for you, along with the audio and the certificate.

---

### Windows Services

Run both commands in **PowerShell as Administrator**.

**API service:**
```powershell
New-Service -Name "ArosApi" -BinaryPathName "C:\Aros\api\Aros.Api.exe" -DisplayName "Aros API" -StartupType Automatic
Start-Service ArosApi
```

**Web (nginx) service:**
```powershell
$nginx = "C:\Users\<user>\AppData\Local\Microsoft\WinGet\Packages\nginxinc.nginx_Microsoft.Winget.Source_8wekyb3d8bbwe\nginx-1.29.8\nginx.exe"
nssm install ArosWeb $nginx
nssm set ArosWeb AppDirectory "<nginx-directory>"
nssm set ArosWeb Start SERVICE_AUTO_START
Start-Service ArosWeb
```

**Grant service control without admin** (run once per user) — the deploy hook stops and starts
the API on every commit, and prompting for elevation each time would make that unusable:
```powershell
sc.exe sdset ArosApi "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)(A;;RPWPCR;;;$((New-Object System.Security.Principal.NTAccount($env:USERNAME)).Translate([System.Security.Principal.SecurityIdentifier]).Value))"
```

---

### Firewall Rules

Run in **PowerShell as Administrator**:
```powershell
New-NetFirewallRule -DisplayName "Aros Web (HTTP)"  -Direction Inbound -Protocol TCP -LocalPort 80  -Action Allow
New-NetFirewallRule -DisplayName "Aros Web (HTTPS)" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
```

These open the machine to the local network and nothing further. There is no authentication
anywhere in the application, which is only safe because of that: it is a closed service on one
network, not a site.

---

### Auto-Deploy (Git Hook)

`.git/hooks/post-commit` deploys on every commit: it stops the API service, publishes, starts
it again, then builds the front end into `C:/Aros/www`. Nothing else to set up.

Two consequences worth knowing. A commit is a deploy, so a migration must be applied by hand
*before* the commit that ships it. And the hook does not copy `appsettings.json` — that file is
written once per machine and left alone.

---

### Backups

The repository rebuilds the program. It does not rebuild what the program learned: the
database, the audio that cost a paid synthesis a clip, and `appsettings.json` itself. A fresh
clone on a new computer starts blank.

`Scripts\backup.ps1` takes an encrypted snapshot of all of it to a restic repository — client
side, before anything is uploaded, because that archive carries both API keys.
`Scripts\restore.ps1` puts one back, either to inspect or onto a bare machine. Nothing runs on a
schedule: the **Backup** page at the foot of the sidebar takes a snapshot when you ask for one,
and also shows how old the newest is, verifies the stored data, fetches one to look at and
deletes the ones you are done with.

`Scripts\README.md` has the setup, the new-computer procedure, and which secrets must live
somewhere other than this machine.

---

### Linux Migration Notes

When moving to Linux, replace:
- Windows Services → `systemd` services
- NSSM → not needed
- nginx config path → `/etc/nginx/nginx.conf`
- SSL cert path → update nginx.conf accordingly
- PowerShell service commands → `systemctl start/stop/restart`
- `Scripts\*.ps1` → shell equivalents; restic, pg_dump and pg_restore are the same on both
