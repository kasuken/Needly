<#
.SYNOPSIS
    Runs a GO-batched T-SQL script against Azure SQL using a Microsoft Entra access token.
.DESCRIPTION
    Intended for Windows PowerShell 5.1, whose System.Data.SqlClient supports token authentication
    without requiring an extra SQL client to be installed.
#>
param(
    [Parameter(Mandatory = $true)][string] $ServerFqdn,
    [Parameter(Mandatory = $true)][string] $Database,
    [Parameter(Mandatory = $true)][string] $AccessToken,
    [Parameter(Mandatory = $true)][string] $ScriptPath
)

$ErrorActionPreference = 'Stop'

$batches = [regex]::Split((Get-Content -Path $ScriptPath -Raw), '(?im)^\s*GO\s*$') |
    Where-Object { $_.Trim().Length -gt 0 }

$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = "Server=tcp:$ServerFqdn,1433;Initial Catalog=$Database;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60"
$connection.AccessToken = $AccessToken
$connection.Open()

try {
    foreach ($batch in $batches) {
        $command = $connection.CreateCommand()
        $command.CommandText = $batch
        $command.CommandTimeout = 120
        [void]$command.ExecuteNonQuery()
    }

    Write-Host "Applied $($batches.Count) batch(es) to $Database."
}
finally {
    $connection.Close()
}
