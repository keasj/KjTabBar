param([Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $root "artifacts/releases/v$Version"
$staging = Join-Path $root ("artifacts/package-validation/" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $staging | Out-Null
$installer = New-Object -ComObject WindowsInstaller.Installer
$expectedMsiVersion = ($Version.Split('.')[0..2] -join '.')
$payloads = @{}
try {
    foreach ($language in @('ja','en')) {
        $folder = if ($language -eq 'ja') { 'Release' } else { 'Release_en' }
        $source = Join-Path $root "Setup/$folder"
        $msi = Join-Path $source 'Setup.msi'
        $launcher = Join-Path $source 'setup.exe'
        $bytes = [IO.File]::ReadAllBytes($launcher)
        $embedded = [Text.Encoding]::UTF8.GetString($bytes) + [Text.Encoding]::Unicode.GetString($bytes)
        if ($embedded -notmatch 'Name="Setup\.msi"') { throw "Unexpected launcher MSI name: $launcher" }
        $db = $installer.OpenDatabase($msi, 0)
        try {
            $view = $db.OpenView('SELECT `Value` FROM `Property` WHERE `Property`=''ProductVersion''')
            $view.Execute(); $record = $view.Fetch()
            if ($record.StringData(1) -ne $expectedMsiVersion) { throw "Wrong MSI version: $msi" }
            $view.Close()
        } finally { [Runtime.InteropServices.Marshal]::ReleaseComObject($db) | Out-Null }
        $extract = Join-Path $staging $language
        $log = Join-Path $staging "$language.log"
        $process = Start-Process msiexec.exe -ArgumentList @('/a',('"'+$msi+'"'),'/qn',('TARGETDIR="'+$extract+'"'),'/l*v',('"'+$log+'"')) -PassThru -WindowStyle Hidden
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "MSI extraction failed ($($process.ExitCode)): $log" }
        $payload = @(Get-ChildItem $extract -Recurse -Filter KjTabBar.exe)
        if ($payload.Count -ne 1 -or $payload[0].VersionInfo.FileVersion -ne $Version) { throw "Wrong payload version: $msi" }
        $payloads[$language] = $payload[0].FullName
        $package = Join-Path $staging "$language-package"
        New-Item -ItemType Directory -Force $package | Out-Null
        Copy-Item $msi (Join-Path $package 'Setup.msi')
        Copy-Item $launcher (Join-Path $package 'setup.exe')
        Copy-Item (Join-Path $root 'LICENSE') $package
    }
} finally { [Runtime.InteropServices.Marshal]::ReleaseComObject($installer) | Out-Null }
New-Item -ItemType Directory -Force $destination | Out-Null
foreach ($language in @('ja','en')) {
    $package = Join-Path $staging "$language-package"
    $target = Join-Path $destination $language
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $package '*') $target -Force
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath (Join-Path $destination "KjTabBar-v$Version-$language.zip") -Force
    Write-Output "$language payload SHA256=$((Get-FileHash $payloads[$language]).Hash)"
}
Copy-Item $payloads['en'] (Join-Path $destination 'KjTabBar.exe') -Force
Copy-Item ($payloads['en'] + '.config') (Join-Path $destination 'KjTabBar.exe.config') -Force
Copy-Item (Join-Path $root 'LICENSE') $destination -Force
$manifest = Get-ChildItem $destination -Recurse -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object FullName | ForEach-Object {
    (Get-FileHash $_.FullName).Hash + '  ' + $_.FullName.Substring($destination.Length + 1)
}
[IO.File]::WriteAllLines((Join-Path $destination 'SHA256SUMS.txt'), $manifest, [Text.UTF8Encoding]::new($false))
Write-Output "Release package: $destination"
