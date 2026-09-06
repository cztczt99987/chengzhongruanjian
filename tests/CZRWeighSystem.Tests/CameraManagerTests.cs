using CZRWeighSystem;
using CZRWeighSystem.Video;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 摄像头配置与建源逻辑测试：
/// Cameras 列表 JSON 往返一致；BuildSources 按列表逐行建源
/// （有地址 → RtspCameraSource，地址留空 → SimulatedCameraSource，名称留空默认 CAMn）。
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

    [Fact]
    public void BuildSources_UrlFilled_UsesRealSourceWithCustomName()
    {
        var cfg = new AppConfig { Cameras = [new CameraConfig { Name = "大门", Url = " http://x/live.flv " }] };

        var sources = CameraManager.BuildSources(cfg);

        var src = Assert.IsType<RtspCameraSource>(Assert.Single(sources));
        Assert.Equal("大门", src.Name);
    }

    [Fact]
    public void BuildSources_UrlBlank_UsesSimulatedSourceWithDefaultName()
    {
        var cfg = new AppConfig
        {
            Cameras = [new CameraConfig { Name = "", Url = "" }, new CameraConfig { Name = "磅前", Url = "rtsp://10.0.0.8/live" }],
        };

        var sources = CameraManager.BuildSources(cfg);

        Assert.Equal(2, sources.Count);
        Assert.IsType<SimulatedCameraSource>(sources[0]);
        Assert.Equal("CAM1", sources[0].Name);          // 名称留空 → 默认命名
        Assert.IsType<RtspCameraSource>(sources[1]);
        Assert.Equal("磅前", sources[1].Name);
    }

    [Fact]
    public void Cameras_JsonRoundTrip_Preserved()
    {
        var cfg = new AppConfig
        {
            CameraEnabled = false,
            Cameras =
            [
                new CameraConfig { Name = "磅前", Url = "https://rtmp05open.ys7.com:9188/v3/openlive/a.flv?t=x" },
                new CameraConfig { Name = "", Url = "" },
            ],
        };

        var json = System.Text.Json.JsonSerializer.Serialize(cfg);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);

        Assert.NotNull(back);
        Assert.False(back.CameraEnabled);
        Assert.Equal(2, back.Cameras.Count);
        Assert.Equal("磅前", back.Cameras[0].Name);
        Assert.Equal("https://rtmp05open.ys7.com:9188/v3/openlive/a.flv?t=x", back.Cameras[0].Url);
        Assert.Equal("", back.Cameras[1].Name);
    }
}
