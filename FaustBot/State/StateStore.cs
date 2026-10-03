using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FaustBot.State;

/// <summary>Runtime state that must survive restarts. Saved to state.json next to config.json.</summary>
public sealed class BotState
{
    public ulong? EmbedChannelId { get; set; }
    public ulong? EmbedMessageId { get; set; }
}

public sealed class StateStore(ILogger<StateStore> logger)
{
    public const string FileName = "state.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(AppContext.BaseDirectory, FileName);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private BotState? _state;

    public async Task<T> ReadAsync<T>(Func<BotState, T> read)
    {
        await _lock.WaitAsync();
        try
        {
            return read(await LoadAsync());
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(Action<BotState> update)
    {
        await _lock.WaitAsync();
        try
        {
            var state = await LoadAsync();
            update(state);

            // Write to a temp file and swap it in, so a crash mid-write can't leave a truncated state.json.
            var tempPath = _path + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions);
            }
            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<BotState> LoadAsync()
    {
        if (_state is not null)
        {
            return _state;
        }

        if (!File.Exists(_path))
        {
            return _state = new BotState();
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            _state = await JsonSerializer.DeserializeAsync<BotState>(stream, JsonOptions) ?? new BotState();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "{File} is corrupt and will be replaced.", FileName);
            _state = new BotState();
        }

        return _state;
    }
}
