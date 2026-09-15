using System.IO;
using System.Text;
using System.Text.Json;
using CodexPetFocus.Native;

namespace CodexPetFocus.App;

internal static class DiagnosticWriter
{
    public static void Write(string outputPath)
    {
        var lookup = Task.Run(() =>
        {
            var locator = new CodexPetLocator();
            var target = locator.FindPet();
            return new LookupResult(target, locator.LastStatus.ToString(), locator.LastReason);
        });

        LookupResult result;
        if (!lookup.Wait(TimeSpan.FromSeconds(2)))
        {
            result = new LookupResult(null, "TimedOut", "locator-timeout-after-2-seconds");
        }
        else
        {
            result = lookup.GetAwaiter().GetResult();
        }

        object? targetReport = result.Target is null
            ? null
            : new
            {
                spriteBounds = result.Target.SpriteBounds,
                windowBounds = result.Target.WindowBounds,
                version = result.Target.Version
            };
        var report = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            applicationVersion = typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
            petLocator = new
            {
                status = result.Status,
                reason = result.Reason,
                target = targetReport
            },
            automaticActions = new
            {
                compatible = false,
                reason = "原生 jumping 兼容性验证未通过；本版本不会移动、跳跃或归位原桌宠。"
            },
            privacy = new
            {
                offline = true,
                readsAuthenticationFiles = false,
                readsChats = false
            }
        };

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("诊断输出路径无效。", nameof(outputPath));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private sealed record LookupResult(PetTarget? Target, string Status, string Reason);
}
