namespace CZRWeighSystem.UI;

/// <summary>
/// 称重参数设置窗体（限管理员）。
/// 首次称重有效天数：一次磅保存后超过该天数仍未配对二次磅，系统自动删除该记录；
/// 删除后同车号再次称重按首次称重处理（不再提示配对）。0 表示不启用自动删除。
/// </summary>
public class WeighSettingsDialog : Form
{
    private NumericUpDown _numDays = null!;   // 首次称重有效天数（0 = 不启用）

    public WeighSettingsDialog()
    {
        Text = "称重设置";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(480, 220);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
    }

    private void BuildUi()
    {
        // ---- 首次称重有效天数 ----
        var lblDays = new Label
        { Text = "首次称重有效天数：", AutoSize = true, Location = new Point(25, 33) };
        _numDays = new NumericUpDown
        {
            Location = new Point(190, 28),
            Size = new Size(90, 30),
            Minimum = 0,
            Maximum = 365,
            Value = AppConfig.Current.FirstWeighExpireDays,
        };
        Controls.Add(lblDays);
        Controls.Add(_numDays);

        // ---- 说明文字 ----
        var lblTip = new Label
        {
            Text = "一次磅保存后超过该天数仍未配对二次磅，系统自动删除该记录；\r\n" +
                   "此后同车号再次称重按首次称重处理（不再提示配对）。\r\n" +
                   "填 0 表示不启用自动删除。",
            AutoSize = true,
            Location = new Point(25, 70),
            ForeColor = Color.Gray,
        };
        Controls.Add(lblTip);

        // ---- 保存 / 取消 ----
        var btnSave = new Button
        {
            Text = "保存设置",
            Location = new Point(140, 150),
            Size = new Size(110, 34),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnCancel = new Button
        {
            Text = "取 消",
            Location = new Point(265, 150),
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
        };
        btnSave.Click += (_, _) => Save();
        btnCancel.Click += (_, _) => Close();
        Controls.AddRange([btnSave, btnCancel]);
    }

    /// <summary>保存首次称重有效天数到 config.json（立即生效）。</summary>
    private void Save()
    {
        var cfg = AppConfig.Current;
        cfg.FirstWeighExpireDays = (int)_numDays.Value;
        cfg.Save();
        Log.Info($"称重设置已保存：首次称重有效天数 = {cfg.FirstWeighExpireDays} 天");
        DialogResult = DialogResult.OK;
        Close();
    }
}
