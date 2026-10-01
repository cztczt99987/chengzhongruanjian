using System.Text.Json;
using CZRWeighSystem.Core;
using CZRWeighSystem.Sync;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 平台推送载荷构建测试（对接文档 §4.3/§4.4 字段映射）：
/// 本地 WeighRecord（地主/司机/货主存 ext_json 固定键）→ 平台 RecordPayload；
/// 黑匣子记录 → BlackboxPayload（无照片 photoId 省略）；序列化字段名与平台契约一致。
/// </summary>
public class SyncPayloadsTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void FromRecord_字段完整映射()
    {
        var record = new WeighRecord
        {
            SerialNo = "20260930-01-0001",
            VehicleNo = "冀E12345",
            Goods = "玉米",
            GrossKg = 32550.501,
            TareKg = 12550.5,
            SecondTime = new DateTime(2026, 9, 30, 10, 35, 0),
            Operator = "管理员",
            ExtJson = WeighRecord.ExtToJson(new Dictionary<string, string>
            {
                ["地主姓名"] = "张三",
                ["地主电话"] = "13800138000",
                ["司机电话"] = "13900139000",
                ["货主姓名"] = "李四",
            }),
        };

        var payload = SyncPayloads.FromRecord(record, ["captures/20260930/20260930-01-0001_cam1"]);

        Assert.Equal("20260930-01-0001", payload.ClientRef);
        Assert.Equal("2026-09-30 10:35:00", payload.WeighTime);   // 取二次磅时间
        Assert.Equal("冀E12345", payload.PlateNo);
        Assert.Equal(32550.5, payload.GrossWeight);               // 两位小数
        Assert.Equal(12550.5, payload.TareWeight);
        Assert.Equal("张三", payload.LandownerName);
        Assert.Equal("13800138000", payload.LandownerPhone);
        Assert.Equal("13900139000", payload.DriverPhone);
        Assert.Equal("李四", payload.CargoOwnerName);
        Assert.Equal("玉米", payload.Variety);
        Assert.Equal("管理员", payload.Weigher);
        Assert.Equal(["captures/20260930/20260930-01-0001_cam1"], payload.Photos);
    }

    [Fact]
    public void FromRecord_序列化字段名与平台契约一致且省略空字段()
    {
        var record = new WeighRecord
        {
            SerialNo = "20260930-01-0002",
            GrossKg = 100,
            TareKg = 50,
            SecondTime = DateTime.Now,
        };
        var payload = SyncPayloads.FromRecord(record, []);
        string json = JsonSerializer.Serialize(payload, JsonOpts);

        Assert.Contains("\"clientRef\":", json);       // 平台 camelCase 契约
        Assert.Contains("\"grossWeight\":100", json);
        Assert.Contains("\"tareWeight\":50", json);
        Assert.Contains("\"landownerPhone\":\"\"", json);   // 必填字段空串保留
        Assert.DoesNotContain("driverPhone", json);         // 空字段省略
        Assert.DoesNotContain("cargoOwnerName", json);
    }

    [Fact]
    public void FromBlackbox_映射与引用号()
    {
        var record = new BlackBoxRecord
        {
            Id = 42,
            Time = new DateTime(2026, 9, 30, 10, 34, 12),
            WeightKg = 32550.5,
        };

        var withPhoto = SyncPayloads.FromBlackbox(record, "blackbox/20260930/103412_123_cam1");
        Assert.Equal("BX42", withPhoto.ClientRef);
        Assert.Equal(32550.5, withPhoto.Weight);
        Assert.Equal("2026-09-30 10:34:12", withPhoto.WeighedAt);
        Assert.Equal("blackbox/20260930/103412_123_cam1", withPhoto.PhotoId);

        Assert.Null(SyncPayloads.FromBlackbox(record, null).PhotoId);   // 无照片省略
    }

    [Fact]
    public void PhotoRefOf_相对路径正斜杠无扩展名()
    {
        string file = Path.Combine(AppContext.BaseDirectory,
            "captures", "20260930", "20260930-01-0001_cam1.jpg");
        Assert.Equal("captures/20260930/20260930-01-0001_cam1", SyncPayloads.PhotoRefOf(file));
    }
}

/// <summary>服务端响应反序列化：photoId/recordNo 兼容数字与字符串两种返回。</summary>
public class ApiResponseDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOpts = new();

    [Fact]
    public void PhotoData_photoId为数字_可解析()
    {
        var photo = JsonSerializer.Deserialize<PhotoData>(
            """{"photoId":3,"url":"/media/a.jpg","duplicate":false}""", JsonOpts);
        Assert.Equal("3", photo!.PhotoId);
        Assert.Equal("/media/a.jpg", photo.Url);
    }

    [Fact]
    public void PhotoData_photoId为字符串_可解析()
    {
        var photo = JsonSerializer.Deserialize<PhotoData>(
            """{"photoId":"ph_9f8e7d6c","url":null}""", JsonOpts);
        Assert.Equal("ph_9f8e7d6c", photo!.PhotoId);
    }

    [Fact]
    public void BatchResult_recordNo为数字_可解析()
    {
        var result = JsonSerializer.Deserialize<BatchResult>(
            """{"clientRef":"20261002-01-0001","status":"created","recordNo":42,"reason":""}""",
            JsonOpts);
        Assert.Equal("42", result!.RecordNo);
    }
}

/// <summary>地主电话录入校验（平台归属匹配要求 1 开头 11 位；非法整条拒收）。</summary>
public class PhoneValidationTests
{
    [Theory]
    [InlineData("13800138000", true)]
    [InlineData("13900139000", true)]
    [InlineData("", true)]          // 留空允许（本地不强填，推送时平台按必填校验）
    [InlineData("1380013800", false)]     // 10 位
    [InlineData("138001380001", false)]   // 12 位
    [InlineData("23800138000", false)]    // 非 1 开头
    [InlineData("1380013800a", false)]    // 含字母
    public void ValidPhone_按11位手机号校验(string phone, bool expected)
        => Assert.Equal(expected, UI.WeighEntryDialog.ValidPhone(phone));
}
