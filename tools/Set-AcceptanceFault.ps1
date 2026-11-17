param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][ValidateRange(1, 2147483647)][int]$MainProcessId,
    [Parameter(Mandatory = $true)]
    [ValidateSet('reject', 'throw', 'drop', 'delay', 'late-throw', 'rollback-reject', 'pause-sync', 'resume-sync', 'grace', 'end-grace')]
    [string]$Action,
    [ValidateRange(1, 30000)][int]$DelayMs = 3000
)
$ErrorActionPreference = 'Stop'
if (-not [IO.Path]::IsPathRooted($Root)) { throw 'Root must be an absolute test directory.' }
$directory = Join-Path ([IO.Path]::GetFullPath($Root)) ([string]$MainProcessId)
[IO.Directory]::CreateDirectory($directory) | Out-Null
$name = 'navigate.txt'
$content = $Action
switch ($Action) {
    'delay' { $content = 'delay:' + $DelayMs }
    'late-throw' { $content = 'late-throw:' + $DelayMs }
    'rollback-reject' { $name = 'rollback.txt'; $content = 'reject' }
    'pause-sync' { $name = 'sync-pause.flag'; $content = '' }
    'resume-sync' { $name = 'sync-pause.flag' }
    'grace' { $name = 'grace.flag'; $content = '' }
    'end-grace' { $name = 'grace.flag' }
}
$file = Join-Path $directory $name
if ($Action -eq 'resume-sync' -or $Action -eq 'end-grace') {
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
} else {
    # Publish a complete command atomically; an already armed command is not overwritten.
    if (Test-Path -LiteralPath $file) { throw "Already armed: $file. Consume or remove it before rearming." }
    $temporary = Join-Path $directory ([Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, $content, [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporary, $file)
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
}
Write-Output "$Action : $file"
