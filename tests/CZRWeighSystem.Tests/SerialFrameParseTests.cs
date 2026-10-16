using CZRWeighSystem.Comm;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 串口仪表帧解析测试。
/// 帧样例取自 2026-09-06 闪跳排查的真实抓帧：
///   60kg 帧：02 2B "00006001" 44('D') 03 —— 状态字节 0x44 非数字字符，正常；
///   80kg 帧：02 2B "00008001" 33('3') 03 —— 状态字节 0x33 恰为数字字符，
///   修复前被拼入重量得 80013 → 800.13 → 800 kg，即"60 闪 800"的根因。
/// 修复后重量数字最多截取 8 位，其后的数字字符视为状态字节忽略。
/// </summary>
public class SerialFrameParseTests
{
    private const int DecimalDigits = 2; // 与 config.json WeightDecimalDigits 一致

    [Fact]
    public void SixtyKgFrame_ParsesTo60()
    {
        byte[] frame = { 0x02, 0x2B, 0x30, 0x30, 0x30, 0x30, 0x36, 0x30, 0x30, 0x31, 0x44 };
        Assert.Equal(60, SerialWeightSource.ParseFrame(frame, DecimalDigits));
    }

    [Fact]
    public void EightyKgFrame_DigitLikeStatusByte_NotGluedIntoWeight()
    {
        byte[] frame = { 0x02, 0x2B, 0x30, 0x30, 0x30, 0x30, 0x38, 0x30, 0x30, 0x31, 0x33 };
        Assert.Equal(80, SerialWeightSource.ParseFrame(frame, DecimalDigits));
    }

    [Fact]
    public void HeavyTruckWeight_DigitLikeStatusByte_NotGluedIntoWeight()
    {
        // 55264.00 kg → "05526400" + 状态 '3'（大车满磅场景同样受状态字节影响）
        byte[] frame = { 0x02, 0x2B, 0x30, 0x35, 0x35, 0x32, 0x36, 0x34, 0x30, 0x30, 0x33 };
        Assert.Equal(55264, SerialWeightSource.ParseFrame(frame, DecimalDigits));
    }

    [Fact]
    public void ExplicitDotFrame_ParsesCorrectly()
    {
        // 兼容带显式小数点的帧："80.00"
        byte[] frame = { 0x02, 0x2B, 0x38, 0x30, 0x2E, 0x30, 0x30 };
        Assert.Equal(80, SerialWeightSource.ParseFrame(frame, DecimalDigits));
    }

    [Fact]
    public void FrameWithoutDigits_ReturnsNull()
    {
        byte[] frame = { 0x02, 0x2B, 0x44 };
        Assert.Null(SerialWeightSource.ParseFrame(frame, DecimalDigits));
    }

    [Fact]
    public void NegativeWeightFrame_ParsesToNegativeValue()
    {
        // 负重量（皮重校验/传感器反向受力场景）：符号位 '-'=0x2D，其余与 60kg 帧相同
        byte[] frame = { 0x02, 0x2D, 0x30, 0x30, 0x30, 0x30, 0x36, 0x30, 0x30, 0x31, 0x44 };
        Assert.Equal(-60, SerialWeightSource.ParseFrame(frame, DecimalDigits));
    }

    [Fact]
    public void NegativeWeightWithExplicitDot_ParsesToNegativeValue()
    {
        // 带显式小数点的负重量："-80.00"
        byte[] frame = { 0x02, 0x2D, 0x38, 0x30, 0x2E, 0x30, 0x30 };
        Assert.Equal(-80, SerialWeightSource.ParseFrame(frame, DecimalDigits));
    }
}
