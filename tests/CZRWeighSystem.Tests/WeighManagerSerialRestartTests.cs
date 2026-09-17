using CZRWeighSystem.Core;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 串口参数保存后立即生效的回归测试（2026-09-17 用户反馈）：
/// 现场部署时发布包首启为默认串口 COM1，用户在「串口参数设置」改成实际端口并保存后，
/// 旧版软件不会重连串口（且提示里让点的菜单项已不存在），必须重启软件才通——
/// 现要求保存即按新参数重连。
/// </summary>
[Collection("数据库顺序")]
public class WeighManagerSerialRestartTests
{
    /// <summary>
    /// 重启数据源后必须使用 config.json 中的新串口参数（本机无此端口，打开失败属预期，
    /// 但状态栏数据源描述应反映新端口，证明已重建数据源而非沿用旧参数）。
    /// </summary>
    [Fact]
    public void RestartSerialSource_AppliesNewPortWithoutRestart()
    {
        var cfg = AppConfig.Current;
        string originalPort = cfg.PortName;

        try
        {
            cfg.PortName = "COM_TEST_OLD";
            using var manager = new WeighManager();
            manager.Start();
            Assert.Contains("COM_TEST_OLD", manager.SourceDescription);

            cfg.PortName = "COM_TEST_NEW";   // 模拟用户在「串口参数设置」中改端口并保存
            manager.RestartSerialSource();

            Assert.Contains("COM_TEST_NEW", manager.SourceDescription);
        }
        finally
        {
            cfg.PortName = originalPort;     // 还原，避免影响其他测试
        }
    }
}