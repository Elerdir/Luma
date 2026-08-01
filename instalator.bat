@echo off
setlocal enabledelayedexpansion

rem ============================================================================
rem  Builds a Windows MSI installer for Luma.
rem
rem    instalator.bat            build the MSI at the version in Directory.Build.props
rem    instalator.bat 1.2.0      build the MSI at an explicit version
rem
rem  Output: dist\Luma-<version>-x64.msi
rem
rem  Requires the WiX CLI. If it is missing the script installs it.
rem
rem  WiX is pinned to 5.x on purpose. From v6 onwards WiX is gated behind the Open
rem  Source Maintenance Fee EULA, which is a licensing decision for whoever ships
rem  this - not something a build script should accept on their behalf. WiX 5 is
rem  MS-RL and needs no agreement. Raise WIX_VERSION yourself if you have accepted
rem  the OSMF terms.
rem ============================================================================

cd /d "%~dp0"

set "VERSION=%~1"
if "%VERSION%"=="" (
    rem Read the single source of the version out of Directory.Build.props.
    rem Parsed as XML rather than by text matching: a "for /f delims=" split shifts
    rem with the line's indentation, and angle brackets and pipes inside a for /f
    rem command are eaten by cmd's own redirection parsing.
    for /f "delims=" %%v in ('powershell -NoProfile -Command "@(([xml](Get-Content 'Directory.Build.props' -Raw)).Project.PropertyGroup.Version)[0]"') do (
        set "VERSION=%%v"
    )
)
if "%VERSION%"=="" (
    echo [ERROR] Could not determine the version. Pass one explicitly: instalator.bat 1.2.0
    exit /b 1
)

set "WIX_VERSION=5.0.2"
set "RID=win-x64"
set "PUBLISH_DIR=%CD%\artifacts\publish\%RID%"
set "OUTPUT_DIR=%CD%\dist"
set "MSI_PATH=%OUTPUT_DIR%\Luma-%VERSION%-x64.msi"

echo.
echo === Luma installer =========================================
echo  version : %VERSION%
echo  runtime : %RID%
echo  output  : %MSI_PATH%
echo ============================================================
echo.

rem --- WiX -------------------------------------------------------------------
where wix >nul 2>&1
if errorlevel 1 (
    echo [1/4] WiX not found, installing %WIX_VERSION% globally...
    dotnet tool install --global wix --version %WIX_VERSION%
    if errorlevel 1 (
        echo [ERROR] Could not install the WiX CLI.
        exit /b 1
    )
    rem The tools directory is only on PATH for new shells, so reach it directly.
    set "PATH=%USERPROFILE%\.dotnet\tools;%PATH%"
) else (
    echo [1/4] WiX found.
)

rem --- Publish ---------------------------------------------------------------
rem Self-contained: the installed app must not require a .NET runtime on the machine.
echo [2/4] Publishing %RID% (self-contained)...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
dotnet publish src\Luma.Presentation\Luma.Presentation.csproj ^
    --configuration Release ^
    --runtime %RID% ^
    --self-contained true ^
    -p:Version=%VERSION% ^
    -p:PublishSingleFile=false ^
    --output "%PUBLISH_DIR%"
if errorlevel 1 (
    echo [ERROR] Publish failed.
    exit /b 1
)

if not exist "%PUBLISH_DIR%\Luma.exe" (
    echo [ERROR] Publish did not produce Luma.exe in %PUBLISH_DIR%.
    exit /b 1
)

rem --- Package ---------------------------------------------------------------
echo [3/4] Building the MSI...
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

wix build installer\Luma.wxs ^
    -arch x64 ^
    -define Version=%VERSION% ^
    -define PublishDir=%PUBLISH_DIR% ^
    -define IconFile=%CD%\src\Luma.Presentation\Assets\luma.ico ^
    -out "%MSI_PATH%"
if errorlevel 1 (
    echo [ERROR] WiX build failed.
    exit /b 1
)

rem --- Done ------------------------------------------------------------------
echo [4/4] Done.
echo.
for %%f in ("%MSI_PATH%") do echo  %%~nxf  (%%~zf bytes)
echo  %MSI_PATH%
echo.
endlocal
