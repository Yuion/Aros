# Backing Aros up

GitHub rebuilds the program. It does not rebuild what the program learned. That lives in
three places, none of them in the repository:

| | Where |
|---|---|
| The database — lessons, words, answers, schedules | Postgres `aros` |
| The audio, a paid synthesis per clip | `C:\Aros\media\tts` |
| The keys and the database password | `src\Aros.Api\appsettings.json`, gitignored |

`backup.ps1` puts all of it, plus the nginx certificate, into one encrypted snapshot.
`restore.ps1` brings it back — either to look at, or onto a bare machine.

## Setting it up

1. **A Backblaze bucket.** Make a private B2 bucket and an application key scoped to it.
   Note the bucket's S3 endpoint from the bucket details — it names the region, for example
   `s3.eu-central-003.backblazeb2.com`.

2. **`C:\Aros\backup.env.ps1`** — outside the repository and outside the deploy folder, so
   neither a commit nor a redeploy can carry it anywhere:

   ```powershell
   $env:RESTIC_REPOSITORY     = 's3:s3.eu-central-003.backblazeb2.com/your-bucket/aros'
   $env:RESTIC_PASSWORD       = 'the passphrase that encrypts the snapshots'
   $env:AWS_ACCESS_KEY_ID     = 'the B2 keyID'
   $env:AWS_SECRET_ACCESS_KEY = 'the B2 applicationKey'
   ```

   **Write the passphrase down somewhere that is not this machine.** restic encrypts before
   uploading, which is the point — and it means Backblaze cannot help you recover a snapshot
   without it. A lost passphrase is a lost backup.

3. **Initialise the repository**, once:

   ```powershell
   . C:\Aros\backup.env.ps1
   C:\Aros\tools\restic.exe init
   ```

4. **Schedule it.** Daily, whenever the machine is reliably on:

   ```powershell
   $action  = New-ScheduledTaskAction -Execute 'powershell.exe' `
       -Argument '-NonInteractive -ExecutionPolicy Bypass -File "C:\Kram\repos\MyProject\Aros\Scripts\backup.ps1"'
   $trigger = New-ScheduledTaskTrigger -Daily -At 03:00
   Register-ScheduledTask -TaskName 'Aros backup' -Action $action -Trigger $trigger `
       -RunLevel Highest -User $env:USERNAME
   ```

## Using it

```powershell
.\Scripts\backup.ps1                  # a snapshot now
.\Scripts\restore.ps1                 # fetch the latest to C:\Aros\restore, change nothing
.\Scripts\restore.ps1 -Apply -Force   # write it back over what is here
```

`-Apply` without `-Force` refuses to touch a database that already exists, because the first
thing it would do is drop it. `-Force` says do it anyway, and there is no undo.

Snapshots are kept 14 daily, 8 weekly, 12 monthly. At roughly 20 MB a snapshot and restic
storing only what changed between them, the whole history costs pennies a year.

## A new computer

1. Install Postgres 17, Node and the .NET 9 SDK.
2. Clone the repository, and put `restic.exe` at `C:\Aros\tools\restic.exe`.
3. Write `C:\Aros\backup.env.ps1` as above.
4. `.\Scripts\restore.ps1 -Apply` — database, audio, certificate and settings.
5. Install the services with NSSM, then commit once: the post-commit hook builds and deploys.

## Checking it still works

A backup nobody has restored is a guess. Once in a while:

```powershell
.\Scripts\restore.ps1
```

which fetches the latest snapshot to `C:\Aros\restore` and prints its manifest — when it was
taken, which commit was live, how big the dump is, how many clips it holds — without touching
anything. `restic check --read-data-subset=10%` verifies the stored data itself.
