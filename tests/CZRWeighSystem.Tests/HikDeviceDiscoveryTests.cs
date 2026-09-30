using CZRWeighSystem.Video;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 海康 SADP 设备搜索应答解析测试（不发真实网络报文，只测报文解析）：
/// 单/多 ProbeMatch、缺字段、非法 XML、IPv6 应答剔除。
/// </summary>
public class HikDeviceDiscoveryTests
{
    /// <summary>SADP 真实应答样例（海康摄像头单播回包）</summary>
    private const string SampleProbeMatch =
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <ProbeMatch>
          <Uuid>3c97d8e0-1c2b-4a5f-8c2e-9f1a2b3c4d5e</Uuid>
          <Types>inquiry</Types>
          <DeviceType>IPCamera</DeviceType>
          <DeviceDescription>CS-H5 网络摄像机</DeviceDescription>
          <DeviceSN>CS1234567890</DeviceSN>
          <MACAddress>44-47-CC-12-34-56</MACAddress>
          <IPv4Address>192.168.1.64</IPv4Address>
          <PortNo>8000</PortNo>
          <HttpPortNo>80</HttpPortNo>
        </ProbeMatch>
        """;

    [Fact]
    public void ParseResponse_SingleDevice_Parsed()
    {
        var devices = HikDeviceDiscovery.ParseResponse(SampleProbeMatch);

        var d = Assert.Single(devices);
        Assert.Equal("192.168.1.64", d.Ip);
        Assert.Equal("CS-H5 网络摄像机", d.Model);
        Assert.Equal("44-47-CC-12-34-56", d.Mac);
        Assert.Equal(8000, d.SdkPort);
    }

    [Fact]
    public void ParseResponse_MultipleDevicesInOnePacket_AllParsed()
    {
        const string xml = """
            <ProbeMatch><IPv4Address>192.168.1.64</IPv4Address><PortNo>8000</PortNo></ProbeMatch>
            <ProbeMatch><IPv4Address>192.168.1.65</IPv4Address><PortNo>8000</PortNo></ProbeMatch>
            """;

        var devices = HikDeviceDiscovery.ParseResponse(xml);

        Assert.Equal(2, devices.Count);
        Assert.Equal(["192.168.1.64", "192.168.1.65"], devices.Select(d => d.Ip).ToArray());
    }

    [Fact]
    public void ParseResponse_MissingFields_KeepsIpWithDefaults()
    {
        const string xml = "<ProbeMatch><IPv4Address>192.168.1.66</IPv4Address></ProbeMatch>";

        var d = Assert.Single(HikDeviceDiscovery.ParseResponse(xml));

        Assert.Equal("192.168.1.66", d.Ip);
        Assert.Equal("", d.Model);
        Assert.Equal("", d.Mac);
        Assert.Equal(8000, d.SdkPort);   // 缺 PortNo → 海康默认 8000
    }

    [Fact]
    public void ParseResponse_InvalidXml_ReturnsEmpty()
    {
        Assert.Empty(HikDeviceDiscovery.ParseResponse("not a xml <<<"));
        Assert.Empty(HikDeviceDiscovery.ParseResponse(""));
        Assert.Empty(HikDeviceDiscovery.ParseResponse("   "));
    }

    [Fact]
    public void ParseResponse_IpBlankOrInvalid_Skipped()
    {
        const string xml = """
            <ProbeMatch><IPv4Address></IPv4Address></ProbeMatch>
            <ProbeMatch><IPv4Address>not-an-ip</IPv4Address></ProbeMatch>
            <ProbeMatch><IPv4Address>192.168.1.64</IPv4Address></ProbeMatch>
            """;

        var devices = HikDeviceDiscovery.ParseResponse(xml);

        var d = Assert.Single(devices);
        Assert.Equal("192.168.1.64", d.Ip);
    }
}
