using System.Text.Json;
using CZRWeighSystem.Core;
using CZRWeighSystem.Database;
using CZRWeighSystem.Utils;

namespace CZRWeighSystem.Sync;

/// <summary>
/// 平台推送调度器（doc/API对接方案-称重软件.md）。
/// 后台每 30 秒扫描本地推送队列，按"照片 → 称重记录 → 黑匣子 → 作废"顺序推送，
/// 保证作废请求晚于记录到达平台：
///   照片先传拿 photoId，记录引用后挂载；记录推 50 条/批、黑匣子推 200 条/批且 ≥60 秒一批；
///   网络异常/5xx/超时按 1m/5m/15m/1h/6h 退避重试（最多 5 次，§6），断网续传、启动补推；
///   鉴权失败与单条业务校验失败直接置 failed（设置界面可见，可人工"重推失败项"）。
/// </summary>
public sealed class PushScheduler : IDisposable
{
    /// <summary>当前实例（MainForm 启动时赋值；作废同步等跨窗体入队使用）。</summary>
    public static PushScheduler? Current { get; private set; }

    private readonly PushQueueRepository _queue = new();
    private readonly WeighRecordRepository _recordRepo = new();
    private System.Threading.Timer? _timer;
    private int _busy;                                  // 轮次重入保护
    private DateTime _lastBlackboxPush = DateTime.MinValue;

    /// <summary>启动后台轮询（未启用平台对接时轮次直接空转）。</summary>
    public void Start()
    {
        Current = this;
        _timer = new System.Threading.Timer(_ => _ = RunOnceSafeAsync(), null, 3000, 30_000);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        if (Current == this) Current = null;
    }

    // ==================== 业务挂钩入队 ====================

    /// <summary>
    /// 二次磅结算完成后调用：抓拍照片 + 完整称重记录入队（一次磅不推——平台按完整业务成单）。
    /// </summary>
    public void EnqueueRecord(string serialNo)
    {
        try
        {
            if (!IsEnabled()) return;
            var record = _recordRepo.GetBySerialNo(serialNo);
            if (record == null || record.Status != "已完成") return;

            var files = Video.CameraManager.FindCaptures(serialNo);
            var refs = new List<string>();
            foreach (var file in files)
            {
                string photoRef = SyncPayloads.PhotoRefOf(file);
                refs.Add(photoRef);
                _queue.Enqueue(PushQueueRepository.KindPhoto, photoRef,
                    ToJson(new PhotoPayload(file, record.SecondTime)));
            }
            _queue.Enqueue(PushQueueRepository.KindRecord, serialNo,
                ToJson(SyncPayloads.FromRecord(record, refs)));
            Log.Info($"平台推送已入队：{serialNo}（照片 {refs.Count} 张）");
        }
        catch (Exception ex)
        {
            Log.Error("平台推送入队失败：" + serialNo, ex);
        }
    }

    /// <summary>黑匣子记录保存后调用：抓拍照片（取第一张）+ 黑匣子记录入队。</summary>
    public void EnqueueBlackbox(BlackBoxRecord record)
    {
        try
        {
            var cfg = AppConfig.Current;
            if (!cfg.PlatformSyncEnabled || !cfg.PlatformSyncBlackbox) return;

            string? photoRef = null;
            var images = record.ParseImages();
            if (images.Count > 0)
            {
                photoRef = SyncPayloads.PhotoRefOf(images[0]);
                _queue.Enqueue(PushQueueRepository.KindPhoto, photoRef,
                    ToJson(new PhotoPayload(images[0], record.Time)));
            }
            _queue.Enqueue(PushQueueRepository.KindBlackbox, $"BX{record.Id}",
                ToJson(SyncPayloads.FromBlackbox(record, photoRef)));
        }
        catch (Exception ex)
        {
            Log.Error("黑匣子推送入队失败：" + record.Id, ex);
        }
    }

    /// <summary>本地作废成功后调用：平台侧同步作废（未结算过的记录从未推送，无需同步）。</summary>
    public void EnqueueVoid(string serialNo, string reason)
    {
        try
        {
            if (!IsEnabled()) return;
            var record = _recordRepo.GetBySerialNo(serialNo);
            if (record == null || !record.SecondTime.HasValue) return;
            _queue.Enqueue(PushQueueRepository.KindVoid, serialNo,
                ToJson(new VoidPayload(serialNo, reason)));
        }
        catch (Exception ex)
        {
            Log.Error("作废同步入队失败：" + serialNo, ex);
        }
    }

    // ==================== 后台轮次 ====================

    private async Task RunOnceSafeAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;   // 上一轮未结束不重入
        try
        {
            await ProcessDueAsync();
        }
        catch (Exception ex)
        {
            Log.Error("平台推送轮次异常", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task ProcessDueAsync()
    {
        var cfg = AppConfig.Current;
        if (!IsEnabled()) return;

        using var client = new OpenApiClient(cfg.PlatformApiUrl, cfg.PlatformAppKey, cfg.PlatformAppSecret);

        // 1) 照片先传（每轮 ≤20 张，远低于照片上传 120 次/分钟限流）
        foreach (var item in _queue.GetDue(PushQueueRepository.KindPhoto, 20))
            await ProcessItemAsync(client, item, ProcessPhotoAsync);

        // 2) 称重记录（≤50 条/批；照片未全部完成的顺延且不计失败次数）
        var records = _queue.GetDue(PushQueueRepository.KindRecord, 50);
        if (records.Count > 0) await ProcessRecordsAsync(client, records);

        // 3) 黑匣子（≥60 秒一批，≤200 条/批，§6 上报节奏约定）
        if ((DateTime.Now - _lastBlackboxPush).TotalSeconds >= 60)
        {
            var blackbox = _queue.GetDue(PushQueueRepository.KindBlackbox, 200);
            if (blackbox.Count > 0)
            {
                await ProcessBlackboxAsync(client, blackbox);
                _lastBlackboxPush = DateTime.Now;
            }
        }

        // 4) 作废同步（记录每轮先处理，保证作废不早于记录到达）
        foreach (var item in _queue.GetDue(PushQueueRepository.KindVoid, 20))
            await ProcessItemAsync(client, item, ProcessVoidAsync);
    }

    // ---------- 单项处理（照片 / 作废） ----------

    private delegate Task<string> ItemWorker(OpenApiClient client, PushQueueItem item);

    /// <summary>单项处理 + 异常分类落库：鉴权/业务失败置 failed，限流短延，其余退避重试。</summary>
    private async Task ProcessItemAsync(OpenApiClient client, PushQueueItem item, ItemWorker worker)
    {
        try
        {
            _queue.MarkDone(item.Id, await worker(client, item));
        }
        catch (PlatformAuthException ex)
        {
            _queue.MarkFailed(item.Id, $"{ex.Message}（请检查 AppKey/AppSecret 与本机系统时间）");
        }
        catch (PlatformBusinessException ex)
        {
            _queue.MarkFailed(item.Id, ex.Message);
        }
        catch (PlatformRateLimitException)
        {
            _queue.Defer(item.Id, TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            _queue.ScheduleRetry(item.Id, ex.Message);   // 网络/超时/5xx → 退避重试
        }
    }

    private async Task<string> ProcessPhotoAsync(OpenApiClient client, PushQueueItem item)
    {
        var meta = FromJson<PhotoPayload>(item.Payload);
        var photo = await client.UploadPhotoAsync(meta.File, item.ClientRef, meta.ShotAt);
        return photo.PhotoId;
    }

    private async Task<string> ProcessVoidAsync(OpenApiClient client, PushQueueItem item)
    {
        var meta = FromJson<VoidPayload>(item.Payload);
        var result = await client.VoidAsync(item.ClientRef, meta.Reason);
        if (result.Status == "not_found")
            throw new PlatformBusinessException("平台暂无该记录（可能尚未推送成功），重推失败项可重试");
        return result.Status;   // voided / already_voided
    }

    // ---------- 批量处理（称重记录 / 黑匣子） ----------

    private async Task ProcessRecordsAsync(OpenApiClient client, List<PushQueueItem> items)
    {
        // 照片未全部完成的记录本轮顺延（不计失败次数，照片传完后的下一轮推送）
        var ready = new List<(PushQueueItem Item, RecordPayload Payload)>();
        foreach (var item in items)
        {
            var payload = FromJson<RecordPayload>(item.Payload);
            if (payload.Photos.All(p => _queue.GetDoneResult(PushQueueRepository.KindPhoto, p) != null))
                ready.Add((item, payload));
            else
                _queue.Defer(item.Id, TimeSpan.FromMinutes(1));
        }
        if (ready.Count == 0) return;

        try
        {
            var batch = await client.PushRecordsAsync(ready.Select(r => r.Payload).ToList());
            ApplyBatchResults(ready.Select(r => r.Item).ToList(), batch.Results);
        }
        catch (Exception ex)
        {
            ApplyBatchException(ready.Select(r => r.Item).ToList(), ex);
        }
    }

    private async Task ProcessBlackboxAsync(OpenApiClient client, List<PushQueueItem> items)
    {
        // 关联照片已完成的带上 photoId；未完成的顺延；无照片的按"photoId 可选"约定直接推
        var ready = new List<(PushQueueItem Item, BlackboxPayload Payload)>();
        foreach (var item in items)
        {
            var payload = FromJson<BlackboxPayload>(item.Payload);
            if (payload.PhotoId == null)
            {
                ready.Add((item, payload));
                continue;
            }
            var photoId = _queue.GetDoneResult(PushQueueRepository.KindPhoto, payload.PhotoId);
            if (photoId == null)
                _queue.Defer(item.Id, TimeSpan.FromMinutes(1));
            else
                ready.Add((item, payload with { PhotoId = photoId }));
        }
        if (ready.Count == 0) return;

        try
        {
            var batch = await client.PushBlackboxAsync(ready.Select(r => r.Payload).ToList());
            ApplyBatchResults(ready.Select(r => r.Item).ToList(), batch.Results);
        }
        catch (Exception ex)
        {
            ApplyBatchException(ready.Select(r => r.Item).ToList(), ex);
        }
    }

    /// <summary>按 clientRef 回填逐条结果：created/duplicate 完成，failed 置失败并留原因。</summary>
    private void ApplyBatchResults(List<PushQueueItem> items, List<BatchResult>? results)
    {
        foreach (var r in results ?? [])
        {
            var item = items.FirstOrDefault(i => i.ClientRef == r.ClientRef);
            if (item == null) continue;
            if (r.Status is "created" or "duplicate")
                _queue.MarkDone(item.Id, r.RecordNo ?? r.Status);
            else
                _queue.MarkFailed(item.Id,
                    string.IsNullOrWhiteSpace(r.Reason) ? $"推送失败（{r.Status}）" : r.Reason);
        }
        // 服务端未逐条返回的条目（理论上不发生）按可重试处理，避免悬挂
        var returned = (results ?? []).Select(x => x.ClientRef).ToHashSet();
        foreach (var item in items.Where(i => !returned.Contains(i.ClientRef)))
            _queue.ScheduleRetry(item.Id, "服务端未返回该条结果");
    }

    /// <summary>整批请求异常：鉴权/结构错误置 failed（修正后人工重推），限流短延，网络异常退避重试。</summary>
    private void ApplyBatchException(List<PushQueueItem> items, Exception ex)
    {
        foreach (var item in items)
        {
            switch (ex)
            {
                case PlatformAuthException:
                    _queue.MarkFailed(item.Id, $"{ex.Message}（请检查 AppKey/AppSecret 与本机系统时间）");
                    break;
                case PlatformBusinessException:
                    _queue.MarkFailed(item.Id, ex.Message);
                    break;
                case PlatformRateLimitException:
                    _queue.Defer(item.Id, TimeSpan.FromSeconds(15));
                    break;
                default:
                    _queue.ScheduleRetry(item.Id, ex.Message);
                    break;
            }
        }
    }

    // ---------- 工具 ----------

    private static bool IsEnabled() => AppConfig.Current.PlatformSyncEnabled;

    private static string ToJson<T>(T payload) =>
        JsonSerializer.Serialize(payload, SyncPayloadJson.Opts);

    private static T FromJson<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, SyncPayloadJson.Opts)
        ?? throw new PlatformBusinessException("队列载荷解析失败");

    /// <summary>队列载荷序列化选项（省略 null 字段，与平台契约一致）。</summary>
    internal static class SyncPayloadJson
    {
        public static readonly JsonSerializerOptions Opts = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
    }
}
