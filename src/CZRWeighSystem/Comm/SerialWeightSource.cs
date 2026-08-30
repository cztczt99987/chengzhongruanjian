using System.IO.Ports;

namespace CZRWeighSystem.Comm;

/// <summary>
/// 串口称重数据源（对应 PRD 5.6 / F6-01、F6-02）。
/// 数据帧解析采用"通用连续帧"方式：帧头 0x02 ... 帧尾 0x0D，
/// 中间内容剔除小数点、正负号与状态字符后按数字解析，
/// 兼容耀华 XK3190 系列、托利多 IND 等常见仪表的连续发送格式。
/// 注意：各仪表具体位定义请以说明书为准；多协议支持（策略模式）按 F6-02 后续扩展。
/// </summary>
public class SerialWeightSource : IWeightSource
{
    public event Action<WeightData>? WeightReceived;
    public event Action<string>? ErrorOccurred;

    private readonly string _portName;
    private readonly int _baudRate;
    private readonly int _dataBits;
    private readonly Parity _parity;
    private readonly StopBits _stopBits;

    private SerialPort? _port;
    private readonly List<byte> _buffer = new();   // 接收缓冲（粘包/半包处理）

    public SerialWeightSource(string portName, int baudRate, int dataBits,
                              Parity parity, StopBits stopBits)
    {
        _portName = portName;
        _baudRate = baudRate;
        _dataBits = dataBits;
        _parity = parity;
        _stopBits = stopBits;
    }

    public void Start()
    {
        try
        {
            _port = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits)
            {
                ReadTimeout = 500,
                WriteTimeout = 500,
                // 常见仪表为 7 数据位偶校验或 8N1，由配置决定
            };
            _port.DataReceived += OnDataReceived;
            _port.Open();
            Log.Info($"串口已打开：{_portName} {_baudRate} {_dataBits}{_parity}{_stopBits}");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"串口 {_portName} 打开失败：{ex.Message}");
            Log.Error("串口打开失败", ex);
        }
    }

    public void Stop()
    {
        try
        {
            if (_port is { IsOpen: true })
            {
                _port.DataReceived -= OnDataReceived;
                _port.Close();
                Log.Info("串口已关闭");
            }
        }
        catch (Exception ex)
        {
            Log.Error("串口关闭异常", ex);
        }
    }

    public string Describe() => $"串口 {_portName} @{_baudRate}";

    /// <summary>
    /// 串口接收事件（注意：运行于线程池线程，非 UI 线程）。
    /// </summary>
    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            if (_port is not { IsOpen: true }) return;

            int n = _port.BytesToRead;
            if (n <= 0) return;
            var buf = new byte[n];
            _port.Read(buf, 0, n);

            lock (_buffer)
            {
                _buffer.AddRange(buf);
                ParseBuffer();
            }
        }
        catch (Exception ex)
        {
            Log.Error("串口读取异常", ex);
            ErrorOccurred?.Invoke("串口读取异常：" + ex.Message);
        }
    }

    /// <summary>
    /// 从缓冲中提取完整帧：0x02 ... 0x0D，并解析重量。
    /// </summary>
    private void ParseBuffer()
    {
        while (_buffer.Count > 0)
        {
            // 1. 定位帧头
            if (_buffer[0] != 0x02)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            // 2. 查找帧尾
            int end = _buffer.IndexOf((byte)0x0D);
            if (end < 0)
            {
                // 半包：数据不足一帧，等待下次接收（超长保护）
                if (_buffer.Count > 64) _buffer.Clear();
                return;
            }

            // 3. 提取帧体并解析
            byte[] frame = _buffer.GetRange(0, end).ToArray();
            _buffer.RemoveRange(0, end + 1);

            double? kg = ParseFrame(frame);
            if (kg.HasValue)
                WeightReceived?.Invoke(new WeightData(kg.Value, DateTime.Now));
        }
    }

    /// <summary>
    /// 解析单帧：剔除小数点/符号/状态字符后按数字解析。
    /// 返回 null 表示本帧无效。
    /// </summary>
    private static double? ParseFrame(byte[] frame)
    {
        // frame[0]=0x02 帧头，frame[1] 通常为状态字节
        var sb = new System.Text.StringBuilder();
        int dotPos = -1;

        for (int i = 1; i < frame.Length; i++)
        {
            char c = (char)frame[i];
            if (c >= '0' && c <= '9')
            {
                sb.Append(c);
            }
            else if (c == '.' && dotPos < 0)
            {
                dotPos = sb.Length; // 记录小数点位置
            }
            // 其余字符（+/-/状态位）忽略
        }

        if (sb.Length == 0) return null;
        if (!long.TryParse(sb.ToString(), out long raw)) return null;

        // 仪表显示值通常为千克；按小数点位置还原
        double display = dotPos < 0 ? raw : raw / Math.Pow(10, sb.Length - dotPos);
        return display;
    }

    public void Dispose() => Stop();
}
