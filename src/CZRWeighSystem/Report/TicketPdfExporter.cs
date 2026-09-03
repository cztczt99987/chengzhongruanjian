using CZRWeighSystem.Core;
using System.Drawing.Printing;

namespace CZRWeighSystem.Report;

/// <summary>
/// 磅单 PDF 导出（借助 Windows 10+ 内置的 "Microsoft Print to PDF" 虚拟打印机）。
/// 与实际打印共用同一渲染管线（PoundTicketPrinter），保证导出 PDF 与打印格式一致。
/// </summary>
public static class TicketPdfExporter
{
    /// <summary>虚拟打印机名称（Windows 10/11 系统自带）</summary>
    public const string PrinterName = "Microsoft Print to PDF";

    /// <summary>本机是否安装了 PDF 虚拟打印机。</summary>
    public static bool IsAvailable()
        => PrinterSettings.InstalledPrinters.Cast<string>()
               .Any(p => p.Equals(PrinterName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 批量导出磅单 PDF 到指定目录（每张一个文件，按单张 1 联渲染）。
    /// 返回（成功张数, 失败信息列表）。
    /// </summary>
    public static (int OkCount, List<string> Errors) ExportBatch(
        IReadOnlyList<WeighRecord> records, string dir)
    {
        var errors = new List<string>();
        int ok = 0;
        Directory.CreateDirectory(dir);
        foreach (var r in records)
        {
            try
            {
                string file = Path.Combine(dir, SafeFileName($"磅单_{r.SerialNo}") + ".pdf");
                using var doc = PoundTicketPrinter.BuildRecordDocument(r, copies: 1);
                doc.PrinterSettings.PrinterName = PrinterName;
                doc.PrinterSettings.PrintToFile = true;
                doc.PrinterSettings.PrintFileName = file;
                doc.Print();
                ok++;
            }
            catch (Exception ex)
            {
                Log.Error($"导出磅单 PDF 失败：{r.SerialNo}", ex);
                errors.Add($"{r.SerialNo}：{ex.Message}");
            }
        }
        if (ok > 0)
            Log.Info($"批量导出磅单 PDF：成功 {ok} 张，失败 {errors.Count} 张 → {dir}");
        return (ok, errors);
    }

    /// <summary>文件名清理：把非法文件名字符替换为下划线。</summary>
    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
