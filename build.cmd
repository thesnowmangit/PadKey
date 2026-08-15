@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe

"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /utf8output ^
  /win32icon:"%~dp0padkey.ico" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /out:"%~dp0padkey.exe" "%~dp0PadKey.cs"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo OK: %~dp0padkey.exe
