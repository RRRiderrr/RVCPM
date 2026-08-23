@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo ================================================
echo  RVCPM - Release build (.NET Framework 4.7.2)
echo ================================================
echo.

set "MSBUILD="
where msbuild.exe >nul 2>nul && for /f "delims=" %%I in ('where msbuild.exe') do if not defined MSBUILD set "MSBUILD=%%I"

if not defined MSBUILD (
    set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
    if exist "%VSWHERE%" (
        for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do if not defined MSBUILD set "MSBUILD=%%I"
    )
)

if not defined MSBUILD (
    echo [ERROR] MSBuild was not found.
    echo Install Visual Studio 2022 or Build Tools with the ".NET desktop development" workload
    echo and the .NET Framework 4.7.2 targeting pack.
    pause
    exit /b 1
)

echo MSBuild: %MSBUILD%
echo.

"%MSBUILD%" "RVCPM.sln" /restore /m /t:Rebuild /p:Configuration=Release /p:Platform="Any CPU"
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

if exist "dist\RVCPM" rmdir /s /q "dist\RVCPM"
mkdir "dist\RVCPM" >nul 2>nul
xcopy /e /i /y "RVCPM\bin\Release\*" "dist\RVCPM\" >nul

if not exist "dist\RVCPM\RVCPM.exe" (
    echo [ERROR] Build reported success but RVCPM.exe was not found.
    pause
    exit /b 1
)

echo.
echo [OK] Build complete:
echo %CD%\dist\RVCPM\RVCPM.exe
echo.
pause
