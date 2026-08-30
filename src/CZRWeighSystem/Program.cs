namespace CZRWeighSystem;

/// <summary>
/// 程序入口。
/// </summary>
internal static class Program
{
    [STAThread]
    static void Main()
    {
        // WinForms 应用初始化（.NET 8 推荐方式：含 DPI/默认字体设置）
        ApplicationConfiguration.Initialize();

        // 加载配置文件（exe 目录下 config.json，不存在则生成默认配置）
        AppConfig.Load();
        Log.Info("程序启动");

        // 初始化数据库（首次运行自动建库建表并创建默认账号）
        Database.Db.Initialize();

        // 登录 → 主界面 → 注销时重新登录（对应 PRD 5.1）
        while (true)
        {
            using (var login = new UI.LoginForm())
            {
                if (login.ShowDialog() != DialogResult.OK)
                    return;                 // 取消登录：退出程序
            }

            Application.Run(new UI.MainForm());

            // 主界面注销时重新走登录流程，否则视为正常退出
            if (!Core.AppSession.ReLoginRequested)
                break;
            Core.AppSession.ReLoginRequested = false;
            Log.Info("用户注销，返回登录界面");
        }

        Log.Info("程序退出");
    }
}
