using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SessionStore(AppPaths? paths = null) => _paths = paths ?? new AppPaths();

    public string ActiveSessionPath => _paths.ActiveSessionPath;

    public async Task<OperationResult> ProbeWriteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_paths.RootDirectory);
            var probe = Path.Combine(_paths.RootDirectory, $"session-probe.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probe, "ok", cancellationToken);
            File.Delete(probe);
            return OperationResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                "元に戻すための記録を書き込めないため、Windows設定を変更しません。",
                ex.ToString());
        }
    }

    public async Task<OperationResult> SaveAsync(
        ModeSessionSnapshot session,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_paths.RootDirectory);
            var temporaryPath = Path.Combine(
                _paths.RootDirectory, $"active-session.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    16 * 1024, FileOptions.WriteThrough | FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(stream, session, JsonOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                File.Move(temporaryPath, _paths.ActiveSessionPath, true);
                return OperationResult.Success();
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return OperationResult.Failure("元に戻すための記録を保存できませんでした。", ex.ToString());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResult<ModeSessionSnapshot?>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_paths.ActiveSessionPath))
                return OperationResult<ModeSessionSnapshot?>.Success(null);

            await using var stream = new FileStream(
                _paths.ActiveSessionPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous);
            var session = await JsonSerializer.DeserializeAsync<ModeSessionSnapshot>(
                stream, JsonOptions, cancellationToken);
            return session is null || session.SchemaVersion != 1
                ? OperationResult<ModeSessionSnapshot?>.Failure("前回のモード設定記録を読み込めませんでした。")
                : OperationResult<ModeSessionSnapshot?>.Success(session);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return OperationResult<ModeSessionSnapshot?>.Failure(
                "前回のモード設定記録を読み込めませんでした。", ex.ToString());
        }
    }

    public OperationResult Delete()
    {
        try
        {
            if (File.Exists(_paths.ActiveSessionPath))
                File.Delete(_paths.ActiveSessionPath);
            return OperationResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure("使用済みの復元記録を削除できませんでした。", ex.ToString());
        }
    }

    public OperationResult Ignore()
    {
        try
        {
            if (!File.Exists(_paths.ActiveSessionPath))
                return OperationResult.Success();
            Directory.CreateDirectory(_paths.BackupDirectory);
            var destination = Path.Combine(
                _paths.BackupDirectory,
                $"ignored-session-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            File.Move(_paths.ActiveSessionPath, destination, true);
            return OperationResult.Success($"モード設定の記録を {destination} へ退避しました。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure("モード設定の記録を退避できませんでした。", ex.ToString());
        }
    }
}
