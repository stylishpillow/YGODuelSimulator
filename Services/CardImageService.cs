using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using YGODuelSimulator.Data;

namespace YGODuelSimulator.Services;

public enum CardImageSize { Full, Small, Cropped }

/// <summary>
/// Lazily downloads and caches card images on disk. The API guide requires
/// callers to store images locally rather than hotlink the image server, so the
/// first request for an image fetches it once and every later request is served
/// straight from the local cache under &lt;base&gt;/images/&lt;size&gt;/&lt;id&gt;.jpg.
/// </summary>
public class CardImageService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    // Cap how many images download at once so we never hammer the image server.
    private static readonly SemaphoreSlim DownloadConcurrency = new(4);

    // Prevents two callers from downloading the same file simultaneously.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new();

    private readonly string _imagesRoot;

    public CardImageService(string? imagesRoot = null)
    {
        _imagesRoot = imagesRoot ?? Path.Combine(AppPaths.BaseDirectory, "images");
    }

    public string GetCachedPath(long apiImageId, CardImageSize size)
        => Path.Combine(_imagesRoot, FolderFor(size), $"{apiImageId}.jpg");

    public bool IsCached(long apiImageId, CardImageSize size)
        => File.Exists(GetCachedPath(apiImageId, size));

    /// <summary>
    /// Returns the local file path for an image, downloading and caching it from
    /// <paramref name="sourceUrl"/> if it isn't already on disk.
    /// </summary>
    public async Task<string> GetImagePathAsync(
        long apiImageId,
        string sourceUrl,
        CardImageSize size = CardImageSize.Full,
        CancellationToken cancellationToken = default)
    {
        var path = GetCachedPath(apiImageId, size);
        if (File.Exists(path)) return path;

        var gate = FileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have finished the download while we waited.
            if (File.Exists(path)) return path;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            byte[] bytes;
            await DownloadConcurrency.WaitAsync(cancellationToken);
            try
            {
                bytes = await Http.GetByteArrayAsync(sourceUrl, cancellationToken);
            }
            finally
            {
                DownloadConcurrency.Release();
            }

            // Write to a temp file first, then move into place so a cancelled or
            // failed download never leaves a corrupt image in the cache.
            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, cancellationToken);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        finally
        {
            gate.Release();
            FileLocks.TryRemove(path, out _);
        }
    }

    private static string FolderFor(CardImageSize size) => size switch
    {
        CardImageSize.Small => "small",
        CardImageSize.Cropped => "cropped",
        _ => "full",
    };
}
