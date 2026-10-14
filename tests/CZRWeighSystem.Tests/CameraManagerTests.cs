using CZRWeighSystem;
using CZRWeighSystem.Video;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 摄像头配置与建源逻辑测试：
/// Cameras 列表 JSON 往返一致；BuildSources 按列表逐条建源
/// （IP 已填 → RtspCameraSource，IP 留空 = 未启用直接跳过，名称留空默认 CAMn）；
/// RTSP 取流地址按 IP/端口/用户名/密码自动生成（特殊字符做 URL 转义）。
/// BuildSources 只构建不启动，不会触发 VLC 原生库加载。
/// </summary>
public class CameraManagerTests
{
    [Fact]
    public void BuildSources_EmptyList_ReturnsNone()
    {
        var sources = CameraManager.BuildSources(new AppConfig());
        Assert.Empty(sources);
    }

    /// <summary>
    /// 手动拍照（放大画面右键菜单）：存到独立的 manualCaptures/yyyyMMdd/ 目录（不与称重抓拍混放），
    /// 文件名带 cam 序号；序号越界返回 null。
    /// </summary>
    [Fact]
    public void CaptureManual_保存到manualCaptures目录_越界返回null()
    {
        var manager = new CameraManager();
        string? saved = null;
        try
        {
            var sources = (List<ICameraSource>)typeof(CameraManager)
                .GetField("_sources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(manager)!;
            sources.Add(new SimulatedCameraSource("CAM1"));   // 模拟源不启动也有现成帧可拍

            Assert.Null(manager.CaptureManual(-1));
            Assert.Null(manager.CaptureManual(9));

            saved = manager.CaptureManual(0);
            Assert.NotNull(saved);
            Assert.True(File.Exists(saved), $"手动拍照文件应存在：{saved}");
            Assert.Contains("manualCaptures", saved);
            Assert.Contains(DateTime.Now.ToString("yyyyMMdd"), saved);
            Assert.Contains("cam1", Path.GetFileName(saved));
        }
        finally
        {
            if (saved != null && File.Exists(saved))
            {
                Directory.Delete(Path.GetDirectoryName(saved)!, recursive: true);
            }
            manager.Dispose();
        }
    }

    [Fact]
    public void BuildSources_IpFilled_UsesRealSourceWithCustomName()
    {
        var cfg = new AppConfig
        {
            Cameras = [new CameraConfig { Name = "大门", Ip = " 192.168.1.64 " }],
        };

        var sources = CameraManager.BuildSources(cfg);

        var src = Assert.IsType<RtspCameraSource>(Assert.Single(sources));
        Assert.Equal("大门", src.Name);
    }

    [Fact]
    public void BuildSources_IpBlank_SkippedKeepsFilledCameras()
    {
        var cfg = new AppConfig
        {
            Cameras = [new CameraConfig { Name = "", Ip = "" }, new CameraConfig { Name = "磅前", Ip = "10.0.0.8" }],
        };

        var sources = CameraManager.BuildSources(cfg);

        // IP 留空 = 未启用：不建源，已填的一路不受影响
        var src = Assert.IsType<RtspCameraSource>(Assert.Single(sources));
        Assert.Equal("磅前", src.Name);
    }

    [Fact]
    public void BuildSources_NameBlank_UsesOrderBasedDefaultName()
    {
        var cfg = new AppConfig
        {
            Cameras = [new CameraConfig { Ip = "10.0.0.8" }, new CameraConfig { Name = "磅后", Ip = "" }],
        };

        var sources = CameraManager.BuildSources(cfg);

        Assert.Equal("CAM1", Assert.Single(sources).Name);   // 名称留空 → 按启用顺序默认命名
    }

    [Fact]
    public void BuildRtspUrl_Defaults_UsesHikStandardPath()
    {
        var cam = new CameraConfig { Ip = "192.168.1.64" };
        Assert.Equal("rtsp://admin:@192.168.1.64:554/Streaming/Channels/101", cam.BuildRtspUrl());
    }

    [Fact]
    public void BuildRtspUrl_SpecialCharsInPassword_UrlEscaped()
    {
        // 密码含 @ : / 等特殊字符时必须转义，否则 RTSP 地址解析错乱
        var cam = new CameraConfig { Ip = "192.168.1.64", Port = 555, User = "ad@min", Password = "p@ss:1/w" };
        Assert.Equal("rtsp://ad%40min:p%40ss%3A1%2Fw@192.168.1.64:555/Streaming/Channels/101", cam.BuildRtspUrl());
    }

    [Fact]
    public void Cameras_JsonRoundTrip_Preserved()
    {
        var cfg = new AppConfig
        {
            CameraEnabled = false,
            Cameras =
            [
                new CameraConfig { Name = "磅前", Ip = "192.168.1.64", Port = 554, User = "admin", Password = "abc123" },
                new CameraConfig { Name = "", Ip = "" },
            ],
        };

        var json = System.Text.Json.JsonSerializer.Serialize(cfg);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);

        Assert.NotNull(back);
        Assert.False(back.CameraEnabled);
        Assert.Equal(2, back.Cameras.Count);
        Assert.Equal("磅前", back.Cameras[0].Name);
        Assert.Equal("192.168.1.64", back.Cameras[0].Ip);
        Assert.Equal(554, back.Cameras[0].Port);
        Assert.Equal("admin", back.Cameras[0].User);
        Assert.Equal("abc123", back.Cameras[0].Password);
        Assert.Equal("", back.Cameras[1].Name);
        Assert.True(back.Cameras[1].IsBlank);
    }

    [Fact]
    public void Cameras_JsonMissingFields_FallBackToHikDefaults()
    {
        // 旧配置文件缺端口/用户名字段时，反序列化应回落海康默认值（554/admin）
        const string json = """{"Cameras":[{"Name":"磅前","Ip":"192.168.1.64"}]}""";
        var back = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);

        Assert.NotNull(back);
        var cam = Assert.Single(back.Cameras);
        Assert.Equal(554, cam.Port);
        Assert.Equal("admin", cam.User);
    }
}
