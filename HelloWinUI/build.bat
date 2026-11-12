@echo off
setlocal
cd /d "%~dp0"

set "PATH=%PATH%;C:\Program Files (x86)\Microsoft Visual Studio\Installer"
call "E:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

rem Find the installed Windows App Runtime 1.8 (x64) folder, which holds the WinUI .winmd metadata
for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command "(Get-AppxPackage Microsoft.WindowsAppRuntime.1.8 | Where-Object Architecture -eq X64 | Sort-Object Version -Descending | Select-Object -First 1).InstallLocation"`) do set "WAR=%%i"
if not defined WAR (echo Windows App Runtime 1.8 not found & exit /b 1)

rem Generate C++/WinRT headers for WinUI from that metadata (only the first time)
if not exist generated\winrt\Microsoft.UI.Xaml.h (
  if not exist obj mkdir obj
  "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\midlrt.exe" /nologo /winrt /nomidl /h nul ^
    /metadata_dir "C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.22621.0" ^
    /reference "C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.22621.0\Windows.winmd" ^
    /winmd obj\Microsoft.Web.WebView2.Core.winmd WebView2Stub.idl || exit /b 1
  "C:\Program Files (x86)\Windows Kits\10\bin\10.0.28000.0\x64\cppwinrt.exe" ^
    -in obj\Microsoft.Web.WebView2.Core.winmd ^
    -in "%WAR%\Microsoft.UI.Xaml.winmd" "%WAR%\Microsoft.UI.winmd" "%WAR%\Microsoft.UI.Text.winmd" ^
        "%WAR%\Microsoft.Foundation.winmd" "%WAR%\Microsoft.Graphics.winmd" "%WAR%\Microsoft.Windows.ApplicationModel.Resources.winmd" ^
    -ref sdk -out generated || exit /b 1
)

if not exist bin mkdir bin
cl /nologo /std:c++20 /EHsc /permissive- /O2 /MD /utf-8 /DUNICODE /D_UNICODE /Igenerated ^
   main.cpp /Fobin\ /Febin\HelloWinUI.exe ^
   /link /SUBSYSTEM:WINDOWS windowsapp.lib user32.lib /MANIFEST:EMBED /MANIFESTINPUT:app.manifest || exit /b 1

echo Built bin\HelloWinUI.exe
