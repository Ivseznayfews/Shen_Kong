@echo off
setlocal
set "ROOT=%~dp0"
where dotnet >nul 2>nul || (echo Install .NET SDK 8 or later first. & exit /b 1)
if "%~1"=="" (
  dotnet build "%ROOT%BedCheck\BedCheck.csproj" -c Release
) else (
  dotnet build "%ROOT%BedCheck\BedCheck.csproj" -c Release "-p:GameDir=%~1"
)
if errorlevel 1 exit /b 1
if exist "%ROOT%dist\bedcheck" rmdir /s /q "%ROOT%dist\bedcheck"
mkdir "%ROOT%dist\bedcheck\Localization"
copy /y "%ROOT%BedCheck\manifest.xml" "%ROOT%dist\bedcheck\" >nul
copy /y "%ROOT%BedCheck\bin\Release\netstandard2.1\BedCheck.dll" "%ROOT%dist\bedcheck\" >nul
copy /y "%ROOT%BedCheck\Localization\*.csv" "%ROOT%dist\bedcheck\Localization\" >nul
echo Build package ready: %ROOT%dist\bedcheck
endlocal
