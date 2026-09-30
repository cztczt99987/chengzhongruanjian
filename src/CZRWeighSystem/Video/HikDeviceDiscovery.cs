using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;

namespace CZRWeighSystem.Video;

/// <summary>SADP 搜索发现的设备信息。</summary>
public class HikDeviceInfo
{
    /// <summary>设备局域网 IP</summary>
    public string Ip { get; init; } = "";

    /// <summary>设备描述/型号（如 "IP Camera"、"NVR..."）</summary>
    public string Model { get; init; } = "";

    /// <summary>设备 MAC 地址</summary>
    public string Mac { get; init; } = "";

    /// <summary>SDK 服务端口（海康默认 8000；RTSP 端口固定 554，不在 SADP 应答中）</summary>
    public int SdkPort { get; init; }
}

/// <summary>
/// 海康设备局域网搜索（SADP 协议，即参考软件"海康威视设备IP搜索"按钮的原理）。
/// 向广播/组播地址 37020 端口发探测报文，海康设备以单播回 ProbeMatch XML
/// （含 IP、型号、MAC），解析汇总即得设备清单。
/// </summary>
public static class HikDeviceDiscovery
{
    /// <summary>SADP 协议固定端口</summary>
    private const int SadpPort = 37020;

    /// <summary>
    /// 搜索局域网内海康设备（阻塞约 timeoutMs 毫秒，UI 调用须放后台线程）。
    /// </summary>
    public static List<HikDeviceInfo> Search(int timeoutMs = 3000)
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
            Log.Error("海康设备搜索失败（防火墙拦截或多网卡隔离时可能搜索不到，可改手动填写 IP）", ex);
        }
        return devices.Values.OrderBy(d => d.Ip).ToList();
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
