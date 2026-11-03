@echo off
setlocal EnableExtensions DisableDelayedExpansion
pushd "%~dp0" || exit /b 1

set "SEVEN_ZIP=C:\Program Files\7-Zip\7z.exe"
if not exist "%SEVEN_ZIP%" (
    echo ERROR: 7-Zip was not found at "%SEVEN_ZIP%". >&2
    goto :failed
)
if not exist "VERSION" (
    echo ERROR: VERSION was not found. >&2
    goto :failed
)
set "RELEASE_VERSION="
set /p RELEASE_VERSION=<VERSION
if not defined RELEASE_VERSION (
    echo ERROR: VERSION is empty. >&2
    goto :failed
)

dotnet msbuild MusicBee-Autoresume.csproj -noAutoResponse -restore -t:Rebuild -p:Configuration=Release -verbosity:minimal
if errorlevel 1 goto :failed

if not exist "dist" mkdir "dist"
if errorlevel 1 goto :failed
if not exist "bin\Package" mkdir "bin\Package"
if errorlevel 1 goto :failed
copy /y "bin\Release\MB_Autoresume.dll" "bin\Package\MB_Autoresume.dll" >nul
if errorlevel 1 goto :failed
copy /y "README.md" "bin\Package\README.md" >nul
if errorlevel 1 goto :failed
copy /y "LICENSE" "bin\Package\LICENSE" >nul
if errorlevel 1 goto :failed

set "ARCHIVE=%CD%\dist\Autoresume-%RELEASE_VERSION%.7z"
set "TEMP_ARCHIVE=%CD%\dist\Autoresume-%RELEASE_VERSION%.tmp.7z"
if exist "%TEMP_ARCHIVE%" del /q "%TEMP_ARCHIVE%"
if exist "%TEMP_ARCHIVE%" goto :failed
pushd "bin\Package" || goto :failed
"%SEVEN_ZIP%" a -t7z -mx=9 "%TEMP_ARCHIVE%" "MB_Autoresume.dll" "README.md" "LICENSE"
if errorlevel 1 (
    popd
    goto :failed
)
popd
"%SEVEN_ZIP%" t "%TEMP_ARCHIVE%"
if errorlevel 1 goto :failed
move /y "%TEMP_ARCHIVE%" "%ARCHIVE%" >nul
if errorlevel 1 goto :failed
echo Created: %ARCHIVE%
popd
exit /b 0

:failed
echo ERROR: Build or packaging failed. >&2
popd
exit /b 1
