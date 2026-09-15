using System.Text.Json;

namespace CodexPetFocus.Core;

/// <summary>Atomic local checkpoints. Unknown versions and unrecoverable corruption never become empty data.</summary>
public sealed class JsonStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string? RecoveryNotice { get; private set; }
    private string MainPath => Path.Combine(DirectoryPath, "focus.json");
    private string BackupPath => MainPath + ".bak";

    public FocusData Load()
    {
        RecoveryNotice = null;
        if (!File.Exists(MainPath) && !File.Exists(BackupPath)) return new();
        try { return Read(MainPath); }
        catch (InvalidDataException ex) when (ex.Data.Contains("UnsupportedVersion")) { throw; }
        catch (Exception original) when (original is IOException or JsonException or InvalidDataException)
        {
            FocusData recovered;
            try { recovered = Read(BackupPath); }
            catch (Exception backupError) when (backupError is IOException or JsonException or InvalidDataException)
            {
                throw new InvalidDataException("主文件与备份均无法读取；已保留 focus.json 和 focus.json.bak。", original);
            }
            if (File.Exists(MainPath))
                File.Copy(MainPath, MainPath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Write(recovered, keepBackup: false);
            RecoveryNotice = "主文件损坏；已从备份恢复，并保留损坏文件。";
            return recovered;
        }
    }

    public void Save(FocusData data)
    {
        Validate(data);
        Write(data, keepBackup: true);
    }

    private void Write(FocusData data, bool keepBackup)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = Path.Combine(DirectoryPath, ".focus-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data, Options);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(MainPath))
                File.Replace(temporary, MainPath, keepBackup ? BackupPath : null);
            else File.Move(temporary, MainPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static FocusData Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version))
            throw new InvalidDataException("存储文件缺少有效的 schemaVersion。");
        if (version != 1) throw UnsupportedVersion(version);
        // Required persisted collections must not silently default to empty after corruption.
        foreach (var key in new[] { "tasks", "slices", "settings" })
            if (!document.RootElement.TryGetProperty(key, out _)) throw new InvalidDataException("存储文件缺少必需字段：" + key);
        var data = document.RootElement.Deserialize<FocusData>(Options) ?? throw new InvalidDataException("存储文件内容为空。");
        Validate(data);
        return data;
    }

    private static void Validate(FocusData data)
    {
        if (data.SchemaVersion != 1) throw UnsupportedVersion(data.SchemaVersion);
        if (data.Tasks is null || data.Slices is null || data.Settings is null) throw new InvalidDataException("存储数据集合不能为空。");
        var ids = new HashSet<Guid>();
        foreach (var task in data.Tasks)
            if (task is null || task.Id == Guid.Empty || !ids.Add(task.Id) || string.IsNullOrWhiteSpace(task.Title) || task.Title.Length > 500)
                throw new InvalidDataException("任务数据无效。");
        long total = 0;
        foreach (var slice in data.Slices)
        {
            if (slice is null || !ids.Contains(slice.TaskId) || slice.DurationTicks < 0)
                throw new InvalidDataException("计时数据无效。");
            try { total = checked(total + slice.DurationTicks); }
            catch (OverflowException ex) { throw new InvalidDataException("计时总量超出支持范围。", ex); }
        }
    }

    private static InvalidDataException UnsupportedVersion(int version)
    {
        var error = new InvalidDataException($"不支持的存储版本 {version}；拒绝降级覆盖。");
        error.Data["UnsupportedVersion"] = true;
        return error;
    }
}
