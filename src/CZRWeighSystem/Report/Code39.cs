namespace CZRWeighSystem.Report;

/// <summary>
/// Code39 一维码编码（纯逻辑，无 GDI 依赖，便于单元测试）。
/// 编码规则：每个字符 9 个条/空元素（恰 3 宽 6 窄），条空交替、以条起止；
/// 字符间以 1 个窄空间隔；起止符 "*" 由 Encode 自动添加（不要求包含在输入中）。
/// 宽:窄 = 2:1。
/// </summary>
public static class Code39
{
    /// <summary>Code39 支持的字符集（43 字符；* 为起止符不在此列）</summary>
    public const string Charset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

    /// <summary>44 字符编码表（含起止符 *）：每字符 9 位，条空交替（偶数位=条，奇数位=空），n=窄 w=宽</summary>
    private static readonly Dictionary<char, string> Patterns = new()
    {
        ['0'] = "nnnwwnwnn", ['1'] = "wnnwnnnnw", ['2'] = "nnwwnnnnw", ['3'] = "wnwwnnnnn",
        ['4'] = "nnnwwnnnw", ['5'] = "wnnwwnnnn", ['6'] = "nnwwwnnnn", ['7'] = "nnnwnnwnw",
        ['8'] = "wnnwnnwnn", ['9'] = "nnwwnnwnn",
        ['A'] = "wnnnnwnnw", ['B'] = "nnwnnwnnw", ['C'] = "wnwnnwnnn", ['D'] = "nnnnwwnnw",
        ['E'] = "wnnnwwnnn", ['F'] = "nnwnwwnnn", ['G'] = "nnnnnwwnw", ['H'] = "wnnnnwwnn",
        ['I'] = "nnwnnwwnn", ['J'] = "nnnnwwwnn",
        ['K'] = "wnnnnnnww", ['L'] = "nnwnnnnww", ['M'] = "wnwnnnnwn", ['N'] = "nnnnwnnww",
        ['O'] = "wnnnwnnwn", ['P'] = "nnwnwnnwn", ['Q'] = "nnnnnnwww", ['R'] = "wnnnnnwwn",
        ['S'] = "nnwnnnwwn", ['T'] = "nnnnwnwwn",
        ['U'] = "wwnnnnnnw", ['V'] = "nwwnnnnnw", ['W'] = "wwwnnnnnn", ['X'] = "nwnnwnnnw",
        ['Y'] = "wwnnwnnnn", ['Z'] = "nwwnwnnnn",
        ['-'] = "nwnnnnwnw", ['.'] = "wwnnnnwnn", [' '] = "nwwnnnwnn",
        ['$'] = "nwnwnwnnn", ['/'] = "nwnwnnnwn", ['+'] = "nwnnnwnwn", ['%'] = "nnnwnwnwn",
        ['*'] = "nwnnwnwnn",
    };

    /// <summary>文本是否可编码（仅判断字符集；不修改内容）</summary>
    public static bool IsValid(string? text)
        => !string.IsNullOrEmpty(text) && text.All(c => Patterns.ContainsKey(char.ToUpperInvariant(c)));

    /// <summary>
    /// 编码为条/空交替序列（自动添加首尾 * 起止符；字符间补 1 窄空）。
    /// Bar=true 表示条（打印黑色），Units 为该段的窄条宽倍数。
    /// </summary>
    public static List<(bool Bar, int Units)> Encode(string text)
    {
        if (!IsValid(text)) throw new ArgumentException("包含 Code39 不支持的字符", nameof(text));
        string s = "*" + text.ToUpperInvariant() + "*";

        var result = new List<(bool, int)>(s.Length * 10);
        for (int ci = 0; ci < s.Length; ci++)
        {
            string p = Patterns[s[ci]];
            for (int i = 0; i < p.Length; i++)
                result.Add((i % 2 == 0, p[i] == 'w' ? 2 : 1));
            if (ci < s.Length - 1)
                result.Add((false, 1));     // 字符间窄空间隔
        }
        return result;
    }

    /// <summary>编码后总窄单元数（渲染层按元素宽反算窄条宽：unitPx = W_px / TotalUnits）。</summary>
    public static int TotalUnits(string text)
        => Encode(text).Sum(u => u.Units);
}
