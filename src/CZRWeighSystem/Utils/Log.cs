namespace CZRWeighSystem;

/// <summary>
/// 简易文件日志（骨架阶段够用；正式版按 PRD F11-01 替换为 NLog）。
/// 日志文件按天写入 exe 目录下 logs/yyyyMMdd.txt。
/// </summary>
public static class Log
{
    private static readonly object _lock = new();

    private static string Dir => Path.Combine(AppContext.BaseDirectory, "logs");

    public static void Info(string message) => Write("INFO ", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Dir);
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                File.AppendAllText(
                    Path.Combine(Dir, $"{DateTime.Now:yyyyMMdd}.txt"),
                    line + Environment.NewLine);
            }
        }
        catch
        {
            // 日志失败不应影响主流程
        }
    }
}
