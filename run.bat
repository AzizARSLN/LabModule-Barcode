@echo off
echo ========================================
echo Laboratuvar Barkod Yazici Servisi
echo ========================================
echo.

REM .NET Runtime kontrolü
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo HATA: .NET Runtime bulunamadi!
    echo Lutfen .NET 9.0 Runtime'i yukleyin.
    echo https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

echo .NET Runtime bulundu.
echo.

REM Uygulamayı çalıştır
echo Uygulama baslatiliyor...
echo.
dotnet run

REM Hata durumunda konsolu açık tut
if %errorlevel% neq 0 (
    echo.
    echo ========================================
    echo UYGULAMA HATA VERDI!
    echo ========================================
    echo.
    echo Hata kodu: %errorlevel%
    echo.
    echo Muhtemel nedenler:
    echo - Port zaten kullanımda
    echo - .NET Runtime eksik
    echo - Yazici suruculeri eksik
    echo - Windows API erisim izni yok
    echo.
    echo Cozum onerileri:
    echo - Farkli bir port deneyin
    echo - Yonetici olarak calistirin
    echo - .NET 9.0 Runtime'i yukleyin
    echo.
    pause
)

