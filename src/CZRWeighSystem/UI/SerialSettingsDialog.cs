using System.IO.Ports;
using System.Text;

namespace CZRWeighSystem.UI;

/// <summary>
/// 串口参数设置窗体（对应 PRD F6-01 仪表数据采集）。
/// 枚举本机 COM 口，配置波特率/数据位/校验位/停止位并保存到 config.json；
/// 「测试读取」按钮打开串口收集 3 秒原始数据，以十六进制 + 文本显示，
/// 便于现场核对仪表型号协议与串口参数是否正确。
/// </summary>
public class SerialSettingsDialog : Form
{
    private ComboBox _cboPort = null!;      // COM 口
    private ComboBox _cboBaud = null!;      // 波特率
    private ComboBox _cboData = null!;      // 数据位
    private ComboBox _cboParity = null!;    // 校验位
    private ComboBox _cboStop = null!;      // 停止位
    private TextBox _txtTest = null!;       // 测试读取结果显示
    private Button _btnTest = null!;

    private SerialPort? _testPort;          // 测试读取专用串口
    private readonly List<byte> _testBuf = new();   // 测试期间的原始字节

    private static readonly int[] BaudRates = [1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];

    public SerialSettingsDialog()
    {
        Text = "串口参数设置";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 下避免文字截断
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 480);
        Font = new Font("Microsoft YaHei UI", 10F);

        BuildUi();
    }

    private void BuildUi()
    {
        var cfg = AppConfig.Current;

        int y = 30;
        Label AddRow(string title, ComboBox cbo, int itemWidth = 200)
        {
            var lbl = new Label { Text = title, AutoSize = true, Location = new Point(25, y + 4) };
            cbo.Location = new Point(140, y);
            cbo.Size = new Size(itemWidth, 28);
            cbo.DropDownStyle = ComboBoxStyle.DropDownList;
            Controls.Add(lbl);
            Controls.Add(cbo);
            y += 44;
            return lbl;
        }

        // ---- COM 口（可枚举刷新）----
        _cboPort = new ComboBox();
        AddRow("串口(COM)：", _cboPort, 160);
        var btnRefresh = new Button
        {
            Text = "刷新",
            Location = new Point(310, y - 44),
            Size = new Size(70, 28),
            FlatStyle = FlatStyle.Flat,
        };
        btnRefresh.Click += (_, _) => RefreshPorts();
        Controls.Add(btnRefresh);

        // ---- 波特率 ----
        _cboBaud = new ComboBox();
        AddRow("波特率：", _cboBaud);
        _cboBaud.Items.AddRange(BaudRates.Select(b => (object)b).ToArray());
        _cboBaud.SelectedItem = cfg.BaudRate;
        if (_cboBaud.SelectedIndex < 0) _cboBaud.SelectedItem = 9600;

        // ---- 数据位 ----
        _cboData = new ComboBox();
        AddRow("数据位：", _cboData);
        _cboData.Items.AddRange([7, 8]);
        _cboData.SelectedItem = cfg.DataBits;
        if (_cboData.SelectedIndex < 0) _cboData.SelectedItem = 8;

        // ---- 校验位 ----
        _cboParity = new ComboBox();
        AddRow("校验位：", _cboParity);
        _cboParity.Items.AddRange(["None（无）", "Odd（奇）", "Even（偶）"]);
        _cboParity.SelectedIndex = cfg.Parity switch
        {
            "Odd" => 1,
            "Even" => 2,
            _ => 0,
        };

        // ---- 停止位 ----
        _cboStop = new ComboBox();
        AddRow("停止位：", _cboStop);
        _cboStop.Items.AddRange(["One（1）", "Two（2）"]);
        _cboStop.SelectedIndex = cfg.StopBits == "Two" ? 1 : 0;

        RefreshPorts();
        _cboPort.SelectedItem = cfg.PortName;
        if (_cboPort.SelectedIndex < 0 && _cboPort.Items.Count > 0)
            _cboPort.SelectedIndex = 0;      // 配置的口不存在时回落到第一个

        // ---- 测试读取区 ----
        var lblTest = new Label
        {
            Text = "测试读取（3 秒原始数据，核对仪表协议）：",
            AutoSize = true,
            Location = new Point(25, y + 2),
            ForeColor = Color.Gray,
        };
        Controls.Add(lblTest);
        y += 30;

        _txtTest = new TextBox
        {
            Location = new Point(25, y),
            Size = new Size(510, 120),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
        };
        Controls.Add(_txtTest);
        y += 130;

        _btnTest = new Button
        {
            Text = "测试读取",
            Location = new Point(140, y),
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
        };
        var btnSave = new Button
        {
            Text = "保存设置",
            Location = new Point(265, y),
            Size = new Size(110, 34),
            BackColor = Color.FromArgb(0, 122, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        var btnCancel = new Button
        {
            Text = "取 消",
            Location = new Point(390, y),
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
        };
        _btnTest.Click += async (_, _) => await TestReadAsync();
        btnSave.Click += (_, _) => Save();
        btnCancel.Click += (_, _) => { StopTest(); DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { _btnTest, btnSave, btnCancel });

        FormClosed += (_, _) => StopTest();
    }

    /// <summary>枚举本机可用 COM 口填入下拉框。</summary>
    private void RefreshPorts()
    {
        string sel = _cboPort.Text;
        _cboPort.Items.Clear();
        try
        {
            _cboPort.Items.AddRange(SerialPort.GetPortNames().Order().ToArray());
        }
        catch (Exception ex)
        {
            Log.Error("枚举串口失败", ex);
        }
        if (sel.Length > 0 && _cboPort.Items.Contains(sel))
            _cboPort.SelectedItem = sel;
    }

    /// <summary>当前界面选择对应的 Parity 枚举。</summary>
    private Parity SelectedParity => _cboParity.SelectedIndex switch
    {
        1 => Parity.Odd,
        2 => Parity.Even,
        _ => Parity.None,
    };

    /// <summary>当前界面选择对应的 StopBits 枚举。</summary>
    private StopBits SelectedStopBits => _cboStop.SelectedIndex == 1 ? StopBits.Two : StopBits.One;

    /// <summary>
    /// 用当前界面参数打开串口收集 3 秒数据，结束后以
    /// 「十六进制 ｜ 可见字符」两列显示，帮助确认仪表协议帧格式。
    /// </summary>
    private async Task TestReadAsync()
    {
        if (_cboPort.SelectedItem is not string portName || portName.Length == 0)
        {
            MessageBox.Show("请先选择串口", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_testPort is { IsOpen: true })
        {
            StopTest();     // 测试中再点一次 = 提前停止
            return;
        }

        try
        {
            lock (_testBuf) _testBuf.Clear();
            _testPort = new SerialPort(portName, (int)_cboBaud.SelectedItem!,
                SelectedParity, (int)_cboData.SelectedItem!, SelectedStopBits)
            {
                ReadTimeout = 200,
            };
            _testPort.DataReceived += (_, _) =>
            {
                try
                {
                    var p = _testPort;
                    if (p is not { IsOpen: true }) return;
                    int n = p.BytesToRead;
                    if (n <= 0) return;
                    var buf = new byte[n];
                    p.Read(buf, 0, n);
                    lock (_testBuf) _testBuf.AddRange(buf);
                }
                catch { /* 测试口即将关闭时的读取竞争，忽略 */ }
            };
            _testPort.Open();

            _btnTest.Text = "停止读取";
            _txtTest.Text = "读取中……（可上磅压一下让仪表发出数据）";
            await Task.Delay(3000);
            StopTest();

            string hex, text;
            byte[] data;
            lock (_testBuf) data = [.. _testBuf];
            if (data.Length == 0)
            {
                hex = "3 秒内未收到任何数据。请检查：\r\n" +
                      "1. COM 口选择是否正确（设备管理器中确认）\r\n" +
                      "2. 波特率/校验位是否与仪表说明书一致\r\n" +
                      "3. 串口线是否为正确的交叉/直连方式";
            }
            else
            {
                hex = BitConverter.ToString(data).Replace("-", " ");
                var sb = new StringBuilder();
                foreach (var b in data)
                    sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                text = sb.ToString();
                hex = $"共收到 {data.Length} 字节：\r\n{hex}\r\n\r\n文本形式：\r\n{text}";
            }
            _txtTest.Text = hex;
            Log.Info($"串口测试读取：{portName} 收到 {data.Length} 字节");
        }
        catch (Exception ex)
        {
            StopTest();
            _txtTest.Text = "打开串口失败：" + ex.Message;
            MessageBox.Show("打开串口失败：" + ex.Message, "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>停止并释放测试串口。</summary>
    private void StopTest()
    {
        try
        {
            if (_testPort is { IsOpen: true }) _testPort.Close();
            _testPort?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("关闭测试串口异常", ex);
        }
        _testPort = null;
        _btnTest.Text = "测试读取";
    }

    /// <summary>保存成功后触发（综合设置页签内嵌时，主界面据此重连串口）。</summary>
    internal event Action? Saved;

    /// <summary>保存串口参数到 config.json。</summary>
    private void Save()
    {
        if (_cboPort.SelectedItem is not string portName || portName.Length == 0)
        {
            MessageBox.Show("未检测到可用串口，请确认设备已连接后点「刷新」", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var cfg = AppConfig.Current;
        cfg.PortName = portName;
        cfg.BaudRate = (int)_cboBaud.SelectedItem!;
        cfg.DataBits = (int)_cboData.SelectedItem!;
        cfg.Parity = SelectedParity.ToString();
        cfg.StopBits = SelectedStopBits.ToString();
        cfg.Save();
        Log.Info($"串口参数已保存：{cfg.PortName} {cfg.BaudRate} {cfg.DataBits}{cfg.Parity}{cfg.StopBits}");
        Saved?.Invoke();
        MessageBox.Show("串口参数已保存，已按新参数重新连接串口。\r\n若通讯指示灯仍为红色，请检查仪表电源、串口线及端口号是否正确。",
            "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }
}
