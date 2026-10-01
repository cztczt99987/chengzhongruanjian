using System.Text.Json;

namespace CZRWeighSystem.Core;

/// <summary>
/// 黑匣子记录模型（t_black_box）。
/// 重量持续稳定达到设定秒数后自动记录：当时的稳定重量 + 全部摄像头抓拍图片。
/// </summary>
public class BlackBoxRecord
{
    public long Id { get; set; }
    /// <summary>记录时间（稳定持续达到设定秒数的时刻）</summary>
    public DateTime Time { get; set; }
    /// <summary>稳定重量（千克）</summary>
    public double WeightKg { get; set; }
    /// <summary>抓拍图片路径（JSON 数组，存储于 images 列）</summary>
    public string? ImagesJson { get; set; }

    /// <summary>解析抓拍图片路径列表（JSON 无效时返回空列表）。</summary>
    public List<string> ParseImages()
    {
        if (string.IsNullOrWhiteSpace(ImagesJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(ImagesJson) ?? [];
        }
        catch { return []; }
    }

    /// <summary>图片路径列表序列化（空列表返回 null，不占存储）。</summary>
    public static string? ImagesToJson(List<string>? images)
        => images is { Count: > 0 } ? JsonSerializer.Serialize(images) : null;
}
