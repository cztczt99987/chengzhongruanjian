namespace CZRWeighSystem.UI;

/// <summary>
/// 作废原因输入窗体（对应 PRD F3-04：作废需填写原因并留痕）。
/// </summary>
public class VoidReasonDialog : Form
{
    private TextBox _txtReason = null!;

    /// <summary>用户填写的作废原因</summary>
    public string Reason => _txtReason.Text.Trim();

    public VoidReasonDialog(string serialNo)
    {
        Text = "作废记录";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 190);
        Font = new Font("Microsoft YaHei UI", 10F);

        var lbl = new Label
        {
            Text = $"作废磅单 {serialNo}，请填写作废原因（必填）：",
            AutoSize = true,
            Location = new Point(25, 25),
        };
        _txtReason = new TextBox
        {
            Location = new Point(25, 60),
            Size = new Size(370, 60),
            Multiline = true,
        };
        var btnOk = new Button
        {
            Text = "确认作废",
            Location = new Point(140, 130),
            Size = new Size(110, 34),
            BackColor = Color.FromArgb(200, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnCancel = new Button
        {
            Text = "取 消",
            Location = new Point(260, 130),
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
        };
        btnOk.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_txtReason.Text))
            {
                MessageBox.Show("作废原因不能为空", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { lbl, _txtReason, btnOk, btnCancel });
        ActiveControl = _txtReason;
    }
}
