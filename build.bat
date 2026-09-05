@echo off
setlocal
cd /d "%~dp0"

echo === Display Profile Switcher ===
where dotnet >nul 2>&1
if errorlevel 1 (
    echo.
    echo No se ha encontrado .NET SDK 8.
    echo Instala el SDK de .NET 8 y vuelve a ejecutar este archivo.
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

dotnet restore "src\DisplayProfileSwitcher\DisplayProfileSwitcher.csproj"
if errorlevel 1 goto :error

dotnet publish "src\DisplayProfileSwitcher\DisplayProfileSwitcher.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "publish"
if errorlevel 1 goto :error

echo.
echo Compilado correctamente:
echo %CD%\publish\DisplayProfileSwitcher.exe
pause
exit /b 0

:error
echo.
echo ERROR al compilar.
pause
exit /b 1
