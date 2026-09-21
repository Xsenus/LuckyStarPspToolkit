@echo off
setlocal
REM Checked build + tests + self-contained release ZIPs; forwarded arguments belong to Python.
where py >nul 2>nul
if errorlevel 1 goto use_python
py -3 "%~dp0scripts\build_release.py" %*
exit /b %errorlevel%
:use_python
python "%~dp0scripts\build_release.py" %*
exit /b %errorlevel%
