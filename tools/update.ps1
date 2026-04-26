param (
    [Parameter(Mandatory=$false)]
    [switch]$Build,
    [Parameter(Mandatory=$false)]
    [switch]$Publish,
    [Parameter(Mandatory=$false)]
    [switch]$Deploy,
    [Parameter(Mandatory=$false)]
    [switch]$RunOnce,
    [Parameter(Mandatory=$false)]
    [switch]$Bump,
    [Parameter(Mandatory=$false)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

# Configuration
$ProjectDir = "P:\Visual Studio\source\repos\VRChatBioUpdater"
$DeployDir = "D:\OneDrive\Games\VRChat\_TOOLS\VRChatBioUpdater"
$ExecutableName = "VRChatBioUpdater.exe"
$AssemblyInfoPath = Join-Path $ProjectDir "Properties\AssemblyInfo.cs"

Write-Host "--- VRChat Bio Updater Automation Script ---" -ForegroundColor Cyan

# Versioning Logic
if ($Bump -or $Version) {
    $content = Get-Content $AssemblyInfoPath -Raw
    $currentVersionMatch = [regex]::Match($content, '\[assembly: AssemblyVersion\("(?<version>.*?)"\)\]')
    $currentVersion = $currentVersionMatch.Groups["version"].Value
    
    $newVersion = $Version
    if ($Bump) {
        $parts = $currentVersion.Split('.')
        $parts[$parts.Length - 1] = [int]$parts[$parts.Length - 1] + 1
        $newVersion = [string]::Join('.', $parts)
    }

    if ($newVersion -and $newVersion -ne $currentVersion) {
        Write-Host "[*] Bumping Version: $currentVersion -> $newVersion" -ForegroundColor Yellow
        $content = $content -replace '\[assembly: AssemblyVersion\(".*?"\)\]', "[assembly: AssemblyVersion(""$newVersion"")]"
        $content = $content -replace '\[assembly: AssemblyFileVersion\(".*?"\)\]', "[assembly: AssemblyFileVersion(""$newVersion"")]"
        $content | Set-Content $AssemblyInfoPath
    }
}

# 1. Build (Debug)
if ($Build) {
    Write-Host "[*] Building Project (Debug) with Zero Warning Enforcement..." -ForegroundColor Yellow
    $buildOutput = dotnet build "$ProjectDir" -c Debug 2>&1
    
    $hasErrors = $buildOutput | Where-Object { $_ -match "Error\(s\)" -and $_ -notmatch " 0 Error\(s\)" }
    $hasWarnings = $buildOutput | Where-Object { $_ -match "Warning\(s\)" -and $_ -notmatch " 0 Warning\(s\)" }

    if ($hasErrors -or $hasWarnings) {
        $buildOutput | Out-Host
        Write-Error "Build failed enforcement: Warnings or Errors detected. Please fix all issues before deploying/publishing."
        exit 1
    }
    Write-Host "[+] Build clean (0 Errors, 0 Warnings)." -ForegroundColor Green
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
    Write-Host "[*] Running Application (Once) with logging..." -ForegroundColor Green
    $exePath = Join-Path $DeployDir $ExecutableName
    $logPath = Join-Path $DeployDir "last_run.log"
    
    if (Test-Path $exePath) {
        # Run and wait for completion, capturing both stdout and stderr to the same file
        cmd /c "`"$exePath`" --once > `"$logPath`" 2>&1"
        
        Write-Host "[*] Run complete. Output:" -ForegroundColor Cyan
        if (Test-Path $logPath) {
            Get-Content $logPath | Out-Host
        }
    } else {
        Write-Error "Executable not found at $exePath"
    }
}

Write-Host "--- Done ---" -ForegroundColor Cyan
