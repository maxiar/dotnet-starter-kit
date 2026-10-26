#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the DbMigrator (migrations + seed) against Development, QA or Production from your own machine.

.DESCRIPTION
    Development: the connection string given with -ConnectionString (e.g. a local SQL Server or the one Aspire
                 started), or DatabaseOptions from appsettings.Development.json when omitted.
    QA:          DatabaseOptions from src/Host/<App>.Api/appsettings.QA.json. Needs network access to the QA SQL
                 Server (VPN). Asks you to type QA unless -Force.
    Production:  DatabaseOptions from appsettings.Production.json. Needs network access to the production SQL
                 Server. Always asks you to type PRODUCTION (-Force does not skip it).

    If the connection string still contains the __SET_BEFORE_DEPLOY__ placeholder, the script asks for the SQL
    password and passes the completed connection string to the DbMigrator process only — nothing is written to
    disk and your shell is left untouched.

    The DbMigrator and Api projects are discovered under src/Host (*.DbMigrator / *.Api), so the same script
    works in every project scaffolded from the starter kit.

.EXAMPLE
    ./deploy/scripts/db-migrate.ps1 -Environment QA -Command list-pending
.EXAMPLE
    ./deploy/scripts/db-migrate.ps1 -Environment QA -Seed
.EXAMPLE
    ./deploy/scripts/db-migrate.ps1 -Environment Production -DryRun
.EXAMPLE
    ./deploy/scripts/db-migrate.ps1 -Environment Development -ConnectionString 'Server=localhost;Database=App;User Id=sa;Password=...;TrustServerCertificate=True' -Seed
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Development', 'QA', 'Production')]
    [string] $Environment,

    # apply: pending migrations · list-pending: report only · seed: seed only · seed-demo: demo tenants (Development only)
    [ValidateSet('apply', 'list-pending', 'seed', 'seed-demo')]
    [string] $Command = 'apply',

    # With apply: run the seed (root tenant, admin user, permissions) after migrating.
    [switch] $Seed,

    # Limit to one tenant identifier (e.g. root).
    [string] $Tenant,

    # Only the tenant catalog database.
    [switch] $CatalogOnly,

    # Development only: connection string to use instead of appsettings.Development.json.
    [string] $ConnectionString,

    # Development only: provider for -ConnectionString (MSSQL or POSTGRESQL).
    [ValidateSet('MSSQL', 'POSTGRESQL')]
    [string] $Provider = 'MSSQL',

    # QA only: skip the confirmation prompt (Production always asks).
    [switch] $Force,

    # Print the target and the DbMigrator command without running it.
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
$Placeholder = '__SET_BEFORE_DEPLOY__'

# Walk up from the script's folder to the repository root (the folder that has src/Host), so the script works
# from scripts/ or deploy/scripts/.
$repoRoot = $PSScriptRoot
while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'src/Host'))) { $repoRoot = Split-Path -Parent $repoRoot }
if (-not $repoRoot) { throw "Could not find the repository root (a folder containing src/Host) above $PSScriptRoot." }
$hostDir = Join-Path $repoRoot 'src/Host'
$migratorProject = Get-ChildItem $hostDir -Directory -Filter '*.DbMigrator' | Select-Object -First 1
$apiProject = Get-ChildItem $hostDir -Directory -Filter '*.Api' | Select-Object -First 1
if (-not $migratorProject -or -not $apiProject) { throw "No *.DbMigrator / *.Api project found under $hostDir." }

if ($Command -eq 'seed-demo' -and $Environment -ne 'Development') {
    throw 'seed-demo only runs in Development (the DbMigrator refuses it elsewhere).'
}
if ($ConnectionString -and $Environment -ne 'Development') {
    throw "-ConnectionString is only for Development; $Environment uses appsettings.$Environment.json."
}

function Get-ConnectionPart([string] $Connection, [string[]] $Keys) {
    foreach ($part in $Connection -split ';') {
        $pair = $part -split '=', 2
        if ($pair.Count -eq 2 -and $Keys -contains $pair[0].Trim()) { return $pair[1].Trim() }
    }
    ''
}

function Hide-Password([string] $Connection) {
    $Connection -replace '(?i)(Password|Pwd)\s*=\s*[^;]*', '$1=***'
}

# Settings for the child process only. DatabaseOptions__* are cleared by default so a variable left in your
# shell from a local run can never redirect a QA/Production migration (environment variables override appsettings).
$settings = [ordered]@{
    DOTNET_ENVIRONMENT                  = $Environment
    DatabaseOptions__Provider           = $null
    DatabaseOptions__MigrationsAssembly = $null
    DatabaseOptions__ConnectionString   = $null
}

$settingsFile = Join-Path $apiProject.FullName "appsettings.$Environment.json"
$target = ''
if ($Environment -eq 'Development' -and $ConnectionString) {
    $migrationsAssembly = (Get-ChildItem $hostDir -Directory -Filter "*.Migrations.$Provider" | Select-Object -First 1).Name
    $settings.DatabaseOptions__Provider = $Provider
    $settings.DatabaseOptions__MigrationsAssembly = $migrationsAssembly
    $settings.DatabaseOptions__ConnectionString = $ConnectionString
    $target = Hide-Password $ConnectionString
}
else {
    # Development may inherit appsettings.json (the order the DbMigrator loads them in). QA and Production must
    # define their own connection: falling back to appsettings.json would point a deployed run at localhost.
    $files = @($settingsFile)
    if ($Environment -eq 'Development') { $files += Join-Path $apiProject.FullName 'appsettings.json' }
    $connection = $null
    foreach ($file in $files) {
        if (-not $connection -and (Test-Path $file)) {
            $connection = (Get-Content $file -Raw | ConvertFrom-Json).DatabaseOptions.ConnectionString
        }
    }
    if (-not $connection) {
        throw "DatabaseOptions:ConnectionString is not set for $Environment (looked in: $($files -join ', '))."
    }

    if ($connection.Contains($Placeholder)) {
        if ($DryRun) {
            Write-Host "The connection string contains $Placeholder; a real run would ask for the SQL password." -ForegroundColor DarkYellow
        }
        else {
            $user = Get-ConnectionPart $connection @('User Id', 'User ID', 'UID', 'User')
            $secure = Read-Host "SQL password for '$user' ($Environment)" -AsSecureString
            $plain = [System.Net.NetworkCredential]::new('', $secure).Password
            if (-not $plain) { throw 'No password given.' }
            # The completed string reaches the DbMigrator through its environment only; the file is not touched.
            $settings.DatabaseOptions__ConnectionString = $connection.Replace($Placeholder, $plain)
        }
    }

    $server = Get-ConnectionPart $connection @('Data Source', 'Server')
    $database = Get-ConnectionPart $connection @('Initial Catalog', 'Database')
    $target = "$server / $database"
}

$migratorArgs = @($Command)
if ($Seed) { $migratorArgs += '--seed' }
if ($Tenant) { $migratorArgs += @('--tenant', $Tenant) }
if ($CatalogOnly) { $migratorArgs += '--catalog-only' }

$color = @{ Development = 'Cyan'; QA = 'Yellow'; Production = 'Red' }[$Environment]
Write-Host "Target:   $Environment — $target" -ForegroundColor $color
Write-Host "Project:  $($migratorProject.Name)" -ForegroundColor $color
Write-Host "Command:  $($migratorArgs -join ' ')" -ForegroundColor $color

if ($DryRun) { Write-Host 'Dry run: nothing executed.'; exit 0 }

$readOnly = $Command -eq 'list-pending'
if ($Environment -eq 'QA' -and -not $Force -and -not $readOnly) {
    if ((Read-Host 'Type QA to continue') -cne 'QA') { Write-Host 'Cancelled.'; exit 1 }
}
if ($Environment -eq 'Production' -and -not $readOnly) {
    if ((Read-Host 'Type PRODUCTION to continue') -cne 'PRODUCTION') { Write-Host 'Cancelled.'; exit 1 }
}

# Null means "remove". Never pass $null to [Environment]::SetEnvironmentVariable: PowerShell turns it into "",
# .NET then keeps the variable with an empty value, and that empty value overrides appsettings.{Environment}.json.
function Set-ProcessVariable([string] $Name, [AllowNull()] [string] $Value) {
    if ([string]::IsNullOrEmpty($Value)) { Remove-Item "Env:\$Name" -ErrorAction SilentlyContinue }
    else { Set-Item "Env:\$Name" $Value }
}

$previous = @{}
foreach ($name in $settings.Keys) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name)
    Set-ProcessVariable $name $settings[$name]
}

try {
    & dotnet run --project $migratorProject.FullName -- @migratorArgs
    $exitCode = $LASTEXITCODE
}
finally {
    foreach ($name in $previous.Keys) {
        Set-ProcessVariable $name $previous[$name]
    }
}

if ($exitCode -ne 0) {
    Write-Host "DbMigrator failed (exit code $exitCode)." -ForegroundColor Red
}
exit $exitCode
