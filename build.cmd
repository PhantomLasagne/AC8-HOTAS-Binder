@echo off
rem Builds AC8-HOTAS-Binder.exe with the C# compiler included in Windows (.NET Framework 4.x).
rem No SDK or Visual Studio needed.
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Could not find csc.exe from .NET Framework 4.
    exit /b 1
)
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
    /win32icon:"%~dp0src\icon.ico" /win32manifest:"%~dp0src\app.manifest" ^
    /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
    /out:"%~dp0AC8-HOTAS-Binder.exe" "%~dp0src\Binder.cs"
if errorlevel 1 exit /b 1
echo Built %~dp0AC8-HOTAS-Binder.exe
