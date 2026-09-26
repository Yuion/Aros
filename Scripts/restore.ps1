<#
    The other half: putting a snapshot back.

    Two modes, because they carry different risks.

        .\restore.ps1                 fetches a snapshot to C:\Aros\restore and stops there
        .\restore.ps1 -Apply          also writes it into Postgres, the media folder and the
                                      settings file

    Without -Apply nothing on this machine changes, which is the mode for "what was in there
    on Tuesday". With it, this is how a new computer is brought up: install Postgres and
    Node, clone the repository, run this, then commit once to deploy.

    -Apply refuses to write over a database that already exists. -Force overrides that, and
    there is no undo: the existing database is dropped.
#>
[CmdletBinding()]
param(
    [string] $Snapshot = 'latest',
    [string] $To       = 'C:\Aros\restore',
    [switch] $Apply,
    [switch] $Force,

    # Both exist so that a restore can be rehearsed against a scratch database instead of the
    # real one. A drill that writes over the thing it is drilling for is not a drill.
    [string] $Database,
    [string] $Service = 'ArosApi',

    [string] $Settings,
    [string] $Media    = 'C:\Aros\media',
    [string] $Ssl      = 'C:\Aros\ssl',
    [string] $Restic   = 'C:\Aros\tools\restic.exe',
    [string] $Secrets  = 'C:\Aros\backup.env.ps1'
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not always populated the way a param default needs it to be, so the
# paths that hang off the repository are worked out here instead
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Settings) { $Settings = Join-Path $here '..\src\Aros.Api\appsettings.json' }

function Fail([string] $message) {
    Write-Host "[restore] $message" -ForegroundColor Red
    exit 1
}

function Say([string] $message) {
    Write-Host "[restore] $message"
}

# Runs an external program without letting anything it prints to stderr end the script.
#
# PowerShell wraps a native command's stderr in an error record, and with the strict setting
# above that record is fatal - but only when the streams are redirected, which is exactly what
# happens when the Backup page runs this instead of a person. Every call below checks
# $LASTEXITCODE for itself.
function Native([scriptblock] $command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $command } finally { $ErrorActionPreference = $previous }
}

if (-not (Test-Path $Secrets)) { Fail "No credentials at $Secrets." }
. $Secrets
if (-not (Test-Path $Restic))  { Fail "restic is not at $Restic." }

# --------------------------------------------------------------- fetch it down
if (Test-Path $To) { Remove-Item $To -Recurse -Force }
New-Item -ItemType Directory -Path $To -Force | Out-Null

Say "Fetching snapshot $Snapshot..."

# Restored by subfolder rather than whole, which is not a detail. A snapshot remembers the
# absolute path it was taken from, so restoring it plainly rebuilds C\Aros\backup-staging
# underneath the target - including a folder named C, carrying the drive root's permissions,
# which the next run is then not allowed to delete. Asking for the subfolder lands its
# contents at the target and leaves no such thing behind.
$described = Native { & $Restic snapshots $Snapshot --json } | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $described) { Fail "No snapshot '$Snapshot' in the repository." }

$taken = @($described)[0]
$subpath = '/' + $taken.paths[0].Replace(':', '').Replace('\', '/')

Native { & $Restic restore "$($taken.short_id):$subpath" --target $To }
if ($LASTEXITCODE -ne 0) { Fail "restic restore exited $LASTEXITCODE." }

$root = $To
if (-not (Test-Path "$root\MANIFEST.txt")) { Fail 'No MANIFEST.txt in the snapshot - is this an Aros backup?' }

Write-Host ''
Get-Content "$root\MANIFEST.txt" | ForEach-Object { Write-Host "  $_" }
Write-Host ''

if (-not $Apply) {
    Say "Fetched to $root. Nothing on this machine was changed."
    Say 'Run again with -Apply to put it back.'
    exit 0
}

# ------------------------------------------------------------------ put it back
$settingsBackup = "$root\config\appsettings.json"
if (-not (Test-Path $settingsBackup)) { Fail 'The snapshot has no appsettings.json.' }

# The snapshot's own settings say where its database belongs - on a new machine the local
# file does not exist yet, which is half the reason it is in the backup
$connection = (Get-Content $settingsBackup -Raw | ConvertFrom-Json).ConnectionStrings.Default
$parts = @{}
foreach ($pair in $connection.Split(';')) {
    if ($pair -match '^\s*([^=]+)=(.*)$') { $parts[$matches[1].Trim().ToLower()] = $matches[2].Trim() }
}

$bin = (Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\psql.exe' -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1).DirectoryName
if (-not $bin) { Fail 'Postgres client tools not found under C:\Program Files\PostgreSQL.' }

$env:PGPASSWORD = $parts['password']
if (-not $Database) { $Database = $parts['database'] }
$database = $Database
$psqlArgs = @('-h', $parts['host'], '-p', $parts['port'], '-U', $parts['username'])

try {
    $exists = Native { & "$bin\psql.exe" @psqlArgs -d postgres -tAc `
        "SELECT 1 FROM pg_database WHERE datname = '$database'" }

    if ($exists -eq '1' -and -not $Force) {
        Fail "The database '$database' already exists. Re-run with -Force to drop and replace it."
    }

    $service = $null
    if ($Service) { $service = Get-Service $Service -ErrorAction SilentlyContinue }
    if ($service -and $service.Status -eq 'Running') {
        Say "Stopping $Service..."
        Stop-Service $Service -Force
    }

    if ($exists -eq '1') {
        Say "Dropping $database..."
        Native { & "$bin\psql.exe" @psqlArgs -d postgres -c "DROP DATABASE $database WITH (FORCE)" }
        if ($LASTEXITCODE -ne 0) { Fail "Could not drop $database." }
    }

    Say "Creating $database..."
    Native { & "$bin\psql.exe" @psqlArgs -d postgres -c "CREATE DATABASE $database" }
    if ($LASTEXITCODE -ne 0) { Fail "Could not create $database." }

    Say 'Restoring the dump...'
    Native { & "$bin\pg_restore.exe" @psqlArgs -d $database --no-owner "$root\db\aros.dump" }
    if ($LASTEXITCODE -ne 0) { Fail "pg_restore exited $LASTEXITCODE." }

    if (Test-Path "$root\media") {
        Say 'Putting the audio back...'
        if (Test-Path $Media) { Remove-Item $Media -Recurse -Force }
        Copy-Item "$root\media" $Media -Recurse -Force
    }

    if (Test-Path "$root\ssl") {
        if (Test-Path $Ssl) { Remove-Item $Ssl -Recurse -Force }
        Copy-Item "$root\ssl" $Ssl -Recurse -Force
    }

    Say 'Putting the settings back...'
    New-Item -ItemType Directory -Path (Split-Path $Settings) -Force | Out-Null
    Copy-Item $settingsBackup $Settings -Force

    if ($service) {
        Say "Starting $Service..."
        Start-Service $Service
    }

    Say 'Restored. Commit once to rebuild and deploy the program itself.'
}
finally {
    $env:PGPASSWORD = $null
}
