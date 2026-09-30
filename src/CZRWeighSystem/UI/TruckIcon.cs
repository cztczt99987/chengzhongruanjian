using System.Drawing.Drawing2D;

namespace CZRWeighSystem.UI;

/// <summary>
/// 空/重车自卸车图标（用户提供的图片，作为内嵌资源随程序分发，无外部文件依赖）：
/// 空车 = 车厢举升卸货，重车 = 满载碎石；原图已处理为透明背景。
/// 两张资源等宽且共用画布（底部对齐），故按同一尺寸缩放后两车比例一致、按钮内文字对齐。
/// </summary>
internal static class TruckIcon
{
    /// <summary>按目标高度加载并等比缩放图标（宽度随资源画布比例，约 1.79:1）。</summary>
    public static Bitmap Draw(bool loaded, int height = 56)
    {
        using var src = Load(loaded ? "truck_loaded.png" : "truck_empty.png");
        int width = Math.Max(1, (int)Math.Round(height * (double)src.Width / src.Height));
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, new Rectangle(0, 0, width, height));
        return bmp;
    }

    /// <summary>读取内嵌资源原图（csproj 中 UI\Resources\*.png 以 EmbeddedResource 打包）。</summary>
    private static Bitmap Load(string fileName)
    {
        using var stream = typeof(TruckIcon).Assembly
            .GetManifestResourceStream($"{typeof(TruckIcon).Namespace}.Resources.{fileName}")
            ?? throw new InvalidOperationException($"缺少内嵌图标资源：{fileName}");
        return new Bitmap(stream);
    }
}