namespace CZRWeighSystem.Video;

/// <summary>
/// 模拟摄像头画面源（开发调试用，零硬件依赖）。
/// GDI+ 动态绘制：渐变背景 + 移动色块 + 时间戳 + 摄像头名称，
/// 模拟磅房监控画面效果。正式部署时由 RtspCameraSource 替代。
/// </summary>
public class SimulatedCameraSource : ICameraSource
{
    public string Name { get; }

    public Image? CurrentFrame => _frame;

    public event Action? PreviewFrameUpdated;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };   // 10fps
    private readonly Bitmap _frame;
    private readonly Graphics _graphics;
    private readonly Random _random = new();
    private int _tick;
    private readonly int _seedColor;

    public SimulatedCameraSource(string name)
    {
        Name = name;
        _frame = new Bitmap(640, 360);
        _graphics = Graphics.FromImage(_frame);
        _seedColor = _random.Next(0, 360);
    }

    public void Start() => _timer.Tick += (_, _) => RenderFrame();

    public void Stop() => _timer.Stop();

    /// <summary>绘制一帧模拟画面。</summary>
    private void RenderFrame()
    {
        _tick++;

        // 背景（按摄像头编号区分色调）
        using var bg = new SolidBrush(HsvToColor(_seedColor, 0.15f, 0.25f));
        _graphics.FillRectangle(bg, 0, 0, _frame.Width, _frame.Height);

        // 移动色块模拟车辆移动
        int x = (_tick * 3) % (_frame.Width + 200) - 100;
        using var block = new SolidBrush(HsvToColor(_seedColor + 40, 0.5f, 0.55f));
        _graphics.FillRectangle(block, x, 180, 180, 90);
        using var block2 = new SolidBrush(HsvToColor(_seedColor + 80, 0.4f, 0.7f));
        _graphics.FillEllipse(block2, _frame.Width - x - 60, 80, 60, 60);

        // 角落时间戳（模拟 OSD）
        var now = DateTime.Now;
        using var font = new Font("Consolas", 16F, FontStyle.Bold);
        using var text = new SolidBrush(Color.Lime);
        _graphics.DrawString(Name, font, text, 12, 10);
        _graphics.DrawString(now.ToString("yyyy-MM-dd HH:mm:ss"), font, text, 12, 42);

        PreviewFrameUpdated?.Invoke();
    }

    /// <summary>抓拍当前帧到文件（JPEG）。</summary>
    public bool CaptureTo(string filePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            lock (_frame)
            {
                _frame.Save(filePath, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            Log.Info($"抓拍保存：{filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("抓拍失败：" + filePath, ex);
            return false;
        }
    }

    /// <summary>HSV 转 Color（生成区分度高的色系）。</summary>
    private static Color HsvToColor(float h, float s, float v)
    {
        h = h % 360;
        float c = v * s;
        float x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        float m = v - c;
        (float r, float g, float b) = h switch
        {
            < 60 => (c, x, 0f),
            < 120 => (x, c, 0f),
            < 180 => (0f, c, x),
            < 240 => (0f, x, c),
            < 300 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _graphics.Dispose();
        _frame.Dispose();
    }
}
