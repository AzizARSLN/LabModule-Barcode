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
            Console.WriteLine("=== Gainscha GS-2408D Laboratuvar Barkod Servisi ===");
            Console.WriteLine($"Port: {PORT}");
            Console.WriteLine($"Version: {VERSION}");
            Console.WriteLine();

            var builder = WebApplication.CreateBuilder(args);
            
            // Configure URLs
            builder.WebHost.UseUrls($"http://localhost:{PORT}");
            
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

                    // Create ZPL command
                    string zplCommand = CreateLaboratoryLabel(
                        data.SampleId ?? "",
                        data.PatientName ?? "",
                        data.TestType ?? "",
                        data.PatientId ?? "",
                        data.ServiceName ?? "Laboratuvar",
                        data.DoctorName ?? "Dr. Unknown",
                        data.SampleDate ?? DateTime.Now
                    );

                    Console.WriteLine("✓ ZPL komutu oluşturuldu");
                    Console.WriteLine("\n📄 ZPL Komutu:");
                    Console.WriteLine("─────────────────────────────────");
                    Console.WriteLine(zplCommand);
                    Console.WriteLine("─────────────────────────────────");

                    // Print
                    Console.WriteLine("\n🖨️ Yazıcıya gönderiliyor...");
                    bool success = PrintLabel(zplCommand, data.Printer);

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

            Console.WriteLine($"🖨️  Print Manager running on http://localhost:{PORT}");
            Console.WriteLine($"📡 Ready to receive print jobs!");
            Console.WriteLine($"🔗 Status: http://localhost:{PORT}");
            Console.WriteLine($"🔗 Printers: http://localhost:{PORT}/printers");
            Console.WriteLine($"🔗 Print: POST http://localhost:{PORT}/print");
            Console.WriteLine();
            Console.WriteLine("Ctrl+C ile durdurun...");
            Console.WriteLine();

            app.Run();
        }

        /// <summary>
        /// Laboratuvar etiketi için ZPL oluşturur
        /// </summary>
        static string CreateLaboratoryLabel(string sampleId, string patientName, string testType, 
            string patientId, string serviceName, string doctorName, DateTime sampleDate)
        {
            StringBuilder zpl = new StringBuilder();
            zpl.AppendLine("^XA"); // ZPL başlangıç
            
            // Etiket boyutu
            zpl.AppendLine("^PW400"); // Genişlik
            zpl.AppendLine("^LL200"); // Yükseklik
            
            // Hasta bilgileri - üst
            zpl.AppendLine($"^FO10,10^A0N,20,20^FD{patientName}^FS");
            
            // Servis bilgisi
            zpl.AppendLine($"^FO10,35^A0N,16,16^FDServisi: {serviceName}^FS");
            
            // Doktor ve tarih
            zpl.AppendLine($"^FO10,58^A0N,14,14^FD{doctorName}^FS");
            zpl.AppendLine($"^FO200,58^A0N,14,14^FD{sampleDate:dd.MM.yyyy HH:mm}^FS");
            
            // Barkod - ortalanmış
            zpl.AppendLine($"^FO50,80^BY2,2,50^BCN,50,Y,N,N^FD{sampleId}^FS");
            
            // Test türü - alt
            zpl.AppendLine($"^FO10,160^A0N,16,16^FD{testType}^FS");
            
            // Çerçeve
            zpl.AppendLine("^FO5,5^GB390,190,2^FS");
            
            zpl.AppendLine("^XZ"); // ZPL bitiş
            
            return zpl.ToString();
        }

        /// <summary>
        /// Etiketi yazdırır
        /// </summary>
        static bool PrintLabel(string zplCommand, string? printerName)
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
                
                // ZPL komutunu raw data olarak gönder
                Console.WriteLine("📤 ZPL komutu yazıcıya gönderiliyor...");
                byte[] zplBytes = Encoding.UTF8.GetBytes(zplCommand);
                
                Console.WriteLine($"📊 Gönderilen ZPL verisi boyutu: {zplBytes.Length} byte");
                
                // Yazıcıya raw data gönder
                bool success = RawPrinterHelper.SendBytesToPrinter(targetPrinter, zplBytes);
                
                if (success)
                {
                    Console.WriteLine("✅ ZPL komutu başarıyla yazıcıya gönderildi!");
                    return true;
                }
                else
                {
                    Console.WriteLine("❌ ZPL komutu gönderimi başarısız!");
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
            
            if (nameLower.Contains("gainscha")) return "label-tspl";
            if (nameLower.Contains("zebra") || nameLower.Contains("zdesigner")) return "label-zpl";
            if (nameLower.Contains("pdf")) return "pdf";
            
            return "standard";
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
            public string pDocName = "ZPL Label";
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)]
            public string pOutputFile = "";
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)]
            public string pDataType = "RAW";
        }

        public static bool SendBytesToPrinter(string szPrinterName, byte[] pBytes)
        {
            IntPtr hPrinter = new IntPtr(0);
            DOCINFOA di = new DOCINFOA();
            bool bSuccess = false;

            di.pDocName = "ZPL Label";
            di.pDataType = "RAW";

            if (OpenPrinter(szPrinterName.Normalize(), out hPrinter, IntPtr.Zero))
            {
                if (StartDocPrinter(hPrinter, 1, di))
                {
                    if (StartPagePrinter(hPrinter))
                    {
                        IntPtr pUnmanagedBytes = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(pBytes.Length);
                        System.Runtime.InteropServices.Marshal.Copy(pBytes, 0, pUnmanagedBytes, pBytes.Length);
                        bSuccess = WritePrinter(hPrinter, pUnmanagedBytes, pBytes.Length, out int dwWritten);
                        System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pUnmanagedBytes);
                        EndPagePrinter(hPrinter);
                    }
                    EndDocPrinter(hPrinter);
                }
                ClosePrinter(hPrinter);
            }
            return bSuccess;
        }
    }
}
