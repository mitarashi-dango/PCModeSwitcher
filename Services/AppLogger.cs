using System.IO;
using System.Text;

namespace PCModeSwitcher.Services;

public sealed class AppLogger
{
    private const long MaximumLogBytes = 50L * 1024L * 1024L;
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppLogger(AppPaths? paths = null) => _paths = paths ?? new AppPaths();

    public string LogDirectory => _paths.LogDirectory;

    public async Task WriteAsync(
        string level,
        Guid? sessionId,
        string? modeId,
        string action,
        string result,
        string? details = null)
    {
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_paths.LogDirectory);
            Cleanup();
            var path = Path.Combine(_paths.LogDirectory, $"pc-mode-switcher-{DateTime.UtcNow:yyyy-MM-dd}.log");
            var safeDetails = details?.Replace('\r', ' ').Replace('\n', ' ');
            var line = string.Join('\t',
                DateTimeOffset.UtcNow.ToString("O"),
                level,
                sessionId?.ToString("D") ?? "-",
                modeId ?? "-",
                action,
                result,
                safeDetails ?? "-") + Environment.NewLine;
            await File.AppendAllTextAsync(path, line, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Cleanup()
    {
        var files = new DirectoryInfo(_paths.LogDirectory)
            .EnumerateFiles("*.log")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();
        var cutoff = DateTime.UtcNow.AddDays(-30);
        long retainedBytes = 0;
        foreach (var file in files)
        {
            retainedBytes += file.Length;
            if (file.LastWriteTimeUtc < cutoff || retainedBytes > MaximumLogBytes)
            {
                try { file.Delete(); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
