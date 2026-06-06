$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$targetExtensions = @('.cs', '.csproj', '.xaml', '.sln', '.slnx', '.md')
$skipDirectoryNames = @('.git', '.vs', 'bin', 'obj', 'TestResults')
$violations = New-Object System.Collections.Generic.List[string]

function Test-SkipPath {
    param(
        [string]$Path
    )

    $segments = $Path.Split([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    foreach ($segment in $segments) {
        foreach ($skipDirectoryName in $skipDirectoryNames) {
            if ($segment.Equals($skipDirectoryName, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
    }

    return $false
}

function Get-LineEndingState {
    param(
        [string]$Path
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasCrLf = $false
    $hasLfOnly = $false

    for ($index = 0; $index -lt $bytes.Length; $index++) {
        if ($bytes[$index] -ne 10) {
            continue
        }

        if ($index -gt 0 -and $bytes[$index - 1] -eq 13) {
            $hasCrLf = $true
        }
        else {
            $hasLfOnly = $true
        }

        if ($hasCrLf -and $hasLfOnly) {
            break
        }
    }

    return @{
        HasCrLf = $hasCrLf
        HasLfOnly = $hasLfOnly
    }
}

foreach ($filePath in [System.IO.Directory]::EnumerateFiles($repositoryRoot, '*', [System.IO.SearchOption]::AllDirectories)) {
    if (Test-SkipPath -Path $filePath) {
        continue
    }

    $extension = [System.IO.Path]::GetExtension($filePath)
    if (-not $targetExtensions.Contains($extension)) {
        continue
    }

    $lineEndingState = Get-LineEndingState -Path $filePath
    if ($lineEndingState.HasLfOnly) {
        $relativePath = $filePath.Substring($repositoryRoot.Length).TrimStart('\')
        $violations.Add($relativePath + ' (CRLF=' + $lineEndingState.HasCrLf + ', LF-only=' + $lineEndingState.HasLfOnly + ')')
    }
}

if ($violations.Count -gt 0) {
    Write-Error ("LF-only line endings detected in CRLF-managed files:`r`n" + ($violations -join "`r`n"))
}
