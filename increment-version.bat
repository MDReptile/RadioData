@echo off
REM Auto-increment version script for RadioData
REM Reads current version from .csproj, increments by 0.01, and updates the file

setlocal enabledelayedexpansion

set "PROJECT_FILE=RadioDataApp\RadioDataApp.csproj"
set "VERSION_FILE=version.txt"

REM Read current version from csproj
for /f "tokens=2 delims=<>" %%a in ('findstr /r "<Version>" %PROJECT_FILE%') do set CURRENT_VERSION=%%a

echo Current version: %CURRENT_VERSION%

REM Convert to integer for increment (multiply by 100, add 1, divide by 100)
for /f "tokens=1,2 delims=." %%a in ("%CURRENT_VERSION%") do (
    set MAJOR=%%a
    set MINOR=%%b
)

REM Increment minor version
set /a MINOR=%MINOR%+1

REM Check if we hit 100 (need to roll over to next major)
if %MINOR% GEQ 100 (
    set /a MAJOR=%MAJOR%+1
    set MINOR=0
)

REM Format with leading zero if needed
if %MINOR% LSS 10 (
    set NEW_VERSION=%MAJOR%.0%MINOR%
) else (
    set NEW_VERSION=%MAJOR%.%MINOR%
)

echo New version: %NEW_VERSION%

REM Update the .csproj file
powershell -Command "(gc %PROJECT_FILE%) -replace '<Version>%CURRENT_VERSION%</Version>', '<Version>%NEW_VERSION%</Version>' -replace '<AssemblyVersion>.*</AssemblyVersion>', '<AssemblyVersion>%MAJOR%.%MINOR%.0.0</AssemblyVersion>' -replace '<FileVersion>.*</FileVersion>', '<FileVersion>%MAJOR%.%MINOR%.0.0</FileVersion>' | Out-File -encoding ASCII %PROJECT_FILE%"

echo Version updated successfully!
echo.
echo Building project with new version...
dotnet build -c Release

endlocal
