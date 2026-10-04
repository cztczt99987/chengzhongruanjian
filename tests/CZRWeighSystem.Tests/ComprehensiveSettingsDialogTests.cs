using CZRWeighSystem.UI;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 综合设置窗体测试（对应目标软件"综合设置 [F3]"）：
/// 1) 构造冒烟：5 个页签齐全、标题正确，内嵌串口/联网/平台对接/视频设置不抛异常；
/// 2) 基本设置页保存：界面值逐项写入 AppConfig（公司信息/打印次数/省份/稳定秒数/锁屏/归零拦截/自动清除/二次称重禁止/黑匣子）。
/// </summary>
public class ComprehensiveSettingsDialogTests
{
    [Fact]
    public void Construct_HasFiveTabs_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
        {
            using var f = new ComprehensiveSettingsDialog();
            Assert.Equal("综合设置 [F3]", f.Text);
            Assert.Equal(5, f.Tabs.TabPages.Count);
            Assert.Contains("基本设置", f.Tabs.TabPages[0].Text);
            Assert.Contains("高级设置", f.Tabs.TabPages[1].Text);
            Assert.Contains("联网设置", f.Tabs.TabPages[2].Text);
            Assert.Contains("平台对接", f.Tabs.TabPages[3].Text);
            Assert.Contains("视频设置", f.Tabs.TabPages[4].Text);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void SaveBasic_PersistsAllBasicSettings()
    {
        using var f = new ComprehensiveSettingsDialog();
        var messages = new List<string>();
        f.InfoBox = (msg, title, ok) => messages.Add(msg);   // 注入提示框，静默保存

        f.TxtCompany.Text = " 精锐计量 ";
        f.TxtHeader.Text = " 过磅单 ";
        f.TxtFooter.Text = " 谢谢惠顾 ";
        f.ChkPrintCount.Checked = true;
        f.NumMaxPrint.Value = 3;
        f.CboProvince.SelectedItem = "吉";
        f.CboLetter.SelectedItem = "A";
        f.ChkUseProvinceLetter.Checked = false;
        f.ChkStableSeconds.Checked = true;
        f.CboStableSeconds.SelectedItem = 10;
        f.NumIdleLock.Value = 30;
        f.ChkBlockZero.Checked = true;
        f.CboExpireDays.SelectedItem = 15;
        f.CboForbidMinutes.SelectedItem = 15;
        f.ChkBlackBox.Checked = false;
        f.NumBlackBoxMinKg.Value = 150;
        f.NumBlackBoxSeconds.Value = 20;

        f.SaveBasic();

        var cfg = AppConfig.Current;
        Assert.Equal("精锐计量", cfg.CompanyName);
        Assert.Equal("过磅单", cfg.SlipHeader);
        Assert.Equal("谢谢惠顾", cfg.SlipFooter);
        Assert.True(cfg.PrintCountEnabled);
        Assert.Equal(3, cfg.MaxPrintCount);
        Assert.Equal("吉", cfg.DefaultProvince);
        Assert.Equal("A", cfg.DefaultLetter);
        Assert.False(cfg.UseProvinceLetter);
        Assert.Equal(10, cfg.StableSeconds);
        Assert.Equal(30, cfg.IdleLockMinutes);
        Assert.True(cfg.BlockSaveUntilZero);
        Assert.Equal(15, cfg.FirstWeighExpireDays);
        Assert.Equal(15, cfg.ForbidReweighMinutes);
        Assert.False(cfg.BlackBoxEnabled);
        Assert.Equal(150, cfg.BlackBoxMinKg);
        Assert.Equal(20, cfg.BlackBoxSeconds);
        Assert.Single(messages);   // 保存成功提示恰好一次

        // 还原，避免影响其他测试
        cfg.StableSeconds = 0;
        cfg.BlockSaveUntilZero = false;
        cfg.ForbidReweighMinutes = 0;
        cfg.Save();
    }

    [Fact]
    public void 空车去皮差值预警_回显当前值_保存后写入配置()
    {
        var cfg = CZRWeighSystem.AppConfig.Current;
        double old = cfg.TareDiffWarnTon;
        try
        {
            cfg.TareDiffWarnTon = 0.5;
            using var f = new ComprehensiveSettingsDialog();
            Assert.Equal(0.5m, f.NumTareDiffWarnTon.Value);   // 打开即回显配置值
            Assert.Equal(0, f.NumTareDiffWarnTon.Minimum);   // 允许 0=关闭

            f.NumTareDiffWarnTon.Value = 1.25m;
            f.SaveBasic();
            Assert.Equal(1.25, cfg.TareDiffWarnTon, 2);
        }
        finally
        {
            cfg.TareDiffWarnTon = old;
            cfg.Save();
        }
    }

    [Fact]
    public void 空车去皮差值预警_输入框与两段文字同一行对齐()
    {
        // 整个用例必须在同一 STA 线程：窗体构造可能已创建控件句柄，
        // 换线程 Show 会跨线程操作句柄而卡死（测试挂起、testhost 无输出）。
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var f = new ComprehensiveSettingsDialog();
                Label? head = null, tail = null;
                void Walk(Control c)
                {
                    foreach (Control child in c.Controls)
                    {
                        if (child is Label l)
                        {
                            if (l.Text.Contains("相差超过")) head = l;
                            if (l.Text.Contains("吨时提示")) tail = l;
                        }
                        Walk(child);
                    }
                }
                Walk(f);

                Assert.NotNull(head);
                Assert.NotNull(tail);
                var num = f.NumTareDiffWarnTon;
                Assert.True(num.Top > 0, "差值输入框不能停在分组框顶部（默认 y=0）");
                // 输入框高约 23、标签高约 15：同行基线对齐时两者垂直中线相差数像素（字体度量差异）；
                // 回归前输入框默认 y=0，与标签中线相差约 17px，用 8px 上限可稳定识别"跑到上一行"
                double numMiddle = num.Top + num.Height / 2.0;
                Assert.InRange(Math.Abs(numMiddle - (head!.Top + head.Height / 2.0)), 0, 8);
                Assert.InRange(Math.Abs(numMiddle - (tail!.Top + tail.Height / 2.0)), 0, 8);

                // 末个分组框不得超出基本设置页底部（回归：窗口高 700 时末块底 628 > 页内 623，下边框被裁）
                f.Show();   // 触发 Dock=Fill 布局，未 Show 时页签尺寸未计算
                var page = f.Tabs.TabPages[0];
                var grp = (GroupBox)num.Parent!;
                Assert.True(grp.Bottom <= page.ClientSize.Height - 4,
                    $"末区块底边 {grp.Bottom} 超出基本设置页可用高度 {page.ClientSize.Height}");
                f.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }

    [Fact]
    public void StableSecondsCombo_DisabledWhenUnchecked()
    {
        using var f = new ComprehensiveSettingsDialog();
        f.ChkStableSeconds.Checked = false;
        Assert.False(f.CboStableSeconds.Enabled);
        f.ChkStableSeconds.Checked = true;
        Assert.True(f.CboStableSeconds.Enabled);
    }

    [Fact]
    public void ExpireDaysCombo_OnlyAllowsOneToThirty()
    {
        // 配置值超范围（历史遗留 0/90 等）→ 回落 30
        AppConfig.Current.FirstWeighExpireDays = 90;
        using var f = new ComprehensiveSettingsDialog();

        var items = f.CboExpireDays.Items.Cast<int>().ToList();
        Assert.Equal(30, items.Count);                       // 恰好 1~30 共 30 项
        Assert.Equal(1, items[0]);
        Assert.Equal(30, items[^1]);
        Assert.DoesNotContain(0, items);                     // 不提供"不启用"
        Assert.Equal(30, f.CboExpireDays.SelectedItem);      // 超范围回落 30

        // 范围内配置值正常回显
        AppConfig.Current.FirstWeighExpireDays = 7;
        using var f2 = new ComprehensiveSettingsDialog();
        Assert.Equal(7, f2.CboExpireDays.SelectedItem);

        AppConfig.Current.FirstWeighExpireDays = 7;   // 还原，避免影响其他测试
    }
}
