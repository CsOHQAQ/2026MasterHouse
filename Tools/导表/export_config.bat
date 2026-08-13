@echo off
chcp 65001 >nul
echo ========================================
echo   MasterHouse - Excel Config Exporter
echo ========================================
echo.

where python >nul 2>nul
if %errorlevel% neq 0 (
    echo [ERROR] Python is not installed or not in PATH.
    pause
    exit /b 1
)

pip show openpyxl >nul 2>nul
if %errorlevel% neq 0 (
    echo [INFO] Installing openpyxl...
    pip install openpyxl
)

echo [INFO] Exporting furniture table...
python "%~dp0export_furniture.py"
if %errorlevel% neq 0 (
    echo [ERROR] Furniture export failed!
    pause
    exit /b 1
)

echo [INFO] Exporting store table...
python "%~dp0export_store.py"
if %errorlevel% neq 0 (
    echo [ERROR] Store export failed!
    pause
    exit /b 1
)

echo [INFO] Exporting furniture room table...
python "%~dp0export_furniture_room.py"
if %errorlevel% neq 0 (
    echo [ERROR] Furniture room export failed!
    pause
    exit /b 1
)

echo [INFO] Exporting visitor tables...
python "%~dp0export_visitor.py"
if %errorlevel% neq 0 (
    echo [ERROR] Visitor export failed!
    pause
    exit /b 1
)

echo [INFO] Exporting sfx table...
python "%~dp0export_sfx.py"
if %errorlevel% neq 0 (
    echo [ERROR] Sfx export failed!
    pause
    exit /b 1
)

echo [INFO] Exporting dialogue table...
python "%~dp0export_dialogue.py"
if %errorlevel% neq 0 (
    echo [ERROR] Dialogue export failed!
    pause
    exit /b 1
)

echo.
echo [DONE] All exports complete.
echo        CSV written into Assets\Configs\ - Unity picks them up
echo        automatically on next focus (asset pipeline + auto import).
echo.
pause