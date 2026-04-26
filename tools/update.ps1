param (
    [Parameter(Mandatory=$false)]
    [switch]$Build,
    [Parameter(Mandatory=$false)]
    [switch]$Publish,
    [Parameter(Mandatory=$false)]
    [switch]$Deploy,
    [Parameter(Mandatory=$false)]
    [switch]$RunOnce
)

$ErrorActionPreference = "Stop"

# Configuration
$ProjectDir = "P:\Visual Studio\source\repos\VRChatBioUpdater"
$DeployDir = "D:\OneDrive\Games\VRChat\_TOOLS\VRChatBioUpdater"
$ExecutableName = "VRChatBioUpdater.exe"

Write-Host "--- VRChat Bio Updater Automation Script ---" -ForegroundColor Cyan

# 1. Build (Debug)
if ($Build) {
    Write-Host "[*] Building Project (Debug)..." -ForegroundColor Yellow
    dotnet build "$ProjectDir" -c Debug
}

# 2. Publish (GitHub Release)
if ($Publish) {
    Write-Host "[*] Publishing Single-File Executable for Release..." -ForegroundColor Yellow
    $PublishDir = Join-Path $ProjectDir "bin\Release\net8.0\publish"
    
    # Ensure it's single file and trimmed if possible (adjusting for net8.0)
    dotnet publish "$ProjectDir" -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:PublishTrimmed=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o "$PublishDir"

    $exePath = Join-Path $PublishDir $ExecutableName
    
    if (Test-Path $exePath) {
        $Version = (Get-Item $exePath).VersionInfo.ProductVersion
        if ([string]::IsNullOrWhiteSpace($Version)) {
            $Version = Get-Date -Format "yyyy.MM.dd.HHmm"
        }
        $TagName = "v$Version"

        Write-Host "[*] Version: $Version" -ForegroundColor Gray
        Write-Host "[*] Tagging and Releasing $TagName..." -ForegroundColor Yellow

        # Git operations
        git add .
        git commit -m "Release $TagName"
        git tag $TagName
        git push origin main --tags

        # GH Release
        gh release create $TagName $exePath --title "Release $TagName" --notes "Automated release of version $Version"
        
        Write-Host "[+] GitHub Release Created Successfully!" -ForegroundColor Green
    } else {
        Write-Error "Failed to find published executable at $exePath"
    }
}

# 3. Deploy (Release)
if ($Deploy) {
    Write-Host "[*] Deploying to $DeployDir..." -ForegroundColor Yellow
    if (!(Test-Path $DeployDir)) {
        New-Item -ItemType Directory -Path $DeployDir -Force
    }
    
    # Try to stop existing process if it's running in the deploy dir
    $runningProc = Get-Process | Where-Object { $_.Path -like "$DeployDir*" }
    if ($runningProc) {
        Write-Host "[!] Stopping running instance..." -ForegroundColor Magenta
        $runningProc | Stop-Process -Force
        Start-Sleep -Seconds 1
    }

    dotnet publish "$ProjectDir" -c Release -o "$DeployDir" --self-contained false
}

# 4. RunOnce
if ($RunOnce) {
    Write-Host "[*] Running Application (Once)..." -ForegroundColor Green
    $exePath = Join-Path $DeployDir $ExecutableName
    if (Test-Path $exePath) {
        Start-Process -FilePath $exePath -ArgumentList "--once" -WorkingDirectory $DeployDir
    } else {
        Write-Error "Executable not found at $exePath"
    }
}

Write-Host "--- Done ---" -ForegroundColor Cyan
