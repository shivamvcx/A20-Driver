@echo off
echo ===================================================================
echo   Building Redgear A20 Control Center v0.1.1 (64-bit Windows)
echo ===================================================================
echo.

if exist "release" goto HAS_RELEASE
mkdir "release"
:HAS_RELEASE

echo Compiling src\Program.cs for x64 architecture...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /platform:x64 /debug- /optimize /nowarn:169,649 /out:"release\RedgearA20-win-x64.exe" /reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll "src\Program.cs"

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
) else (
    echo.
    echo [ERROR] Compilation failed.
)
