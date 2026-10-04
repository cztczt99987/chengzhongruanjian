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
    /// <summary>仪表帧数字隐含小数位数：帧内数字 = 实际重量 × 10^该值。
    /// 当前仪表为 2（如帧数字 4001 = 40.01kg）；帧内显式带小数点的仪表不受影响。</summary>
    public int WeightDecimalDigits { get; set; } = 2;

    // ===== 称重参数（对应 PRD F10-02）=====
    /// <summary>稳定判定次数：连续 N 次采样一致判定稳定</summary>
    public int StableCount { get; set; } = 3;
    /// <summary>稳定判定容差（千克），采样差值小于该值视为一致</summary>
    public double StableToleranceKg { get; set; } = 5;
    /// <summary>最大量程（千克），超过判定超载</summary>
    public double MaxCapacityKg { get; set; } = 100000;
    /// <summary>显示单位：t=吨 / kg=千克（默认吨）</summary>
    public string Unit { get; set; } = "t";
    /// <summary>磅号（一机双磅时区分两台磅，骨架暂用单磅）</summary>
    public string ScaleNo { get; set; } = "01";

    // ===== 公司信息（对应 PRD F10-01，磅单抬头使用；综合设置-基本设置）=====
    /// <summary>公司名称（打印在磅单上）</summary>
    public string CompanyName { get; set; } = "CZR 称重有限公司";
    /// <summary>磅单抬头（打印磅单的大标题，空则打印"磅 单"）</summary>
    public string SlipHeader { get; set; } = "过磅单";
    /// <summary>磅单底部文字（打印在磅单页脚；空则打印系统默认文案）</summary>
    public string SlipFooter { get; set; } = "";

    // ===== 打印设置（对应 PRD F9-04 / F5-02 / F5-03）=====
    /// <summary>磅单纸张 key：A4 / 241x140 / 241x93（目录见 PoundTicketPrinter.Papers）</summary>
    public string PrintPaper { get; set; } = "A4";
    /// <summary>不打印的磅单字段 key 列表（未列出的字段默认打印，对应 F5-02 字段显隐）</summary>
    public List<string> PrintHiddenFields { get; set; } = [];
    /// <summary>联数 1~3：一式两联/三联时逐页输出（对应 F5-03）</summary>
    public int PrintCopies { get; set; } = 1;
    /// <summary>结算完成后自动打印磅单（false = 每次询问，对应 F5-01）</summary>
    public bool AutoPrintEnabled { get; set; } = false;
    /// <summary>启用的磅单模板文件名（templates/ 目录下；空 = 不用模板，按字段勾选的内置版式）</summary>
    public string PrintTemplateFile { get; set; } = "";

    // ===== 使用细节（对应 PRD 7.2 交互要点 / 4.1 规则 3）=====
    /// <summary>保存操作前是否二次确认（true 更稳妥，熟练后可改 false）</summary>
    public bool ConfirmBeforeSave { get; set; } = true;

    /// <summary>一次磅超过多少小时未完成二次磅则列表标红提醒（4.1 规则 3）</summary>
    public double UnfinishedWarnHours { get; set; } = 24;

    /// <summary>
    /// 首次称重有效天数：一次磅保存后超过该天数仍未配对二次磅，系统自动删除该记录；
    /// 删除后同车号再次称重按首次称重处理（不再提示配对）。0 = 不启用自动删除。
    /// （综合设置-基本设置：自动清除 X 天内未使用的一次称重）
    /// </summary>
    public int FirstWeighExpireDays { get; set; } = 0;

    // ===== 黑匣子（重量稳定持续设定秒数后自动记录重量 + 抓拍）=====
    /// <summary>是否启用黑匣子自动记录</summary>
    public bool BlackBoxEnabled { get; set; } = true;
    /// <summary>触发重量（千克）：稳定重量达到该值才记录（过滤空磅/行人干扰）</summary>
    public double BlackBoxMinKg { get; set; } = 200;
    /// <summary>持续稳定秒数：稳定达到该秒数后记录并抓拍</summary>
    public int BlackBoxSeconds { get; set; } = 10;

    // ===== 综合设置-基本设置（目标软件版式，2026-10-01）=====
    /// <summary>启用磅单"打印次数"累计（每张磅单最多打印 MaxPrintCount 次）</summary>
    public bool PrintCountEnabled { get; set; } = true;
    /// <summary>每个磅单最多允许打印次数（PrintCountEnabled 开启时生效）</summary>
    public int MaxPrintCount { get; set; } = 5;
    /// <summary>默认省份简称（车牌录入预填，如"黑"）</summary>
    public string DefaultProvince { get; set; } = "黑";
    /// <summary>默认发牌机关代号字母（车牌录入预填，如"D"）</summary>
    public string DefaultLetter { get; set; } = "D";
    /// <summary>车牌录入预填省、字母（使用 DefaultProvince + DefaultLetter）</summary>
    public bool UseProvinceLetter { get; set; } = true;
    /// <summary>数据稳定持续秒数（稳定须保持该秒数绿灯亮方可保存；0 = 不启用）</summary>
    public int StableSeconds { get; set; } = 0;
    /// <summary>电脑闲置锁定屏幕分钟数（0 = 不锁定）</summary>
    public int IdleLockMinutes { get; set; } = 900;
    /// <summary>仪表不归零不允许保存数据（防止重复称重）</summary>
    public bool BlockSaveUntilZero { get; set; } = false;
    /// <summary>严格禁止同车 X 分钟内二次称重（距上次结算不足该分钟数拒绝保存；0 = 不限制）</summary>
    public int ForbidReweighMinutes { get; set; } = 0;

    /// <summary>
    /// 空车去皮差值预警阈值（吨）：空车去皮确认时，若本次皮重与该车最近一次已完成记录的皮重
    /// 相差超过该值则弹窗预警（仅提示，不阻止保存）。0 = 关闭预警。
    /// </summary>
    public double TareDiffWarnTon { get; set; } = 0;

    // ===== 收费设置（按重量分段固定金额，参考行业"代磅收费"版式，仅此一种模式）=====
    /// <summary>是否启用收费：二次磅结算时按净重自动计算应收金额</summary>
    public bool FeeEnabled { get; set; } = true;

    /// <summary>其余收费：分段之外的重量按每吨该标准收费（元/吨）</summary>
    public decimal FeeFallbackPerTon { get; set; } = 1;

    /// <summary>
    /// 重量分段固定金额（按净重匹配，含起点、不含终点；ToTon &lt;= 0 表示不封顶）。
    /// 默认值与参考软件一致：0-3吨3元、3-5吨5元、5-10吨10元、10-22吨20元，
    /// 22 吨以上金额为 0（按"其余每吨"标准计费）。
    /// </summary>
    public List<FeeBracket> FeeBrackets { get; set; } =
    [
        new() { FromTon = 0, ToTon = 3, AmountYuan = 3 },
        new() { FromTon = 3, ToTon = 5, AmountYuan = 5 },
        new() { FromTon = 5, ToTon = 10, AmountYuan = 10 },
        new() { FromTon = 10, ToTon = 22, AmountYuan = 20 },
        new() { FromTon = 22, ToTon = 0, AmountYuan = 0 },
    ];

    // ===== 数据备份（对应 PRD F11-02 定时自动备份）=====
    /// <summary>是否启用每日自动备份（备份到 exe 目录 backups/）</summary>
    public bool AutoBackupEnabled { get; set; } = true;

    /// <summary>自动备份间隔（天）</summary>
    public int AutoBackupDays { get; set; } = 1;

    /// <summary>自动备份最大保留份数（超出删除最旧的）</summary>
    public int AutoBackupKeep { get; set; } = 30;

    // ===== 平台对接（公平计量平台推送，见 doc/API对接方案-称重软件.md）=====
    /// <summary>是否启用平台对接（称重记录/照片/黑匣子/作废同步；综合设置-平台对接）</summary>
    public bool PlatformSyncEnabled { get; set; } = false;
    /// <summary>平台 API 地址（如 https://platform.example.com）</summary>
    public string PlatformApiUrl { get; set; } = "";
    /// <summary>接入标识 AppKey（平台管理端"计量站管理"生成）</summary>
    public string PlatformAppKey { get; set; } = "";
    /// <summary>接入密钥 AppSecret（config.json 明文存储已确认；泄露可在管理端重置立即失效）</summary>
    public string PlatformAppSecret { get; set; } = "";
    /// <summary>是否同步黑匣子记录（重量+抓拍留痕）</summary>
    public bool PlatformSyncBlackbox { get; set; } = true;

    // ===== 视频监控（对应 PRD 5.7 / F7-01）=====
    /// <summary>是否启用视频监控与抓拍</summary>
    public bool CameraEnabled { get; set; } = true;

    /// <summary>摄像头列表（最多 3 路，每设置一个即有一路画面；IP 留空的条目不启用、不取流）</summary>
    public List<CameraConfig> Cameras { get; set; } = [];

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

    /// <summary>单位的界面显示文本（内部仍用 t/kg，界面不显示字母 t）</summary>
    public string UnitDisplay => Unit == "t" ? "吨" : "kg";

    /// <summary>重量显示文本（自动换算单位并保留 2 位小数，带汉字单位，如"34.6 吨"）</summary>
    public string FormatWeight(double kg)
    {
        double value = Unit == "t" ? kg / 1000.0 : kg;
        return $"{value:0.##} {UnitDisplay}";
    }

    /// <summary>重量数值文本（自动换算单位、不带单位），供表格单元格使用（单位在表头显示）</summary>
    public string FormatWeightNumber(double kg)
    {
        double value = Unit == "t" ? kg / 1000.0 : kg;
        return value.ToString("0.##");
    }
}

/// <summary>
/// 收费重量分段（按重量区分固定金额）：净重吨数 t 满足 FromTon &lt;= t &lt; ToTon
/// （含起点、不含终点；ToTon &lt;= 0 为不封顶）。固定金额 &lt;= 0 表示该段按"其余每吨"标准计费。
/// </summary>
public class FeeBracket
{
    /// <summary>分段起点（吨，含）</summary>
    public double FromTon { get; set; }

    /// <summary>分段终点（吨，不含）；&lt;= 0 表示不封顶</summary>
    public double ToTon { get; set; }

    /// <summary>该段固定金额（元）；&lt;= 0 表示按"其余每吨"标准计费</summary>
    public decimal AmountYuan { get; set; }
}

/// <summary>
/// 单个摄像头配置（菜单「视频 → 摄像头设置」编辑，本地网络直连）。
/// </summary>
public class CameraConfig
{
    /// <summary>主码流取流路径（海康/萤石通用；播放与"测试连接"共用此常量）</summary>
    public const string DefaultRtspPath = "/Streaming/Channels/101";

    /// <summary>摄像头名称（画面角标显示；留空按 CAM1/CAM2… 默认命名）</summary>
    public string Name { get; set; } = "";

    /// <summary>摄像头局域网 IP（留空 = 该路未启用，不取流）</summary>
    public string Ip { get; set; } = "";

    /// <summary>RTSP 端口（海康默认 554）</summary>
    public int Port { get; set; } = 554;

    /// <summary>登录用户名（海康默认 admin）</summary>
    public string User { get; set; } = "admin";

    /// <summary>登录密码（摄像头激活时设置的管理密码，保存在本机 config.json）</summary>
    public string Password { get; set; } = "";

    /// <summary>是否未配置 IP（未配置 = 该路未启用、不取流）</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Ip);

    /// <summary>
    /// 生成海康取流地址（主码流 101 通道，端口默认 554）：
    /// rtsp://user:pwd@ip:554/Streaming/Channels/101。
    /// 用户名/密码做 URL 转义，兼容含 @ : / 等特殊字符的密码。
    /// </summary>
    public string BuildRtspUrl() =>
        $"rtsp://{Uri.EscapeDataString(User)}:{Uri.EscapeDataString(Password)}" +
        $"@{Ip.Trim()}:{Port}{DefaultRtspPath}";
}
