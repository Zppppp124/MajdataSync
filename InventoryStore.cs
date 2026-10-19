using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataSync;

internal sealed class InventoryStore
{
    private const string FileName = ".majdata-sync-index.json";
    private readonly string _rootPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public InventoryStore(string rootPath)
    {
        _rootPath = rootPath;
    }

    private string FilePath => Path.Combine(_rootPath, FileName);

    public async Task<SyncInventory> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            SyncInventory inventory;
            if (File.Exists(FilePath))
            {
                await using var stream = File.OpenRead(FilePath);
                inventory = await JsonSerializer.DeserializeAsync<SyncInventory>(
                    stream, JsonOptions.Default, cancellationToken) ?? new SyncInventory();
            }
            else
            {
                inventory = new SyncInventory();
            }

            inventory.RootPath = _rootPath;
            inventory.Charts ??= new Dictionary<string, SyncRecord>(StringComparer.OrdinalIgnoreCase);
            return inventory;
        }
        catch (JsonException)
        {
            return new SyncInventory { RootPath = _rootPath };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(SyncInventory inventory, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_rootPath);
            inventory.RootPath = _rootPath;
            var temp = FilePath + ".tmp";
            await using (var stream = new FileStream(
                             temp, FileMode.Create, FileAccess.Write, FileShare.None,
                             64 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, inventory, JsonOptions.Default, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
