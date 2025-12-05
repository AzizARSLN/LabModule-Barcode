using System;
using System.Text;
using System.Drawing.Printing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace LisBarkodPrinter
{
    class Program
    {
        private const int PORT = 22443;
        private const string VERSION = "1.0.0";

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("=== Gainscha GS-2408D Laboratuvar Barkod Servisi ===");
                Console.WriteLine($"Port: {PORT}");
                Console.WriteLine($"Version: {VERSION}");
                Console.WriteLine();

                // Bağımlılık kontrolü
                CheckDependencies();

                var builder = WebApplication.CreateBuilder(args);
                
                // Configure URLs - alternatif portlar ile
                ConfigureUrls(builder);
                
                // Add services
                builder.Services.AddCors(options =>
                {
                    options.AddDefaultPolicy(policy =>
                    {
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    });
                });

                var app = builder.Build();
                app.UseCors();

            // Status endpoint
            app.MapGet("/", () =>
            {
                Console.WriteLine("📡 Status check request");
                return Results.Json(new
                {
                    status = "Print Manager Running",
                    port = PORT,
                    version = VERSION,
                    timestamp = DateTime.Now.ToString("o")
                });
            });

            // Get printers endpoint
            app.MapGet("/printers", () =>
            {
                Console.WriteLine("📡 Printers list request");
                try
                {
                    var printers = new List<object>();
                    foreach (string printerName in PrinterSettings.InstalledPrinters)
                    {
                        printers.Add(new
                        {
                            name = printerName,
                            type = DetectPrinterType(printerName)
                        });
                    }
                    
                    Console.WriteLine($"✓ Found {printers.Count} printers");
                    return Results.Json(new { printers });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error getting printers: {ex.Message}");
                    return Results.Json(new { error = ex.Message });
                }
            });

            // Print endpoint
            app.MapPost("/print", async (HttpRequest request) =>
            {
                try
                {
                    using var reader = new StreamReader(request.Body);
                    var body = await reader.ReadToEndAsync();
                    var data = JsonSerializer.Deserialize<PrintRequest>(body, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (data == null)
                    {
                        Console.WriteLine("❌ Invalid request data");
                        return Results.Json(new { error = "Invalid request data" }, statusCode: 400);
                    }

                    Console.WriteLine("\n🖨️  Print Request Received:");
                    Console.WriteLine($"   Sample ID: {data.SampleId}");
                    Console.WriteLine($"   Patient: {data.PatientName}");
                    Console.WriteLine($"   Test: {data.TestType}");
                    Console.WriteLine($"   Printer: {data.Printer ?? "default"}");

                    // Yazıcı tipini belirle
                    string printerType = "zpl"; // default
                    string? targetPrinterName = data.Printer;
                    
                    if (string.IsNullOrEmpty(targetPrinterName))
                    {
                        // Gainscha yazıcısını ara
                        foreach (string name in PrinterSettings.InstalledPrinters)
                        {
                            if (name.ToLower().Contains("gainscha") || 
                                name.ToLower().Contains("gs-") ||
                                name.ToLower().Contains("ga-"))
                            {
                                targetPrinterName = name;
                                printerType = DetectPrinterType(name);
                                break;
                            }
                        }
                    }
                    else
                    {
                        printerType = DetectPrinterType(targetPrinterName);
                    }

                    Console.WriteLine($"📌 Tespit edilen yazıcı tipi: {printerType}");

                    // Create label command (ZPL veya TSPL)
                    string labelCommand;
                    if (printerType == "label-tspl" || printerType == "tspl")
                    {
                        labelCommand = CreateTSPLLabel(
                            data.SampleId ?? "",
                            data.PatientName ?? "",
                            data.TestType ?? "",
                            data.PatientId ?? "",
                            data.ServiceName ?? "Laboratuvar",
                            data.DoctorName ?? "Dr. Unknown",
                            data.SampleDate ?? DateTime.Now
                        );
                        Console.WriteLine("✓ TSPL komutu oluşturuldu");
                    }
                    else
                    {
                        labelCommand = CreateLaboratoryLabel(
                            data.SampleId ?? "",
                            data.PatientName ?? "",
                            data.TestType ?? "",
                            data.PatientId ?? "",
                            data.ServiceName ?? "Laboratuvar",
                            data.DoctorName ?? "Dr. Unknown",
                            data.SampleDate ?? DateTime.Now
                        );
                        Console.WriteLine("✓ ZPL komutu oluşturuldu");
                    }

                    Console.WriteLine($"\n📄 {printerType.ToUpper()} Komutu:");
                    Console.WriteLine("─────────────────────────────────");
                    Console.WriteLine(labelCommand);
                    Console.WriteLine("─────────────────────────────────");
                    
                    // Komut dosyaya kaydet (debug için)
                    try
                    {
                        string debugFileName = printerType == "label-tspl" || printerType == "tspl" 
                            ? "debug_tspl.txt" 
                            : "debug_zpl.txt";
                        File.WriteAllText(debugFileName, labelCommand);
                        Console.WriteLine($"💾 Komut {debugFileName} dosyasına kaydedildi");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Komut dosyaya kaydedilemedi: {ex.Message}");
                    }

                    // Print
                    Console.WriteLine("\n🖨️ Yazıcıya gönderiliyor...");
                    bool success = PrintLabel(labelCommand, data.Printer, printerType);

                    if (success)
                    {
                        Console.WriteLine("✅ Yazdırma başarılı!");
                        return Results.Json(new
                        {
                            success = true,
                            message = "Etiket başarıyla yazdırıldı",
                            printer = data.Printer ?? "default",
                            sampleId = data.SampleId
                        });
                    }
                    else
                    {
                        Console.WriteLine("❌ Yazdırma başarısız!");
                        return Results.Json(new
                        {
                            success = false,
                            error = "Yazdırma başarısız"
                        }, statusCode: 500);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Hata: {ex.Message}");
                    Console.WriteLine($"📍 Detay: {ex.StackTrace}");
                    return Results.Json(new { error = ex.Message }, statusCode: 500);
                }
            });

            // Test print endpoint
            app.MapPost("/test", async (HttpRequest request) =>
            {
                try
                {
                    Console.WriteLine("\n🧪 Test yazdırma isteği alındı");
                    
                    // Yazıcı tipini belirle
                    string testPrinterType = "zpl";
                    string? testPrinterName = null;
                    
                    foreach (string name in PrinterSettings.InstalledPrinters)
                    {
                        if (name.ToLower().Contains("gainscha") || 
                            name.ToLower().Contains("gs-") ||
                            name.ToLower().Contains("ga-"))
                        {
                            testPrinterName = name;
                            testPrinterType = DetectPrinterType(name);
                            break;
                        }
                    }

                    string testCommand;
                    if (testPrinterType == "label-tspl" || testPrinterType == "tspl")
                    {
                        // TSPL test komutu (güncellenmiş format)
                        testCommand = @"SIZE 50 mm, 30 mm
GAP 3 mm, 0 mm
CLS
TEXT 20,25,""2"",0,1,1,""TEST ETIKET""
TEXT 20,50,""2"",0,1,1,""Servisi: Test Laboratuvar""
TEXT 20,70,""1"",0,1,1,""Dr. Test""
TEXT 160,70,""1"",0,1,1,""" + DateTime.Now.ToString("dd.MM.yyyy HH:mm") + @"""
BARCODE 75,95,""128"",60,1,0,3,3,""TEST123""
TEXT 20,195,""1"",0,1,1,""TEST YAZDIRMA""
BOX 10,10,390,220,2
PRINT 1,1";
                        Console.WriteLine("📄 Test TSPL Komutu:");
                    }
                    else
                    {
                        // ZPL test komutu
                        testCommand = @"^XA
^PW400
^LL200
^CF0,0
^FO10,10^A0N,25,25^FDTEST ETIKET^FS
^FO10,40^A0N,18,18^FDServisi: Test Laboratuvar^FS
^FO10,65^A0N,16,16^FDDr. Test^FS
^FO200,65^A0N,16,16^FD" + DateTime.Now.ToString("dd.MM.yyyy HH:mm") + @"^FS
^FO50,90^BY3,2,60^BCN,60,Y,N,N^FDTEST123^FS
^FO50,155^A0N,20,20^FDTEST123^FS
^FO10,180^A0N,18,18^FDTEST YAZDIRMA^FS
^FO5,5^GB390,190,3^FS
^XZ";
                        Console.WriteLine("📄 Test ZPL Komutu:");
                    }

                    Console.WriteLine("─────────────────────────────────");
                    Console.WriteLine(testCommand);
                    Console.WriteLine("─────────────────────────────────");

                    // Test yazdırma
                    Console.WriteLine("\n🖨️ Test etiketi yazıcıya gönderiliyor...");
                    bool success = PrintLabel(testCommand, null, testPrinterType);

                    if (success)
                    {
                        Console.WriteLine("✅ Test yazdırma başarılı!");
                        return Results.Json(new
                        {
                            success = true,
                            message = "Test etiketi başarıyla yazdırıldı",
                            timestamp = DateTime.Now.ToString("o")
                        });
                    }
                    else
                    {
                        Console.WriteLine("❌ Test yazdırma başarısız!");
                        return Results.Json(new
                        {
                            success = false,
                            error = "Test yazdırma başarısız"
                        }, statusCode: 500);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Test yazdırma hatası: {ex.Message}");
                    return Results.Json(new { error = ex.Message }, statusCode: 500);
                }
            });

                Console.WriteLine($"🖨️  Print Manager running on http://localhost:{PORT}");
                Console.WriteLine($"📡 Ready to receive print jobs!");
                Console.WriteLine($"🔗 Status: http://localhost:{PORT}");
                Console.WriteLine($"🔗 Printers: http://localhost:{PORT}/printers");
                Console.WriteLine($"🔗 Print: POST http://localhost:{PORT}/print");
                Console.WriteLine($"🔗 Test: POST http://localhost:{PORT}/test");
                Console.WriteLine();
                Console.WriteLine("Ctrl+C ile durdurun...");
                Console.WriteLine();

                app.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("❌ UYGULAMA BAŞLATMA HATASI!");
                Console.WriteLine($"📍 Hata: {ex.Message}");
                Console.WriteLine($"📍 Detay: {ex.StackTrace}");
                Console.WriteLine();
                Console.WriteLine("Bu hata genellikle şu nedenlerden kaynaklanır:");
                Console.WriteLine("• Port zaten kullanımda");
                Console.WriteLine("• .NET 9.0 Runtime yüklü değil");
                Console.WriteLine("• Yazıcı sürücüleri eksik");
                Console.WriteLine("• Windows API erişim izni yok");
                Console.WriteLine();
                Console.WriteLine("Çözüm önerileri:");
                Console.WriteLine("• Farklı bir port deneyin");
                Console.WriteLine("• .NET 9.0 Runtime'ı yükleyin");
                Console.WriteLine("• Yönetici olarak çalıştırın");
                Console.WriteLine();
                Console.WriteLine("Devam etmek için bir tuşa basın...");
                Console.ReadKey();
            }
        }

        /// <summary>
        /// Bağımlılıkları kontrol eder
        /// </summary>
        static void CheckDependencies()
        {
            Console.WriteLine("🔍 Sistem bağımlılıkları kontrol ediliyor...");
            
            try
            {
                // .NET Runtime kontrolü
                var runtimeVersion = Environment.Version;
                Console.WriteLine($"✓ .NET Runtime: {runtimeVersion}");
                
                // Yazıcı kontrolü
                var printerCount = PrinterSettings.InstalledPrinters.Count;
                Console.WriteLine($"✓ Yüklü yazıcı sayısı: {printerCount}");
                
                if (printerCount > 0)
                {
                    Console.WriteLine("📄 Yüklü yazıcılar:");
                    foreach (string printer in PrinterSettings.InstalledPrinters)
                    {
                        Console.WriteLine($"  • {printer}");
                    }
                }
                else
                {
                    Console.WriteLine("⚠️ Hiç yazıcı bulunamadı!");
                }
                
                Console.WriteLine("✅ Bağımlılık kontrolü tamamlandı");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Bağımlılık kontrolünde hata: {ex.Message}");
                Console.WriteLine();
            }
        }

        /// <summary>
        /// URL yapılandırması - sadece ana portu kullan
        /// </summary>
        static void ConfigureUrls(WebApplicationBuilder builder)
        {
            // Sadece ana portu kullan - alternatif portlar opsiyonel
            builder.WebHost.UseUrls($"http://localhost:{PORT}");
            Console.WriteLine($"🌐 Dinlenecek port: {PORT}");
            
            // Alternatif portlar için bilgi mesajı
            Console.WriteLine($"💡 Alternatif portlar mevcut değilse, manuel olarak yapılandırabilirsiniz: {PORT + 1}, {PORT + 2}");
        }

        /// <summary>
        /// Laboratuvar etiketi için ZPL oluşturur
        /// </summary>
        static string CreateLaboratoryLabel(string sampleId, string patientName, string testType, 
            string patientId, string serviceName, string doctorName, DateTime sampleDate)
        {
            Console.WriteLine("  ⚙️ Basit etiket oluşturuluyor...");
            
            StringBuilder zpl = new StringBuilder();
            zpl.AppendLine("^XA"); // ZPL başlangıç
            
            // Etiket boyutu (orijinal boyut - yazıcı uyumlu)
            zpl.AppendLine("^PW400"); // Genişlik
            zpl.AppendLine("^LL200"); // Yükseklik
            
            // Hasta bilgileri - üst (aşağıya taşındı)
            zpl.AppendLine($"^FO10,30^A0N,20,20^FD{patientName}^FS");
            
            // Servis bilgisi (aşağıya taşındı)
            zpl.AppendLine($"^FO10,55^A0N,16,16^FDServisi: {serviceName}^FS");
            
            // Doktor ve tarih (aşağıya taşındı)
            zpl.AppendLine($"^FO10,78^A0N,14,14^FD{doctorName}^FS");
            zpl.AppendLine($"^FO200,78^A0N,14,14^FD{sampleDate:dd.MM.yyyy HH:mm}^FS");
            
            // Barkod - aşağıya taşındı (etiketin alt yarısında)
            zpl.AppendLine($"^FO50,120^BY2,2,50^BCN,50,Y,N,N^FD{sampleId}^FS");
            
            // Test türü - alt (daha aşağıya taşındı - üst üste gelmesin)
            zpl.AppendLine($"^FO10,195^A0N,16,16^FD{testType}^FS");
            
            // Çerçeve (daha aşağıya büyütüldü - test türü içinde kalsın)
            zpl.AppendLine("^FO5,5^GB390,210,2^FS");
            
            zpl.AppendLine("^XZ"); // ZPL bitiş
            
            return zpl.ToString();
        }

        /// <summary>
        /// Laboratuvar etiketi için TSPL (TSC Print Language) oluşturur - Gainscha yazıcılar için
        /// </summary>
        static string CreateTSPLLabel(string sampleId, string patientName, string testType, 
            string patientId, string serviceName, string doctorName, DateTime sampleDate)
        {
            Console.WriteLine("  ⚙️ TSPL etiket oluşturuluyor...");
            
            StringBuilder tspl = new StringBuilder();
            
            // TSPL başlangıç komutları
            tspl.AppendLine("SIZE 50 mm, 30 mm"); // Etiket boyutu (50mm x 30mm)
            tspl.AppendLine("GAP 3 mm, 0 mm"); // Etiket arası boşluk
            tspl.AppendLine("CLS"); // Clear - önceki komutları temizle
            
            // Hasta adı - üst (biraz büyütüldü: "1" → "2")
            tspl.AppendLine($"TEXT 20,25,\"2\",0,1,1,\"{EscapeTSPL(patientName)}\"");
            
            // Servis bilgisi (font büyütüldü: "1" → "2")
            tspl.AppendLine($"TEXT 20,50,\"2\",0,1,1,\"Servisi: {EscapeTSPL(serviceName)}\"");
            
            // Doktor adı ve tarih/saat yan yana (y: 70)
            string dateStr = sampleDate.ToString("dd.MM.yyyy HH:mm");
            tspl.AppendLine($"TEXT 20,70,\"1\",0,1,1,\"{EscapeTSPL(doctorName)}\"");
            tspl.AppendLine($"TEXT 160,70,\"1\",0,1,1,\"{dateStr}\"");
            
            // Barkod (Code 128) - sola kaydırıldı (x: 90 → 75)
            // BARCODE x,y,"code type",height,human readable,rotation,narrow,wide,"content"
            // height: 70 → 60 (daha kompakt), narrow: 2 → 3, wide: 2 → 3 (daha geniş çubuklar, daha iyi okunabilirlik)
            tspl.AppendLine($"BARCODE 75,95,\"128\",60,1,0,3,3,\"{sampleId}\"");
            
            // Barkod altında numara KALDIRILDI - barkod zaten okunabilir numarayı içeriyor
            
            // Test türü - en alt (aşağıya indirildi, üst üste binmeyi önlemek için)
            tspl.AppendLine($"TEXT 20,195,\"1\",0,1,1,\"{EscapeTSPL(testType)}\"");
            
            // Çerçeve (BOX x,y,x_end,y_end,thickness)
            tspl.AppendLine("BOX 10,10,390,220,2"); // Çerçeve yüksekliği test türü için ayarlandı
            
            // Yazdır ve kağıdı besle
            tspl.AppendLine("PRINT 1,1"); // 1 kopya, 1 set
            
            return tspl.ToString();
        }

        /// <summary>
        /// TSPL komutları için özel karakterleri escape eder
        /// </summary>
        static string EscapeTSPL(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            
            // TSPL'de " karakteri escape edilmeli
            return text.Replace("\"", "\"\"");
        }

        /// <summary>
        /// Etiketi yazdırır
        /// </summary>
        static bool PrintLabel(string labelCommand, string? printerName, string printerType = "zpl")
        {
            try
            {
                Console.WriteLine("🔍 Yüklü yazıcılar aranıyor...");
                
                // Yazıcı seç
                string? targetPrinter = printerName;
                
                if (string.IsNullOrEmpty(targetPrinter))
                {
                    // Gainscha yazıcısını ara
                    foreach (string name in PrinterSettings.InstalledPrinters)
                    {
                        Console.WriteLine($"  📄 Bulunan yazıcı: {name}");
                        if (name.ToLower().Contains("gainscha") || 
                            name.ToLower().Contains("gs-2408") ||
                            name.ToLower().Contains("label"))
                        {
                            targetPrinter = name;
                            break;
                        }
                    }
                }
                
                if (string.IsNullOrEmpty(targetPrinter))
                {
                    Console.WriteLine("⚠️ Özel yazıcı bulunamadı. Varsayılan yazıcı kullanılacak.");
                    targetPrinter = new PrinterSettings().PrinterName;
                }
                
                Console.WriteLine($"✓ Kullanılacak yazıcı: {targetPrinter}");
                
                // Komut formatını belirle
                string commandType = (printerType == "label-tspl" || printerType == "tspl") ? "TSPL" : "ZPL";
                Console.WriteLine($"📤 {commandType} komutu yazıcıya gönderiliyor...");
                
                // Komutunu byte array'e çevir
                byte[] commandBytes = Encoding.UTF8.GetBytes(labelCommand);
                
                Console.WriteLine($"📊 Gönderilen {commandType} verisi boyutu: {commandBytes.Length} byte");
                Console.WriteLine($"📊 {commandType} komutunun ilk 100 karakteri: {labelCommand.Substring(0, Math.Min(100, labelCommand.Length))}");
                
                // Komut dosyaya da kaydet (debug için)
                try
                {
                    string debugFileName = commandType.ToLower() + "_bytes.bin";
                    File.WriteAllBytes(debugFileName, commandBytes);
                    Console.WriteLine($"💾 {commandType} byte verisi {debugFileName} dosyasına kaydedildi");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ {commandType} byte dosyaya kaydedilemedi: {ex.Message}");
                }
                
                // Yazıcıya raw data gönder
                Console.WriteLine($"🎯 Hedef yazıcı: {targetPrinter}");
                bool success = RawPrinterHelper.SendBytesToPrinter(targetPrinter, commandBytes, commandType);
                
                if (success)
                {
                    Console.WriteLine($"✅ {commandType} komutu başarıyla yazıcıya gönderildi!");
                    return true;
                }
                else
                {
                    Console.WriteLine($"❌ {commandType} komutu gönderimi başarısız!");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Yazdırma hatası: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Yazıcı tipini tespit eder
        /// </summary>
        static string DetectPrinterType(string printerName)
        {
            var nameLower = printerName.ToLower();
            
            // Gainscha yazıcıları TSPL kullanır
            if (nameLower.Contains("gainscha") || 
                nameLower.Contains("ga-") || 
                nameLower.Contains("gs-")) 
                return "label-tspl";
            
            // Zebra yazıcıları ZPL kullanır
            if (nameLower.Contains("zebra") || 
                nameLower.Contains("zdesigner")) 
                return "label-zpl";
            
            if (nameLower.Contains("pdf")) return "pdf";
            
            return "zpl"; // Varsayılan ZPL
        }
    }

    /// <summary>
    /// Print request model
    /// </summary>
    public class PrintRequest
    {
        public string? SampleId { get; set; }
        public string? PatientName { get; set; }
        public string? TestType { get; set; }
        public string? PatientId { get; set; }
        public string? ServiceName { get; set; }
        public string? DoctorName { get; set; }
        public DateTime? SampleDate { get; set; }
        public string? Printer { get; set; }
    }

    /// <summary>
    /// Raw printer helper sınıfı - Windows API kullanarak direkt yazıcıya veri gönderir
    /// </summary>
    public static class RawPrinterHelper
    {
        [System.Runtime.InteropServices.DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Ansi, ExactSpelling = true, CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall)]
        public static extern bool OpenPrinter([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [System.Runtime.InteropServices.DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [System.Runtime.InteropServices.DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Ansi, ExactSpelling = true, CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall)]
        public static extern bool StartDocPrinter(IntPtr hPrinter, int level, [System.Runtime.InteropServices.In, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] DOCINFOA di);

        [System.Runtime.InteropServices.DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall)]
        public static extern bool EndDocPrinter(IntPtr hPrinter);

        [System.Runtime.InteropServices.DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall)]
        public static extern bool StartPagePrinter(IntPtr hPrinter);

        [System.Runtime.InteropServices.DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall)]
        public static extern bool EndPagePrinter(IntPtr hPrinter);

        [System.Runtime.InteropServices.DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall)]
        public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
        public class DOCINFOA
        {
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)]
            public string pDocName = "Label Print";
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)]
            public string pOutputFile = "";
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)]
            public string pDataType = "RAW";
        }

        public static bool SendBytesToPrinter(string szPrinterName, byte[] pBytes, string commandType = "ZPL")
        {
            IntPtr hPrinter = new IntPtr(0);
            DOCINFOA di = new DOCINFOA();
            bool bSuccess = false;

            Console.WriteLine($"🔧 RawPrinterHelper: Yazıcı açılıyor: {szPrinterName}");
            Console.WriteLine($"🔧 RawPrinterHelper: Gönderilecek veri boyutu: {pBytes.Length} byte");

            di.pDocName = $"{commandType} Label";
            di.pDataType = "RAW";

            if (OpenPrinter(szPrinterName.Normalize(), out hPrinter, IntPtr.Zero))
            {
                Console.WriteLine("✅ RawPrinterHelper: Yazıcı başarıyla açıldı");
                
                if (StartDocPrinter(hPrinter, 1, di))
                {
                    Console.WriteLine("✅ RawPrinterHelper: Doküman başlatıldı");
                    
                    if (StartPagePrinter(hPrinter))
                    {
                        Console.WriteLine("✅ RawPrinterHelper: Sayfa başlatıldı");
                        
                        IntPtr pUnmanagedBytes = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(pBytes.Length);
                        System.Runtime.InteropServices.Marshal.Copy(pBytes, 0, pUnmanagedBytes, pBytes.Length);
                        bSuccess = WritePrinter(hPrinter, pUnmanagedBytes, pBytes.Length, out int dwWritten);
                        
                        Console.WriteLine($"🔧 RawPrinterHelper: Yazdırma sonucu: {bSuccess}, Yazılan byte: {dwWritten}");
                        
                        System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pUnmanagedBytes);
                        EndPagePrinter(hPrinter);
                        Console.WriteLine("✅ RawPrinterHelper: Sayfa sonlandırıldı");
                    }
                    else
                    {
                        Console.WriteLine("❌ RawPrinterHelper: Sayfa başlatılamadı");
                    }
                    
                    EndDocPrinter(hPrinter);
                    Console.WriteLine("✅ RawPrinterHelper: Doküman sonlandırıldı");
                }
                else
                {
                    Console.WriteLine("❌ RawPrinterHelper: Doküman başlatılamadı");
                }
                
                ClosePrinter(hPrinter);
                Console.WriteLine("✅ RawPrinterHelper: Yazıcı kapatıldı");
            }
            else
            {
                Console.WriteLine($"❌ RawPrinterHelper: Yazıcı açılamadı: {szPrinterName}");
                int errorCode = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                Console.WriteLine($"❌ RawPrinterHelper: Windows hata kodu: {errorCode}");
            }
            
            return bSuccess;
        }
    }
}
