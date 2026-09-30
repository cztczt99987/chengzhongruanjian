using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml;

namespace CZRWeighSystem.Video;

/// <summary>SADP 搜索发现的设备信息。</summary>
public class HikDeviceInfo
{
    /// <summary>设备局域网 IP</summary>
    public string Ip { get; init; } = "";

    /// <summary>设备描述/型号（SADP 应答的 DeviceDescription；端口扫描发现的为通用描述）</summary>
    public string Model { get; init; } = "";

    /// <summary>设备 MAC 地址</summary>
    public string Mac { get; init; } = "";

    /// <summary>SDK 服务端口（海康默认 8000；0 表示端口扫描发现、未取得 SDK 端口）</summary>
    public int SdkPort { get; init; }
}

/// <summary>
/// 海康设备局域网搜索，双通道并行：
/// 1) SADP 协议（广播/组播 37020 端口探测，解析 ProbeMatch 应答）—— 专业线设备应答；
/// 2) RTSP 端口扫描兜底 —— 萤石/海康消费系列（CS-C/CS-H 等）不响应 SADP，
///    改为并行探测本机各 /24 网段的 554（RTSP）端口；仅 8000（海康 SDK）开放
///    而 554 关闭的设备作为"本地服务未开启"提示返回，便于界面指导用户开启。
/// </summary>
public static class HikDeviceDiscovery
{
    /// <summary>SADP 协议固定端口</summary>
    private const int SadpPort = 37020;

    /// <summary>
    /// 搜索局域网内海康/RTSP 摄像头（总耗时约 timeoutMs + 2 秒，UI 调用须放后台线程）。
    /// 返回：可用设备列表（SADP 应答优先、带型号；扫描发现标注 RTSP）+
    /// 疑似海康但 554/RTSP 未开启的设备 IP（提示用户去萤石云 App 开启）。
    /// </summary>
    public static (List<HikDeviceInfo> Devices, List<string> RtspDisabledCandidates) Search(int timeoutMs = 3000)
    {
        // SADP 与端口扫描并行执行，萤石系设备不响应 SADP 时扫描结果兜底
        var sadpTask = Task.Run(() => SadpSearch(timeoutMs));
        var scanTask = Task.Run(ScanLanPorts);
        try { Task.WaitAll([sadpTask, scanTask], timeoutMs + 20000); }
        catch (AggregateException) { /* 单侧失败用另一侧结果 */ }

        var devices = new Dictionary<string, HikDeviceInfo>();
        foreach (var d in sadpTask.Status == TaskStatus.RanToCompletion ? sadpTask.Result : [])
            devices[d.Ip] = d;   // SADP 结果优先（带型号/MAC）
        foreach (var ip in scanTask.Status == TaskStatus.RanToCompletion ? scanTask.Result.RtspOpen : [])
            if (!devices.ContainsKey(ip))
                devices[ip] = new HikDeviceInfo { Ip = ip, Model = "网络摄像头（RTSP）" };

        // 已可用设备不再列入"未开启"提示
        var candidates = scanTask.Status == TaskStatus.RanToCompletion
            ? scanTask.Result.SdkOnly.Where(ip => !devices.ContainsKey(ip)).ToList()
            : [];
        return (devices.Values.OrderBy(d => d.Ip).ToList(), candidates);
    }

    /// <summary>
    /// SADP 探测：向广播/组播 37020 发探测报文，收集设备单播回的 ProbeMatch XML。
    /// </summary>
    private static List<HikDeviceInfo> SadpSearch(int timeoutMs)
    {
        var devices = new Dictionary<string, HikDeviceInfo>();   // 按 IP 去重
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            udp.EnableBroadcast = true;

            // SADP 探测报文（ inquiry ）
            string probe =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                $"<Probe><Uuid>{Guid.NewGuid()}</Uuid><Types>inquiry</Types></Probe>";
            byte[] data = Encoding.UTF8.GetBytes(probe);

            // 全网广播 + 组播双发，尽量覆盖各类网络环境
            udp.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, SadpPort));
            udp.Send(data, data.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), SadpPort));

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                int remainMs = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remainMs <= 0) break;
                udp.Client.ReceiveTimeout = remainMs;
                try
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] resp = udp.Receive(ref remote);
                    foreach (var d in ParseResponse(Encoding.UTF8.GetString(resp)))
                        devices[d.Ip] = d;
                }
                catch (SocketException)
                {
                    break;   // 等待超时，收包结束
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("海康设备 SADP 搜索失败（将自动改用端口扫描兜底）", ex);
        }
        return devices.Values.OrderBy(d => d.Ip).ToList();
    }

    /// <summary>端口扫描结果：554 开放（可用）与仅 8000 开放（本地服务未开启）的 IP。</summary>
    private record LanScanResult(List<string> RtspOpen, List<string> SdkOnly);

    /// <summary>
    /// 并行探测本机所有 /24 局域网网段的 554（RTSP）与 8000（海康 SDK）端口。
    /// 554 开放 = 可直接取流；仅 8000 开放 = 疑似海康/萤石设备但本地服务未开启。
    /// </summary>
    private static LanScanResult ScanLanPorts()
    {
        var rtspOpen = new System.Collections.Concurrent.ConcurrentBag<string>();
        var sdkOnly = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = new List<Task>();
        foreach (string prefix in GetScanPrefixes())
        {
            for (int i = 1; i <= 254; i++)
            {
                string ip = $"{prefix}.{i}";
                tasks.Add(Task.Run(() =>
                {
                    bool rtsp = TryConnect(ip, 554);
                    if (rtsp) rtspOpen.Add(ip);
                    else if (TryConnect(ip, 8000)) sdkOnly.Add(ip);
                }));
            }
        }
        try { Task.WaitAll([.. tasks], 20000); }
        catch (AggregateException) { /* 单个连接任务失败不影响整体 */ }

        return new LanScanResult(rtspOpen.OrderBy(x => x).ToList(), sdkOnly.OrderBy(x => x).ToList());
    }

    /// <summary>取本机所有 IPv4 局域网 /24 网段前缀（排除回环/链路本地/非 /24 的虚拟网段）。</summary>
    private static List<string> GetScanPrefixes()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = addr.Address.ToString();
                    if (ip.StartsWith("127.") || ip.StartsWith("169.254.")) continue;
                    if (addr.PrefixLength != 24) continue;   // 只扫标准 C 段（WSL 等虚拟网段多为 /20 自动排除）
                    string prefix = string.Join('.', ip.Split('.')[0..3]);
                    if (!list.Contains(prefix)) list.Add(prefix);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("获取端口扫描网段失败", ex);
        }
        return list;
    }

    /// <summary>快速 TCP 探测端口是否连通。</summary>
    private static bool TryConnect(string ip, int port)
    {
        try
        {
            using var client = new TcpClient();
            IAsyncResult ar = client.BeginConnect(ip, port, null, null);
            if (ar.AsyncWaitHandle.WaitOne(500) && client.Connected) return true;
            client.Close();
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 解析 SADP 应答 XML（可能含多个 ProbeMatch，或一个设备一个报文）。
    /// 单根报文直接解析；多根混杂报文包一层伪根节点后统一取 //ProbeMatch。
    /// </summary>
    internal static List<HikDeviceInfo> ParseResponse(string xml)
    {
        var list = new List<HikDeviceInfo>();
        if (string.IsNullOrWhiteSpace(xml)) return list;

        // 剥离 XML 声明：声明只能位于文档顶层，包伪根节点后会变成非法子节点
        xml = xml.TrimStart();
        if (xml.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            int end = xml.IndexOf("?>", StringComparison.Ordinal);
            if (end >= 0) xml = xml[(end + 2)..].TrimStart();
        }

        var doc = new XmlDocument();
        try
        {
            try { doc.LoadXml(xml); }
            catch (XmlException) { doc.LoadXml($"<r>{xml}</r>"); }   // 多根：包伪根节点再解析
        }
        catch (XmlException)
        {
            return list;   // 非法报文直接忽略
        }

        foreach (XmlNode m in doc.SelectNodes("//ProbeMatch")!)
        {
            string ip = m["IPv4Address"]?.InnerText.Trim() ?? "";
            // 非 IPv4 的应答（如设备回 IPv6）不进清单
            if (ip == "" || !IPAddress.TryParse(ip, out _)) continue;
            list.Add(new HikDeviceInfo
            {
                Ip = ip,
                Model = m["DeviceDescription"]?.InnerText.Trim() ?? "",
                Mac = m["MACAddress"]?.InnerText.Trim() ?? "",
                SdkPort = int.TryParse(m["PortNo"]?.InnerText.Trim(), out int p) ? p : 8000,
            });
        }
        return list;
    }
}
