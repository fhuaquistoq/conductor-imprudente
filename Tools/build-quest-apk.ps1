<#
.SYNOPSIS
    Compila el APK independiente de Meta Quest (Android, IL2CPP, ARM64, Vulkan).

.DESCRIPTION
    Lanza el Editor de Unity en batch mode con el Build Profile "Meta Quest" y ejecuta
    TaxiVR.Bootstrap.Editor.QuestApkBuilder.Build.

    El perfil entra por -activeBuildProfile, no se activa desde el metodo: Unity no puede cambiar de
    plataforma mientras ejecuta -executeMethod, y el perfil es ademas el unico sitio que aporta los
    scripting defines de Android (ENABLE_RUNTIME_OPTIMIZER, OVR_DISABLE_HAND_PINCH_BUTTON_MAPPING,
    USE_INPUT_SYSTEM_POSE_CONTROL, USE_STICK_CONTROL_THUMBSTICKS) y el nivel de calidad de Quest.
    Sin -activeBuildProfile el Editor compilaria el perfil activo (Windows) sobre una ruta .apk.

    Deja el APK en Builds/Quest/TaxiVR.apk, el resumen en Logs/TaxiQuestBuildResult.txt y el log completo
    del Editor en Logs/QuestBuild.log. Sale con codigo distinto de cero si la compilacion falla.

    Cierra cualquier instancia del Editor que tenga este proyecto abierto: en batch mode solo puede haber
    una instancia por proyecto.

.PARAMETER ProjectPath
    Raiz del proyecto Unity. Por defecto, la carpeta padre de este script.

.PARAMETER UnityPath
    Ruta a Unity.exe. Si se omite, usa la variable de entorno UNITY_PATH y, si no, la version declarada en
    ProjectSettings/ProjectVersion.txt dentro de la instalacion por defecto de Unity Hub.

.EXAMPLE
    pwsh -File Tools/build-quest-apk.ps1

.EXAMPLE
    pwsh -File Tools/build-quest-apk.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe"
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [string]$UnityPath,
    [string]$ApkPath = 'Builds/Quest/TaxiVR.apk',
    [string]$LogPath = 'Logs/QuestBuild.log'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ProfilePath = 'Assets/Settings/Build Profiles/Meta Quest.asset'
$BuildMethod = 'TaxiVR.Bootstrap.Editor.QuestApkBuilder.Build'
$SummaryPath = 'Logs/TaxiQuestBuildResult.txt'

function Get-UnityEditor {
    param([string]$ProjectRoot, [string]$Explicit)

    if ($Explicit) {
        if (-not (Test-Path -LiteralPath $Explicit)) { throw "No existe el Editor indicado en -UnityPath: $Explicit" }
        return (Resolve-Path -LiteralPath $Explicit).Path
    }
    if ($env:UNITY_PATH -and (Test-Path -LiteralPath $env:UNITY_PATH)) {
        return (Resolve-Path -LiteralPath $env:UNITY_PATH).Path
    }

    $versionFile = Join-Path $ProjectRoot 'ProjectSettings/ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $versionFile)) { throw "No encuentro $versionFile." }
    $version = (Select-String -LiteralPath $versionFile -Pattern '^m_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value

    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity",
        "$HOME/Unity/Hub/Editor/$version/Editor/Unity"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    throw "No encuentro el Editor de Unity $version. Pasa -UnityPath o define la variable UNITY_PATH."
}

$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$profileFile = Join-Path $ProjectPath $ProfilePath
if (-not (Test-Path -LiteralPath $profileFile)) { throw "Falta el Build Profile: $profileFile" }

$unity = Get-UnityEditor -ProjectRoot $ProjectPath -Explicit $UnityPath
$apkFile = Join-Path $ProjectPath $ApkPath
$logFile = Join-Path $ProjectPath $LogPath

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $apkFile) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logFile) | Out-Null
# El APK anterior se borra para que su presencia al final pruebe esta compilacion y no la anterior.
Remove-Item -LiteralPath $apkFile -Force -ErrorAction SilentlyContinue

Write-Host "Unity:   $unity"
Write-Host "Perfil:  $ProfilePath"
Write-Host "Destino: $ApkPath"
Write-Host ''

# Unity.exe es una aplicacion GUI: con el operador & PowerShell no la espera, seguiria leyendo un APK que
# aun no existe y $LASTEXITCODE quedaria sin definir. Start-Process -Wait si bloquea hasta que termina.
# Los argumentos van en una sola cadena porque Start-Process los une por espacios y las rutas de este
# proyecto llevan espacios ("Conductor Imprudente", "Meta Quest.asset").
$argumentLine = '-batchmode -quit -accept-apiupdate' +
    ' -projectPath "' + $ProjectPath + '"' +
    ' -activeBuildProfile "' + $ProfilePath + '"' +
    ' -executeMethod ' + $BuildMethod +
    ' -logFile "' + $logFile + '"'

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$editor = Start-Process -FilePath $unity -ArgumentList $argumentLine -NoNewWindow -Wait -PassThru
$exitCode = $editor.ExitCode
$stopwatch.Stop()

$summaryFile = Join-Path $ProjectPath $SummaryPath
if (Test-Path -LiteralPath $summaryFile) { Get-Content -LiteralPath $summaryFile | Write-Host }

if ($exitCode -ne 0) {
    Write-Host ''
    Write-Host "Compilacion fallida (codigo de salida $exitCode). Ultimas lineas de ${LogPath}:" -ForegroundColor Red
    if (Test-Path -LiteralPath $logFile) { Get-Content -LiteralPath $logFile -Tail 40 | Write-Host }
    exit $exitCode
}

if (-not (Test-Path -LiteralPath $apkFile)) {
    Write-Host "Unity termino sin error pero no hay APK en $ApkPath. Mirate ${LogPath}." -ForegroundColor Red
    exit 1
}

$apk = Get-Item -LiteralPath $apkFile
$hash = (Get-FileHash -LiteralPath $apkFile -Algorithm SHA256).Hash
Write-Host ''
Write-Host ("APK listo: {0} ({1:N1} MB en {2:N1} s)" -f $ApkPath, ($apk.Length / 1MB), $stopwatch.Elapsed.TotalSeconds) -ForegroundColor Green
Write-Host "SHA256: $hash"
Write-Host "Log:    $LogPath"
Write-Host ''
Write-Host 'Para instalarlo en el Quest por USB:'
Write-Host "  adb install -r `"$ApkPath`""
