namespace CZRWeighSystem.Utils;

/// <summary>
/// 表格导出工具（对应 PRD F3-07 / F4-05 导出 Excel）。
/// 骨架阶段导出 CSV（UTF-8 带 BOM，Excel 双击可直接打开不乱码），
/// 正式版可替换为 ClosedXML 生成原生 .xlsx。
/// </summary>
public static class CsvExporter
{
    /// <summary>
    /// 将 DataGridView 内容导出为 CSV 文件。
    /// </summary>
    /// <returns>是否导出成功（用户取消返回 false）</returns>
    public static bool Export(DataGridView grid, string defaultFileName)
    {
        if (grid.RowCount == 0)
        {
            MessageBox.Show("没有数据可导出，请先查询", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "导出文件",
            FileName = defaultFileName,
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return false;

        try
        {
            // UTF-8 带 BOM：保证 Excel 直接打开中文不乱码
            using var writer = new StreamWriter(dlg.FileName, false, new System.Text.UTF8Encoding(true));

            // 表头
            var headers = grid.Columns.Cast<DataGridViewColumn>()
                .Select(c => Escape(c.HeaderText));
            writer.WriteLine(string.Join(",", headers));

            // 数据行
            foreach (DataGridViewRow row in grid.Rows)
            {
                var cells = row.Cells.Cast<DataGridViewCell>()
                    .Select(c => Escape(c.Value?.ToString() ?? ""));
                writer.WriteLine(string.Join(",", cells));
            }

            Log.Info($"导出 CSV 成功：{dlg.FileName}（{grid.RowCount} 行）");
            MessageBox.Show($"导出成功：{dlg.FileName}", "成功",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("导出 CSV 失败", ex);
            MessageBox.Show("导出失败：" + ex.Message, "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>
    /// CSV 字段转义：包含逗号/引号/换行时用双引号包裹并转义内部引号。
    /// </summary>
    private static string Escape(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.Contains(',') || field.Contains('"') ||
            field.Contains('\n') || field.Contains('\r'))
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
        return field;
    }
}
