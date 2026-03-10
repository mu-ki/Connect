@echo off
setlocal

echo ===========================================
echo Building Angular Client to API wwwroot
echo ===========================================
echo.

echo [1/2] Building Angular client...
cd /d "%~dp0team-app-client"
call npx -p @angular/cli ng build --configuration production --base-href /connect/
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Angular build failed.
    exit /b %errorlevel%
)

echo.
echo [2/2] Copying build output to TeamApp.API\wwwroot...
cd /d "%~dp0"
if exist "TeamApp.API\wwwroot" rmdir /s /q "TeamApp.API\wwwroot"
mkdir "TeamApp.API\wwwroot"

xcopy "team-app-client\dist\team-app-client\browser\*" "TeamApp.API\wwwroot\" /E /I /Y > nul
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Copy to wwwroot failed.
    exit /b %errorlevel%
)

echo.
echo Angular build copied to TeamApp.API\wwwroot successfully.
pause
