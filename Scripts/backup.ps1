<#
    Everything Aros has that GitHub does not.

    The repository rebuilds the program. It does not rebuild what the program learned: the
    database, the audio that was paid for a clip at a time, and the settings file holding the
    two API keys. A fresh clone on a new machine starts blank. This puts the rest somewhere
    it survives the machine.

    The snapshot goes to a restic repository, which encrypts on this side before anything
    leaves: the archive carries the OpenAI and Narakeet keys, and a cloud folder that can read
    its own contents is not a place to put them. Credentials live in C:\Aros\backup.env.ps1,
    outside the repository and outside the deploy folder.

    Run it from Task Scheduler. It needs no arguments and stops the service for nothing —
    pg_dump takes a consistent snapshot of a running database.
#>
[CmdletBinding()]
param(
    [string] $Settings,
    [string] $Media    = 'C:\Aros\media',
    [string] $Ssl      = 'C:\Aros\ssl',
    [string] $Staging  = 'C:\Aros\backup-staging',
    [string] $Restic   = 'C:\Aros\tools\restic.exe',
    [string] $Secrets  = 'C:\Aros\backup.env.ps1',

    # Kept because the last fortnight is what you actually restore from, and the older ones are
    # there for the mistake you do not notice the same day
    [int] $KeepDaily   = 14,
    [int] $KeepWeekly  = 8,
    [int] $KeepMonthly = 12
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not always populated the way a param default needs it to be, so the
# paths that hang off the repository are worked out here instead
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Settings) { $Settings = Join-Path $here '..\src\Aros.Api\appsettings.json' }
$repo = Join-Path $here '..'

function Fail([string] $message) {
    Write-Host "[backup] $message" -ForegroundColor Red
    exit 1
}

function Say([string] $message) {
    Write-Host "[backup] $message"
}

# ---------------------------------------------------------------- credentials
if (-not (Test-Path $Secrets)) {
    Fail "No credentials at $Secrets. See Scripts\README.md for what belongs in it."
}

. $Secrets

foreach ($name in 'RESTIC_REPOSITORY', 'RESTIC_PASSWORD') {
    if (-not (Get-Item "env:$name" -ErrorAction SilentlyContinue).Value) {
        Fail "$name is not set by $Secrets."
    }
}

if (-not (Test-Path $Restic)) { Fail "restic is not at $Restic." }

# --------------------------------------------------- where Postgres is, per the app
if (-not (Test-Path $Settings)) { Fail "No settings file at $Settings." }

$connection = (Get-Content $Settings -Raw | ConvertFrom-Json).ConnectionStrings.Default
if (-not $connection) { Fail "$Settings has no ConnectionStrings.Default." }

$parts = @{}
foreach ($pair in $connection.Split(';')) {
    if ($pair -match '^\s*([^=]+)=(.*)$') { $parts[$matches[1].Trim().ToLower()] = $matches[2].Trim() }
}

$pgDump = (Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\pg_dump.exe' -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1).FullName
if (-not $pgDump) { Fail 'pg_dump.exe not found under C:\Program Files\PostgreSQL.' }

# ------------------------------------------------------------------ the snapshot
if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
New-Item -ItemType Directory -Path $Staging -Force | Out-Null

try {
    New-Item -ItemType Directory -Path "$Staging\db", "$Staging\config" -Force | Out-Null

    # Never on the command line: an argument is visible to anything that can list processes
    $env:PGPASSWORD = $parts['password']

    Say "Dumping $($parts['database'])..."
    & $pgDump -h $parts['host'] -p $parts['port'] -U $parts['username'] `
        -d $parts['database'] -Fc -f "$Staging\db\aros.dump"
    if ($LASTEXITCODE -ne 0) { Fail "pg_dump exited $LASTEXITCODE." }

    $env:PGPASSWORD = $null

    if (Test-Path $Media) { Copy-Item $Media "$Staging\media" -Recurse -Force }
    if (Test-Path $Ssl)   { Copy-Item $Ssl   "$Staging\ssl"   -Recurse -Force }
    Copy-Item $Settings "$Staging\config\appsettings.json" -Force

    # What a restore is looking at, in case it is being read a year from now by someone who
    # has forgotten what was in the box
    $commit = (& git -C $repo rev-parse --short HEAD 2>$null)
    $clips  = (Get-ChildItem "$Staging\media" -Recurse -File -ErrorAction SilentlyContinue).Count

    @(
        "taken       $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        "machine     $env:COMPUTERNAME"
        "commit      $commit"
        "database    $($parts['database'])"
        "dump bytes  $((Get-Item "$Staging\db\aros.dump").Length)"
        "media files $clips"
    ) | Set-Content "$Staging\MANIFEST.txt" -Encoding utf8

    Say 'Uploading...'
    & $Restic backup $Staging --tag aros --host aros
    if ($LASTEXITCODE -ne 0) { Fail "restic backup exited $LASTEXITCODE." }

    Say 'Trimming old snapshots...'
    & $Restic forget --tag aros --keep-daily $KeepDaily --keep-weekly $KeepWeekly `
        --keep-monthly $KeepMonthly --prune
    if ($LASTEXITCODE -ne 0) { Fail "restic forget exited $LASTEXITCODE." }

    Say 'Done.'
}
finally {
    # The staging tree holds the keys in the clear. It does not outlive the run, including
    # the runs that fail halfway.
    $env:PGPASSWORD = $null
    if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force -ErrorAction SilentlyContinue }
}
