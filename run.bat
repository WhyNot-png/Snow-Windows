@echo off
chcp 65001 >nul
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo Не найден .NET SDK 8 или новее. Установите SDK, а не только Runtime:
  echo https://dotnet.microsoft.com/download/dotnet/8.0
  echo После установки закройте и снова запустите этот файл.
  pause
  exit /b 1
)
echo Запускаем Snow Windows. Первый запуск может занять несколько минут.
dotnet run --project SnowWindows\SnowWindows.csproj -c Release
if errorlevel 1 (
  echo Не удалось собрать или запустить приложение. Скопируйте текст ошибки.
  pause
)
