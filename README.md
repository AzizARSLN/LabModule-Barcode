# Gainscha GS-2408D Laboratuvar Barkod Servisi

Client-side barkod yazdırma servisi. Laboratuvar modülünden gelen istekleri alır ve local Gainscha yazıcıda barkod etiketleri yazdırır.

## 🚀 Kullanım

### Client Kurulum:
1. `LisBarkodPrinter.exe` dosyasını client bilgisayara kopyalayın
2. Çift tıklayarak çalıştırın
3. Servis `localhost:22443` portunda dinlemeye başlar

### Server'dan Yazdırma:

```javascript
// Laboratuvar modülünden HTTP POST isteği gönderin:
fetch('http://localhost:22443/print', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    sampleId: "7813724113",
    patientName: "MUSTAFA KUDAY",
    patientId: "12345678",
    testType: "TAM KAN",
    serviceName: "Laboratuvar",
    doctorName: "Dr. MUSTAFA",
    sampleDate: "2024-10-12T14:30:00",
    printer: null  // null = varsayılan Gainscha yazıcı
  })
})
.then(response => response.json())
.then(result => {
  if (result.success) {
    console.log('✅ Yazdırma başarılı:', result.message);
  } else {
    console.error('❌ Hata:', result.error);
  }
});
```

## 📡 API Endpoints

### GET `/` - Durum Kontrolü
```json
{
  "status": "Print Manager Running",
  "port": 22443,
  "version": "1.0.0",
  "timestamp": "2024-10-12T14:30:00"
}
```

### GET `/printers` - Yazıcı Listesi
```json
{
  "printers": [
    { "name": "Gainscha GS-2408DC", "type": "label-tspl" },
    { "name": "Microsoft Print to PDF", "type": "pdf" }
  ]
}
```

### POST `/print` - Etiket Yazdır
**Request:**
```json
{
  "sampleId": "7813724113",
  "patientName": "MUSTAFA KUDAY",
  "patientId": "12345678",
  "testType": "TAM KAN",
  "serviceName": "Laboratuvar",
  "doctorName": "Dr. MUSTAFA",
  "sampleDate": "2024-10-12T14:30:00",
  "printer": "Gainscha GS-2408DC"
}
```

**Response (Success):**
```json
{
  "success": true,
  "message": "Etiket başarıyla yazdırıldı",
  "printer": "Gainscha GS-2408DC",
  "sampleId": "7813724113"
}
```

**Response (Error):**
```json
{
  "success": false,
  "error": "Yazıcı bulunamadı"
}
```

## 🏥 Etiket Tasarımı

**Boyut:** 400x200 dots (5x3 cm @ 203 DPI)

```
┌─────────────────────────────────────────────┐
│ MUSTAFA KUDAY                    (20pt)    │
│ Servisi: Laboratuvar            (16pt)     │
│ Dr. MUSTAFA         12.10.2024 14:30 (14pt)│
│                                             │
│       ████████████████████████████████████ │
│       7813724113                           │
│                                             │
│ TAM KAN                           (16pt)   │
└─────────────────────────────────────────────┘
```

## 🧪 Test

1. Programı çalıştırın: `LisBarkodPrinter.exe`
2. Tarayıcıda açın: `test-print-client.html`
3. Bilgileri girin ve "Etiket Yazdır" butonuna tıklayın

## 🔧 Derleme

```bash
cd LisBarkodPrinter
dotnet build
dotnet publish -c Release -r win-x64 --self-contained
```

EXE dosyası: `bin/Release/net9.0/win-x64/publish/LisBarkodPrinter.exe`

## 📋 Gereksinimler

- Windows 6.1 veya üzeri
- .NET 9.0 Runtime (self-contained ise gerekmez)
- Gainscha GS-2408D yazıcı sürücüsü

