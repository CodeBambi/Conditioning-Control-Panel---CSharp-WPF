param(
    [Parameter(Mandatory = $true)]
    [string[]]$ImagePath,

    [Parameter(Mandatory = $true)]
    [string]$Prompt,

    [string]$WorkingDirectory = (Get-Location).Path,

    [ValidateSet('minimal', 'low', 'medium', 'high', 'xhigh')]
    [string]$Thinking = 'high',

    [string]$SessionName = 'visual-review-k3'
)

$ErrorActionPreference = 'Stop'

$resolvedImages = foreach ($path in $ImagePath) {
    $resolved = (Resolve-Path -LiteralPath $path).Path
    $extension = [System.IO.Path]::GetExtension($resolved).ToLowerInvariant()
    if ($extension -notin @('.png', '.jpg', '.jpeg', '.gif', '.webp')) {
        throw "Unsupported image extension: $extension ($resolved)"
    }
    $resolved
}

if (-not (Get-Command pi -ErrorAction SilentlyContinue)) {
    throw 'Pi is not available on PATH.'
}

$arguments = @(
    '--provider', 'kimi-coding',
    '--model', 'k3',
    '--thinking', $Thinking,
    '--name', $SessionName,
    '-p'
)

foreach ($image in $resolvedImages) {
    $arguments += "@$image"
}
$arguments += $Prompt

Push-Location $WorkingDirectory
try {
    & pi @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Pi visual review failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
