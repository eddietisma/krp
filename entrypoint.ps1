$dnsServers = @("8.8.8.8", "1.1.1.1")

# Wait for the network interface
$adapter = $null
for ($i = 0; $i -lt 30; $i++) {
    $adapter = Get-DnsClient | Where-Object { $_.InterfaceAlias -like "Ethernet*" }
    if ($adapter) { break }
    Start-Sleep -Seconds 1
}

if ($adapter) {
    try {
        $ifaceName = $adapter.InterfaceAlias
        Set-DnsClientServerAddress -InterfaceAlias $ifaceName -ServerAddresses $dnsServers
        Write-Host "Successfully set DNS servers to $dnsServers on interface '$ifaceName'"
    } catch {
        Write-Warning "Failed to set DNS servers: $_"
    }
} else {
    Write-Warning "No matching network interface found. Skipping DNS override."
}

# ⚠️ DO NOT use Start-Process; run it in foreground
Write-Host "Starting Krp.exe..."
& "C:\app\Krp.exe"
