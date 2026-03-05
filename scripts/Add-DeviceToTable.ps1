# Adds a test device to the Devices table so pin-reset works.
# Run from repo root: .\scripts\Add-DeviceToTable.ps1
# Requires: Install-Module AzTable -Scope CurrentUser

param(
    [string]$DeviceId = "device-001",
    [string]$ParentEmail = "your-email@example.com"
)

$settingsPath = Join-Path $PSScriptRoot "..\DigiCompassCloudRelay\local.settings.json"
if (-not (Test-Path $settingsPath)) {
    Write-Error "Not found: $settingsPath"
    exit 1
}

$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
$conn = $settings.Values.TABLES_CONNECTION_STRING
if (-not $conn) {
    Write-Error "TABLES_CONNECTION_STRING not in local.settings.json"
    exit 1
}

$parts = @{}
foreach ($pair in $conn.Split(';')) {
    if ($pair -match '^([^=]+)=(.*)$') { $parts[$matches[1]] = $matches[2] }
}
$accountName = $parts["AccountName"]
$accountKey   = $parts["AccountKey"]
if (-not $accountName -or -not $accountKey) {
    Write-Error "Could not parse AccountName/AccountKey from connection string"
    exit 1
}

if (-not (Get-Module -ListAvailable AzTable)) {
    Write-Host "Installing AzTable module..."
    Install-Module -Name AzTable -Scope CurrentUser -Force
}

Import-Module AzTable -ErrorAction Stop

$ctx = New-AzStorageContext -StorageAccountName $accountName -StorageAccountKey $accountKey
$table = Get-AzStorageTable -Name "Devices" -Context $ctx -ErrorAction SilentlyContinue
if (-not $table) {
    New-AzStorageTable -Name "Devices" -Context $ctx | Out-Null
    $table = Get-AzStorageTable -Name "Devices" -Context $ctx
}

$partitionKey = "device:$DeviceId"
$rowKey       = "v1"

Add-AzTableRow -Table $table.CloudTable -PartitionKey $partitionKey -RowKey $rowKey -property @{
    ParentEmail = $ParentEmail
}

Write-Host "Added device: PartitionKey=$partitionKey RowKey=$rowKey ParentEmail=$ParentEmail"
