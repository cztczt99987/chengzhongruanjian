using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.UI;

namespace CZRWeighSystem.Tests;

/// <summary>
/// 记录查询窗体回归测试（2026-09-07 用户要求：称重记录不可修改）。
/// 管理员登录下也不得出现"修改记录"按钮；错误记录只能"作废留痕 + 重新过磅"修正。
/// 与 FirstWeighExpireTests 同集合串行执行（构建时会查询自定义字段表）。
/// </summary>
[Collection("数据库顺序")]
public class RecordQueryFormTests
{
    public RecordQueryFormTests() => Db.Initialize();   // 幂等建表（构建时查询自定义字段）

    [Fact]
    public void 管理员登录下_记录查询不含修改记录按钮()
    {
        List<string> texts = null!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                // 以管理员身份构建窗体（修改/作废按钮仅管理员可见）
                AppSession.CurrentUser = new User
                {
                    Id = 1, Username = "admin_test", DisplayName = "测试管理员", Role = User.RoleAdmin,
                };
                using var form = new RecordQueryForm();
                texts = ButtonTexts(form);
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                AppSession.CurrentUser = null;   // 还原静态会话，避免污染其他测试
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;

        Assert.DoesNotContain("修改记录", texts);   // 回归：记录不可修改，修改按钮必须移除
        Assert.Contains("作废记录", texts);           // 错误记录的唯一修正手段：作废（留原因）
        Assert.Contains("查看留痕", texts);           // 作废留痕仍可追溯
    }

    /// <summary>递归收集窗体上全部按钮文字。</summary>
    private static List<string> ButtonTexts(Control root)
    {
        var list = new List<string>();
        foreach (Control c in root.Controls)
        {
            if (c is Button b) list.Add(b.Text);
            list.AddRange(ButtonTexts(c));
        }
        return list;
    }
}
