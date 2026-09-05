param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$subkey = 'Software\Mozilla\NativeMessagingHosts\softlight_firefox'
if ($Remove) {
    foreach($view in @([Microsoft.Win32.RegistryView]::Registry32,[Microsoft.Win32.RegistryView]::Registry64)) {
        $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
        try {$base.DeleteSubKey($subkey,$false)} finally {$base.Dispose()}
    }
    return
}
$manifest = Join-Path $PSScriptRoot 'bridge/softlight_firefox.json'
New-Item -ItemType Directory -Path (Split-Path $manifest) -Force | Out-Null
@{name='softlight_firefox';description='Softlight Firefox video geometry bridge';path=(Join-Path $PSScriptRoot 'Softlight.FirefoxHost.exe');type='stdio';allowed_extensions=@('firefox@softlight.artllex')} | ConvertTo-Json | Set-Content $manifest -Encoding ASCII
foreach($view in @([Microsoft.Win32.RegistryView]::Registry32,[Microsoft.Win32.RegistryView]::Registry64)) {
    $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
    try {
        $entry=$base.CreateSubKey($subkey)
        try {$entry.SetValue('', $manifest,[Microsoft.Win32.RegistryValueKind]::String)} finally {$entry.Dispose()}
    } finally {$base.Dispose()}
}
Write-Output 'Firefox native host registered for the current user.'
