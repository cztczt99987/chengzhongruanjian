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
