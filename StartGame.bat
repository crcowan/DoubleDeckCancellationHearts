@echo off
TITLE Double Deck Hearts Launcher
echo ===================================================
echo     Starting Double Deck Cancellation Hearts...
echo ===================================================

echo.
echo [1/4] Cleaning up previous sessions...
taskkill /F /IM GameEngine.Api.exe /T >nul 2>&1
taskkill /F /IM dotnet.exe /T >nul 2>&1
taskkill /F /IM node.exe /T >nul 2>&1

echo.
echo [2/4] Starting Game Engine (C# Backend)...
start "Hearts Game Engine" cmd /k "cd GameEngine.Api && dotnet run"

echo [3/4] Starting User Interface (React Frontend)...
:: Ensuring node path is picked up if installed recently
start "Hearts Web UI" cmd /k "set PATH=%%PATH%%;%%APPDATA%%\npm;C:\Program Files\nodejs && cd frontend && npm run dev"

echo [3/3] Waiting for servers to initialize...
timeout /t 6 /nobreak > nul

echo.
echo Launching your browser to http://localhost:5173 ...
start http://localhost:5173

echo.
echo ===================================================
echo Game launched successfully.
echo. 
echo To stop playing entirely, simply close the two 
echo terminal windows that popped up for the Backend 
echo and Frontend.
echo ===================================================
pause
