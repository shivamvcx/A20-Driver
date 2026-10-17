@echo off
echo ===================================================================
echo   Building Redgear A20 Control Center v0.2.0 (64-bit Windows)
echo ===================================================================
echo.

if exist "release" goto HAS_RELEASE
mkdir "release"
:HAS_RELEASE

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
if not exist "%CSC%" (
    echo [ERROR] .NET Framework 4.0 csc.exe compiler not found under %SystemRoot%\Microsoft.NET\
    exit /b 1
)

echo Compiling src\Program.cs for x64 architecture...
"%CSC%" /target:winexe /platform:x64 /debug- /optimize /nowarn:169,649 /win32manifest:"src\app.manifest" /out:"release\RedgearA20-win-x64.exe" /reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll "src\Program.cs"

if %ERRORLEVEL% equ 0 (
    echo.
    echo [OK] Executable created: release\RedgearA20-win-x64.exe
    echo.
    echo Generating SHA-256 checksum...
    certutil -hashfile "release\RedgearA20-win-x64.exe" SHA256 > "release\RedgearA20-win-x64.sha256"
    echo [OK] Checksum saved: release\RedgearA20-win-x64.sha256
    echo.
    echo ===================================================================
    echo   [SUCCESS] Build Complete! Artifacts ready in release\ folder.
    echo ===================================================================
    exit /b 0
) else (
    echo.
    echo [ERROR] Compilation failed.
    exit /b 1
)
