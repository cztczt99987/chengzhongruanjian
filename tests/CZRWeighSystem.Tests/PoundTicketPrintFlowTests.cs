using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.Report;
using CZRWeighSystem.UI;
using System.Drawing.Printing;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 磅单打印输出路径测试（2026-10-04 用户要求：点"打印"直接出单，不再弹系统打印机选择框）：
/// Print / PrintBatch 必须把构建好的文档直接交给输出动作（默认 = 打印到系统默认打印机），
/// 中途不得再经过 PrintDialog。测试替换 OutputAction 拦截真实出纸；
/// 若实现回退为弹打印机选择框，测试将收不到文档而失败。
/// 并覆盖打印预览窗口底部新增的「打 印」按钮（2026-10-13 用户要求：预览中直接打印）。
/// 与 PrintLimitTests 同集合串行执行，避免共享 AppConfig.Current 与 SQLite 库互相干扰。
/// </summary>
[Collection("数据库顺序")]
public class PoundTicketPrintFlowTests : IDisposable
{
    private readonly WeighRecordRepository _repo = new();
    private readonly AppConfig _cfg = AppConfig.Current;
    private readonly Action<PrintDocument> _savedOutput = PoundTicketPrinter.OutputAction;

    static PoundTicketPrintFlowTests() => Db.Initialize();   // 幂等建表

    public void Dispose()
    {
        PoundTicketPrinter.OutputAction = _savedOutput;
        _cfg.PrintCountEnabled = true;   // 还原默认
        _cfg.MaxPrintCount = 5;
    }

    /// <summary>插入一条唯一车号的未完成记录（共享库会残留上次运行数据）。</summary>
    private WeighRecord InsertRecord(string tag)
    {
        string uniq = Guid.NewGuid().ToString("N")[..6];
        return _repo.InsertFirstWeigh(new WeighRecord
        {
            ScaleNo = $"D{uniq}"[..6],
            VehicleNo = $"测{tag}{uniq}",
        });
    }

    [Fact]
    public void 单张打印_直接输出文档_不经过打印机选择对话框()
    {
        _cfg.PrintCountEnabled = true;
        _cfg.MaxPrintCount = 5;
        var record = InsertRecord("D");

        PrintDocument? output = null;
        PoundTicketPrinter.OutputAction = doc => output = doc;   // 拦截真实出纸

        bool ok = PoundTicketPrinter.Print(record);

        Assert.True(ok);
        Assert.NotNull(output);   // 文档已直接交给输出动作（弹框回退时此处收不到文档）
        Assert.Contains(record.SerialNo, output!.DocumentName);
        // 直接输出后打印次数仍正常累计
        Assert.Equal(1, _repo.GetUnfinishedList().First(x => x.Id == record.Id).PrintCount);
    }

    [Fact]
    public void 批量打印_合并为一个任务直接输出_不经过打印机选择对话框()
    {
        _cfg.PrintCountEnabled = true;
        _cfg.MaxPrintCount = 5;
        var r1 = InsertRecord("B1");
        var r2 = InsertRecord("B2");

        PrintDocument? output = null;
        PoundTicketPrinter.OutputAction = doc => output = doc;

        bool ok = PoundTicketPrinter.PrintBatch(new[] { r1, r2 });

        Assert.True(ok);
        Assert.NotNull(output);
        Assert.Contains("2张", output!.DocumentName);
        Assert.Equal(1, _repo.GetUnfinishedList().First(x => x.Id == r1.Id).PrintCount);
        Assert.Equal(1, _repo.GetUnfinishedList().First(x => x.Id == r2.Id).PrintCount);
    }

    /// <summary>在 STA 线程上执行测试体；线程内异常捕获后回抛（避免未处理异常拖垮测试宿主）。</summary>
    private static void RunOnSta(Action act)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { act(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }

    [Fact]
    public void 磅单预览_底部新增打印按钮_点击即直接出单()
    {
        _cfg.PrintCountEnabled = true;
        _cfg.MaxPrintCount = 5;
        var record = InsertRecord("P");
        record.Status = "已完成";   // 完成结算后才可打印（与记录查询页「补打磅单」一致）

        PrintDocument? output = null;
        PoundTicketPrinter.OutputAction = doc => output = doc;   // 拦截真实出纸

        RunOnSta(() =>
        {
            using var preview = PoundTicketPrinter.BuildPreview(record, allowPrint: true);

            // 预览中新增的「打 印」按钮：位于底部操作栏内，且不与预览区重叠
            var print = (Button)preview.Controls.Find("btnPreviewPrint", true).Single();
            Assert.Equal("打 印", print.Text);
            var bar = (FlowLayoutPanel)preview.Controls.Find("previewActionBar", true).Single();
            Assert.Same(bar, print.Parent);
            var area = preview.Controls.OfType<PrintPreviewControl>().Single();
            Assert.True(bar.Top >= area.Bottom - 1,
                $"操作栏(顶={bar.Top})与预览区(底={area.Bottom})重叠");
            Assert.NotNull(area.Document);   // 预览区已绑定磅单文档

            // 按钮点击动作 = PrintFromPreview：直接打印该磅单
            bool saved = PrintingIndicator.Suppressed;   // 打印提示小窗不弹（测试环境）
            PrintingIndicator.Suppressed = true;
            try { PoundTicketPrinter.PrintFromPreview(record, preview); }
            finally { PrintingIndicator.Suppressed = saved; }
        });

        Assert.NotNull(output);   // 点击后直接出单（弹打印机选择框时此处收不到文档）
        Assert.Contains(record.SerialNo, output!.DocumentName);
        Assert.Equal(1, _repo.GetUnfinishedList().First(x => x.Id == record.Id).PrintCount);
    }

    [Fact]
    public void 磅单预览_不提供打印入口时_仅有预览不出单()
    {
        var record = new WeighRecord { Status = "已完成" };   // 打印设置里的样张预览场景
        RunOnSta(() =>
        {
            using var preview = PoundTicketPrinter.BuildPreview(record, allowPrint: false);
            Assert.Empty(preview.Controls.Find("btnPreviewPrint", true));
        });
    }

    [Fact]
    public void 磅单预览打印_仅已完成结算的记录可打印()
    {
        Assert.False(PoundTicketPrinter.CanPrint(new WeighRecord { Status = "未完成" }));
        Assert.True(PoundTicketPrinter.CanPrint(new WeighRecord { Status = "已完成" }));
    }
}