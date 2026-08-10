REM SPDX-FileCopyrightText: 2024 gluesniffler <159397573+gluesniffler@users.noreply.github.com>
REM SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
REM
REM SPDX-License-Identifier: AGPL-3.0-or-later

@echo off
cd ../../

REM Kill any processes still listening on port 1212 before starting the server
echo Freeing port 1212...
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":1212 " ^| findstr "LISTENING"') do (
    echo Killing process %%p on port 1212
    taskkill /F /PID %%p >nul 2>&1
)

call dotnet run --project Content.Server --no-build %*

pause
