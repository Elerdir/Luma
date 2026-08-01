@echo off
setlocal

rem ============================================================================
rem  Builds and runs Luma from source, for testing.
rem
rem    run.bat                     start with no media
rem    run.bat "D:\video\clip.mkv" start with a file open
rem    run.bat --release           run a Release build instead of Debug
rem
rem  This runs the app straight out of the build output. It does not install
rem  anything and does not touch an installed copy — use instalator.bat for that.
rem  Settings still live in %APPDATA%\Luma, so a test run shares preferences with
rem  an installed build.
rem ============================================================================

cd /d "%~dp0"

set "CONFIG=Debug"
set "MEDIA="

if /i "%~1"=="--release" (
    set "CONFIG=Release"
    set "MEDIA=%~2"
) else (
    set "MEDIA=%~1"
)

echo.
echo === Luma (%CONFIG%) ========================================
if not "%MEDIA%"=="" echo  opening : %MEDIA%
echo ============================================================
echo.

if "%MEDIA%"=="" (
    dotnet run --project src\Luma.Presentation\Luma.Presentation.csproj --configuration %CONFIG%
) else (
    dotnet run --project src\Luma.Presentation\Luma.Presentation.csproj --configuration %CONFIG% -- "%MEDIA%"
)

if errorlevel 1 (
    echo.
    echo [ERROR] Luma exited with an error. See the output above.
    exit /b 1
)

endlocal
