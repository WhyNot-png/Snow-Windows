@echo off
chcp 65001 >nul
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo Сначала установите .NET SDK 8 или новее: https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
echo Собираем приложение для 64-битной Windows...
dotnet publish SnowWindows\SnowWindows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
if errorlevel 1 (
  echo Не удалось собрать приложение. Скопируйте текст ошибки.
) else (
  echo Готово: publish\SnowWindows.exe
)
pause
