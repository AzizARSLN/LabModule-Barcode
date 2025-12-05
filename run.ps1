# Laboratuvar Barkod Yazıcı Servisi - PowerShell Launcher
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Laboratuvar Barkod Yazici Servisi" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# .NET Runtime kontrolü
try {
    $dotnetVersion = dotnet --version
    Write-Host ".NET Runtime bulundu: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "HATA: .NET Runtime bulunamadi!" -ForegroundColor Red
    Write-Host "Lutfen .NET 9.0 Runtime'i yukleyin." -ForegroundColor Yellow
    Write-Host "https://dotnet.microsoft.com/download" -ForegroundColor Blue
    Write-Host ""
    Read-Host "Devam etmek icin Enter'a basin"
    exit 1
}

Write-Host ""
Write-Host "Uygulama baslatiliyor..." -ForegroundColor Yellow
Write-Host ""

# Uygulamayı çalıştır
try {
    dotnet run
} catch {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "UYGULAMA HATA VERDI!" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Hata: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Muhtemel nedenler:" -ForegroundColor Yellow
    Write-Host "- Port zaten kullanımda" -ForegroundColor White
    Write-Host "- .NET Runtime eksik" -ForegroundColor White
    Write-Host "- Yazici suruculeri eksik" -ForegroundColor White
    Write-Host "- Windows API erisim izni yok" -ForegroundColor White
    Write-Host ""
    Write-Host "Cozum onerileri:" -ForegroundColor Yellow
    Write-Host "- Farkli bir port deneyin" -ForegroundColor White
    Write-Host "- Yonetici olarak calistirin" -ForegroundColor White
    Write-Host "- .NET 9.0 Runtime'i yukleyin" -ForegroundColor White
    Write-Host ""
    Read-Host "Devam etmek icin Enter'a basin"
}





