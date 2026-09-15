using System.IO;

namespace CodexPetFocus.App;

internal sealed record CommandLineOptions(string DataDirectory, bool HasCustomDataDirectory, bool Diagnose, string? OutputPath, bool SmokeTest)
{
    public static CommandLineOptions Parse(string[] args)
    {
        string? dataDirectory = null;
        string? outputPath = null;
        var diagnose = false;
        var smokeTest = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--data-dir":
                    dataDirectory = RequireValue(args, ref index, "--data-dir");
                    break;
                case "--diagnose":
                    diagnose = true;
                    break;
                case "--output":
                    outputPath = RequireValue(args, ref index, "--output");
                    break;
                case "--smoke-test":
                    smokeTest = true;
                    break;
                default:
                    throw new ArgumentException($"无法识别的参数：{args[index]}");
            }
        }

        if (diagnose && string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("--diagnose 必须同时提供 --output PATH。");
        if (!diagnose && outputPath is not null)
            throw new ArgumentException("--output 只能与 --diagnose 一起使用。");

        var customDataDirectory = !string.IsNullOrWhiteSpace(dataDirectory);
        if (smokeTest && !customDataDirectory)
            dataDirectory = Path.Combine(Path.GetTempPath(), "CodexPetFocus", "smoke", Environment.ProcessId.ToString());
        dataDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexPetFocus");

        return new CommandLineOptions(
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(dataDirectory)),
            customDataDirectory,
            diagnose,
            outputPath is null ? null : Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath)),
            smokeTest);
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"{option} 后需要一个路径。");
        return args[++index];
    }
}
