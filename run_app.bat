@echo off
setlocal

rem Usage: run_app.bat [publishOutputFolder]
rem If no output folder is provided, it will default to C:\Websites\Home\Connect
set "PUBLISH_PATH=%~1"
if "%PUBLISH_PATH%"=="" set "PUBLISH_PATH=C:\Websites\Home\Connect"

echo ===========================================
echo Building Connect Internal Communication App
echo ===========================================
echo.

echo [1/3] Building Angular Client...
cd team-app-client
call npx -p @angular/cli ng build --configuration production
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Angular build failed!
    cd ..
    exit /b %errorlevel%
)
cd ..
echo.

echo [2/3] Moving Build to API wwwroot...
if exist "TeamApp.API\wwwroot" rmdir /s /q "TeamApp.API\wwwroot"
mkdir "TeamApp.API\wwwroot"

xcopy "team-app-client\dist\team-app-client\browser\*" "TeamApp.API\wwwroot\" /E /I /Y > nul
echo Done copying files.
echo.

echo [3/3] Publishing .NET API to %PUBLISH_PATH%...
cd TeamApp.API
if exist "%PUBLISH_PATH%" (
    echo Cleaning existing publish folder...
    rmdir /s /q "%PUBLISH_PATH%"
)
mkdir "%PUBLISH_PATH%"

dotnet publish "TeamApp.API.csproj" -c Release -o "%PUBLISH_PATH%"
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] .NET publish failed!
    exit /b %errorlevel%
)

echo Publish complete.

echo.
pause
