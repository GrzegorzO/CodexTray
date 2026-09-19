using System.Diagnostics;
using System.Text.Json;

namespace CodexTray;

internal sealed class CodexRateLimitClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    public static async Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var codexPath = CodexLocator.Find();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        var startInfo = CreateStartInfo(codexPath);
        using var process = new Process { StartInfo = startInfo };
        var started = false;
        Task<string>? stderrTask = null;

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Codex app-server could not be started.");
            }
            started = true;
            stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

            await SendAsync(process, new
            {
                id = 1,
                method = "initialize",
                @params = new
                {
                    clientInfo = new { name = "codex-tray", version = "0.1.0" },
                    capabilities = new { experimentalApi = true }
                }
            }, timeout.Token);

            await ReadResponseAsync(process, stderrTask, 1, timeout.Token);
            await SendAsync(process, new { method = "initialized", @params = new { } }, timeout.Token);
            await SendAsync(process, new { id = 2, method = "account/rateLimits/read" }, timeout.Token);

            using var response = await ReadResponseAsync(process, stderrTask, 2, timeout.Token);
            var root = response.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException($"Codex returned an error: {error}");
            }

            if (!root.TryGetProperty("result", out var result))
            {
                throw new InvalidOperationException("Codex returned no rate-limit result.");
            }

            return Parse(result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out while reading Codex usage.");
        }
        finally
        {
            if (started)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    await process.WaitForExitAsync(CancellationToken.None);
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The process exited between the state check and cleanup.
                }
            }

            if (stderrTask is not null)
            {
                try
                {
                    _ = await stderrTask;
                }
                catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
                {
                    // The request timeout already supplies the useful error.
                }
            }
        }
    }

    internal static UsageSnapshot Parse(JsonElement result)
    {
        var limits = SelectCodexLimits(result);
        var primary = ReadWindow(limits, "primary");
        var secondary = ReadWindow(limits, "secondary");
        UsageWindow? fiveHour = null;
        UsageWindow? weekly = null;

        foreach (var window in new[] { primary, secondary }.Where(window => window is not null))
        {
            if (window!.WindowDurationMins == 300)
            {
                fiveHour = window;
            }
            else if (window.WindowDurationMins == 10_080)
            {
                weekly = window;
            }
        }

        // Preserve useful data if a future response omits window durations.
        if (fiveHour is null && primary?.WindowDurationMins is null)
        {
            fiveHour = primary;
        }
        if (weekly is null && secondary?.WindowDurationMins is null)
        {
            weekly = secondary;
        }

        string? planType = limits.TryGetProperty("planType", out var plan) && plan.ValueKind == JsonValueKind.String
            ? plan.GetString()
            : null;

        return new UsageSnapshot(fiveHour, weekly, planType);
    }

    private static ProcessStartInfo CreateStartInfo(string codexPath)
    {
        if (codexPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var info = BaseStartInfo("cmd.exe");
            info.ArgumentList.Add("/d");
            info.ArgumentList.Add("/s");
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add($"\"{codexPath}\" app-server --stdio");
            return info;
        }

        var direct = BaseStartInfo(codexPath);
        direct.ArgumentList.Add("app-server");
        direct.ArgumentList.Add("--stdio");
        return direct;
    }

    private static ProcessStartInfo BaseStartInfo(string fileName) => new()
    {
        FileName = fileName,
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    private static async Task SendAsync(Process process, object message, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static async Task<JsonDocument> ReadResponseAsync(
        Process process,
        Task<string> stderrTask,
        int expectedId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                var stderr = await stderrTask;
                var detail = string.IsNullOrWhiteSpace(stderr) ? string.Empty : $" {stderr.Trim()}";
                throw new InvalidOperationException($"Codex app-server closed before returning usage data.{detail}");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (document.RootElement.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.Number &&
                id.TryGetInt32(out var value) &&
                value == expectedId)
            {
                return document;
            }

            document.Dispose();
        }
    }

    private static JsonElement SelectCodexLimits(JsonElement result)
    {
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) &&
            buckets.ValueKind == JsonValueKind.Object &&
            buckets.TryGetProperty("codex", out var codex))
        {
            return codex;
        }

        if (result.TryGetProperty("rateLimits", out var legacy))
        {
            return legacy;
        }

        throw new InvalidOperationException("Codex returned an unsupported rate-limit response.");
    }

    private static UsageWindow? ReadWindow(JsonElement limits, string propertyName)
    {
        if (!limits.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!value.TryGetProperty("usedPercent", out var usedValue) ||
            usedValue.ValueKind != JsonValueKind.Number ||
            !usedValue.TryGetInt32(out var used))
        {
            return null;
        }
        long? duration = value.TryGetProperty("windowDurationMins", out var durationValue) &&
                         durationValue.ValueKind == JsonValueKind.Number &&
                         durationValue.TryGetInt64(out var durationMinutes)
            ? durationMinutes
            : null;
        DateTimeOffset? resetsAt = value.TryGetProperty("resetsAt", out var resetValue) &&
                                   resetValue.ValueKind == JsonValueKind.Number &&
                                   resetValue.TryGetInt64(out var resetSeconds) &&
                                   resetSeconds >= DateTimeOffset.MinValue.ToUnixTimeSeconds() &&
                                   resetSeconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(resetSeconds).ToLocalTime()
            : null;
        return new UsageWindow(used, duration, resetsAt);
    }
}

internal static class CodexLocator
{
    public static string Find()
    {
        var configured = Environment.GetEnvironmentVariable("CODEXTRAY_CODEX_PATH");
        if (IsFile(configured))
        {
            return Path.GetFullPath(configured!);
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in new[] { "codex.exe", "codex.cmd" })
            {
                var candidate = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var desktopBin = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        if (Directory.Exists(desktopBin))
        {
            var desktopExecutable = Directory.EnumerateFiles(desktopBin, "codex.exe", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (desktopExecutable is not null)
            {
                return desktopExecutable.FullName;
            }
        }

        throw new FileNotFoundException(
            "Codex was not found. Install/sign in to Codex, or set CODEXTRAY_CODEX_PATH to codex.exe.");
    }

    private static bool IsFile(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);
}
