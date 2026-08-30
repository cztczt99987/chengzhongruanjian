using System.Text.Json;

namespace CZRWeighSystem;

/// <summary>
/// 全局配置（对应 PRD 5.10 系统设置）。
/// 保存于 exe 目录下 config.json，修改后调用 Save() 持久化。
/// </summary>
public class AppConfig
{
    /// <summary>当前使用的配置实例</summary>
    public static AppConfig Current { get; private set; } = new();

    // ===== 数据源 =====
    /// <summary>数据源模式：Simulator=模拟数据 / Serial=串口仪表</summary>
    public string DataSourceMode { get; set; } = "Simulator";

    // ===== 数据库（对应 PRD F9-05：单机 SQLite / 网络 SQL Server）=====
    /// <summary>数据库类型：Sqlite=单机 / SqlServer=网络</summary>
    public string DatabaseType { get; set; } = "Sqlite";
    /// <summary>SQL Server 连接字符串（网络版使用；修改后重启生效）</summary>
    public string SqlServerConnectionString { get; set; } =
        "Server=localhost;Database=CZRWeigh;Trusted_Connection=True;TrustServerCertificate=True;";

    // ===== 串口参数（对应 PRD F6-01）=====
    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public string Parity { get; set; } = "None";
    public string StopBits { get; set; } = "One";

    // ===== 称重参数（对应 PRD F10-02）=====
    /// <summary>稳定判定次数：连续 N 次采样一致判定稳定</summary>
    public int StableCount { get; set; } = 3;
    /// <summary>稳定判定容差（千克），采样差值小于该值视为一致</summary>
    public double StableToleranceKg { get; set; } = 5;
    /// <summary>最大量程（千克），超过判定超载</summary>
    public double MaxCapacityKg { get; set; } = 100000;
    /// <summary>显示单位：t=吨 / kg=千克</summary>
    public string Unit { get; set; } = "t";
    /// <summary>磅号（一机双磅时区分两台磅，骨架暂用单磅）</summary>
    public string ScaleNo { get; set; } = "01";

    // ===== 公司信息（对应 PRD F10-01，磅单抬头使用）=====
    /// <summary>公司名称（打印在磅单上）</summary>
    public string CompanyName { get; set; } = "CZR 称重有限公司";

    // ===== 使用细节（对应 PRD 7.2 交互要点 / 4.1 规则 3）=====
    /// <summary>保存操作前是否二次确认（true 更稳妥，熟练后可改 false）</summary>
    public bool ConfirmBeforeSave { get; set; } = true;

    /// <summary>一次磅超过多少小时未完成二次磅则列表标红提醒（4.1 规则 3）</summary>
    public double UnfinishedWarnHours { get; set; } = 24;

    // ===== 数据备份（对应 PRD F11-02 定时自动备份）=====
    /// <summary>是否启用每日自动备份（备份到 exe 目录 backups/）</summary>
    public bool AutoBackupEnabled { get; set; } = true;

    /// <summary>自动备份间隔（天）</summary>
    public int AutoBackupDays { get; set; } = 1;

    /// <summary>自动备份最大保留份数（超出删除最旧的）</summary>
    public int AutoBackupKeep { get; set; } = 30;

    // ===== 视频监控（对应 PRD 5.7 / F7-01）=====
    /// <summary>是否启用视频监控与抓拍</summary>
    public bool CameraEnabled { get; set; } = true;

    /// <summary>摄像头路数（1/2/4）</summary>
    public int CameraCount { get; set; } = 4;

    /// <summary>
    /// 各路 RTSP 地址（分号分隔；留空或不足的路使用模拟画面源）。
    /// 例：rtsp://admin:pwd@192.168.1.64:554/Streaming/Channels/101;rtsp://admin:pwd@192.168.1.65:554/...
    /// 真实画面需接入 LibVLCSharp/FFmpeg 解码 SDK（见 RtspCameraSource 注释）。
    /// </summary>
    public string CameraRtspUrls { get; set; } = "";

    /// <summary>配置文件完整路径</summary>
    private static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "config.json");

    /// <summary>
    /// 从磁盘加载配置；文件不存在时写入默认配置。
    /// </summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null)
                {
                    Current = cfg;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("读取配置失败，使用默认配置", ex);
        }

        // 首次运行：生成默认配置文件
        Current = new AppConfig();
        Current.Save();
    }

    /// <summary>
    /// 保存配置到磁盘。
    /// </summary>
    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, options));
        }
        catch (Exception ex)
        {
            Log.Error("保存配置失败", ex);
        }
    }

    /// <summary>重量显示文本（自动换算单位并保留 2 位小数）</summary>
    public string FormatWeight(double kg)
    {
        double value = Unit == "t" ? kg / 1000.0 : kg;
        return $"{value:0.##} {Unit}";
    }
}
