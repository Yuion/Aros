# Aros

Personal application platform. Vue.js frontend, ASP.NET Core API, PostgreSQL database.

---

## Server Requirements

### Software to Install

| Software | Version | Install |
|---|---|---|
| .NET SDK | 9.x | `winget install Microsoft.DotNet.SDK.9` |
| .NET MAUI Workloads | 9.x | `dotnet workload install maui-android maui-windows` |
| Node.js | 20+ LTS | `winget install OpenJS.NodeJS.LTS` |
| PostgreSQL | 17 | `winget install PostgreSQL.PostgreSQL.17` |
| nginx | 1.29+ | `winget install nginxinc.nginx` |
| NSSM | latest | `winget install NSSM.NSSM` |
| Git | latest | `winget install Git.Git` |

---

### PostgreSQL Setup

1. Set password for `postgres` user
2. Create database:
```sql
CREATE DATABASE aros;
```
3. Connection string format:
```
Host=127.0.0.1;Port=5432;Database=aros;Username=postgres;Password=<password>;SSL Mode=Disable
```

---

### Configuration Files

These files are **not in git** and must be created manually on each server.

**`src/Aros.Api/appsettings.json`**
```json
{
  "ConnectionStrings": {
    "Default": "Host=127.0.0.1;Port=5432;Database=aros;Username=postgres;Password=<password>;SSL Mode=Disable"
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

---

### SSL Certificate

Generate self-signed certificate (replace IP with server's local IP):
```bash
mkdir C:\Aros\ssl
openssl req -x509 -newkey rsa:2048 -keyout C:/Aros/ssl/key.pem -out C:/Aros/ssl/cert.pem -days 3650 -nodes -subj "/CN=<local-ip>" -addext "subjectAltName=IP:<local-ip>,IP:127.0.0.1"
```

Install cert on client devices to avoid browser warnings.

---

### First Deploy

```bash
# 1. Restore and migrate database
dotnet ef database update --project src/Aros.Api

# 2. Publish API
dotnet publish src/Aros.Api/Aros.Api.csproj -c Release -r win-x64 --self-contained false -o C:/Aros/api

# 3. Copy config (not in git)
copy src\Aros.Api\appsettings.json C:\Aros\api\appsettings.json

# 4. Build and deploy Vue
cd src/Aros.UI
npm install
npm run build -- --outDir C:/Aros/www --emptyOutDir
```

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

**Grant service control without admin** (run once per user):
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

---

### Auto-Deploy (Git Hook)

The `post-commit` hook in `.git/hooks/post-commit` handles deploy automatically on every commit. No manual setup needed beyond the above.

---

### Linux Migration Notes

When moving to Linux, replace:
- Windows Services → `systemd` services
- NSSM → not needed
- nginx config path → `/etc/nginx/nginx.conf`
- SSL cert path → update nginx.conf accordingly
- PowerShell service commands → `systemctl start/stop/restart`
