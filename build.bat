@echo off
rem Baut Sensensturm.exe mit dem in Windows enthaltenen .NET-Framework-Compiler (keine Installation n”tig).
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /optimize+ /target:winexe /platform:anycpu /out:Sensensturm.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /resource:assets\stage_blossom.jpg,Sensensturm.assets.stage_blossom.jpg /resource:assets\stage_autumn.jpg,Sensensturm.assets.stage_autumn.jpg /resource:assets\stage_grove.jpg,Sensensturm.assets.stage_grove.jpg src\*.cs
if errorlevel 1 (
  echo.
  echo BUILD FEHLGESCHLAGEN
  pause
  exit /b 1
)
echo Fertig: Sensensturm.exe
