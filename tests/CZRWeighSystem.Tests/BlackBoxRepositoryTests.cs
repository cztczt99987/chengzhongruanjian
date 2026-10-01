using CZRWeighSystem.Core;
using CZRWeighSystem.Database;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 黑匣子记录仓储测试。
/// 使用测试输出目录下的 SQLite 库（Db.Initialize 幂等建表，含 t_black_box）。
/// 与 MainFormWeighDisplayTests 同集合串行执行，避免共享库竞争。
/// </summary>
[Collection("数据库顺序")]
public class BlackBoxRepositoryTests
{
    private readonly BlackBoxRepository _repo = new();

    static BlackBoxRepositoryTests()
    {
        Db.Initialize();   // 幂等建表（含 t_black_box）
        using var conn = Db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM t_black_box;";
        cmd.ExecuteNonQuery();
    }

    public BlackBoxRepositoryTests() => Db.Initialize();

    [Fact]
    public void Insert_And_Query_RoundTrip()
    {
        var images = new List<string> { @"C:\x\blackbox\a.jpg", @"C:\x\blackbox\b.jpg" };
        var inserted = _repo.Insert(new BlackBoxRecord
        {
            Time = new DateTime(2026, 10, 1, 9, 30, 0),
            WeightKg = 35200.5,
            ImagesJson = BlackBoxRecord.ImagesToJson(images),
        });

        Assert.True(inserted.Id > 0);

        var list = _repo.Query(
            new DateTime(2026, 10, 1), new DateTime(2026, 10, 1), null, null);
        var found = list.Single(r => r.Id == inserted.Id);

        Assert.Equal(35200.5, found.WeightKg);
        Assert.Equal(new DateTime(2026, 10, 1, 9, 30, 0), found.Time);
        Assert.Equal(images, found.ParseImages());
    }

    [Fact]
    public void Query_FiltersByWeightRange()
    {
        var low = _repo.Insert(new BlackBoxRecord
        { Time = DateTime.Now, WeightKg = 100, ImagesJson = null });
        var mid = _repo.Insert(new BlackBoxRecord
        { Time = DateTime.Now, WeightKg = 5000, ImagesJson = null });
        var high = _repo.Insert(new BlackBoxRecord
        { Time = DateTime.Now, WeightKg = 90000, ImagesJson = null });

        var inRange = _repo.Query(null, null, minKg: 1000, maxKg: 50000);

        Assert.Contains(inRange, r => r.Id == mid.Id);
        Assert.DoesNotContain(inRange, r => r.Id == low.Id);
        Assert.DoesNotContain(inRange, r => r.Id == high.Id);
    }

    [Fact]
    public void ParseImages_InvalidJson_ReturnsEmpty()
    {
        var r = new BlackBoxRecord { ImagesJson = "not-json" };
        Assert.Empty(r.ParseImages());
        Assert.Empty(new BlackBoxRecord().ParseImages());
        Assert.Null(BlackBoxRecord.ImagesToJson([]));
    }
}
