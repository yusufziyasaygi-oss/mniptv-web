using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MNIPTV.UI;

/// Small in-memory image cache so posters and logos do not reload while scrolling.
public static class ImageCache
{
    private static readonly Dictionary<string, BitmapSource> Cache = new();
    private static readonly LinkedList<string> Order = new();
    private static readonly Dictionary<string, Task<BitmapSource?>> Running = new();
    private static readonly HashSet<string> Failed = new();
    private static readonly SemaphoreSlim Gate = new(8);
    private const int Limit = 700;

    public static BitmapSource? Cached(string url) => Cache.TryGetValue(url, out var b) ? b : null;

    /// Must be called on the UI thread.
    public static Task<BitmapSource?> Load(string url)
    {
        if (Cache.TryGetValue(url, out var hit)) return Task.FromResult<BitmapSource?>(hit);
        if (Failed.Contains(url)) return Task.FromResult<BitmapSource?>(null);
        if (Running.TryGetValue(url, out var running)) return running;
        var task = Fetch(url);
        Running[url] = task;
        return Finish(url, task);
    }

    private static async Task<BitmapSource?> Finish(string url, Task<BitmapSource?> task)
    {
        var img = await task;
        Running.Remove(url);
        if (img == null) { Failed.Add(url); return null; }
        Cache[url] = img;
        Order.AddLast(url);
        while (Order.Count > Limit)
        {
            Cache.Remove(Order.First!.Value);
            Order.RemoveFirst();
        }
        return img;
    }

    private static async Task<BitmapSource?> Fetch(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return null;
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var bytes = await XtreamApi.Client.GetByteArrayAsync(uri, cts.Token).ConfigureAwait(false);
            return Decode(bytes);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            BitmapSource result = bmp;
            // Keep memory bounded: posters are shown at most ~300 px wide.
            if (bmp.PixelWidth > 400)
            {
                var scale = 400.0 / bmp.PixelWidth;
                result = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
            }
            result.Freeze();
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// Image that loads its source from a URL through the cache.
public sealed class RemoteImage : Image
{
    private string? _url;

    public RemoteImage(bool fill = true)
    {
        Stretch = fill ? Stretch.UniformToFill : Stretch.Uniform;
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public string? Url
    {
        get => _url;
        set
        {
            if (_url == value) return;
            _url = value;
            Source = null;
            if (string.IsNullOrWhiteSpace(value)) return;
            var hit = ImageCache.Cached(value);
            if (hit != null) { Source = hit; return; }
            _ = LoadAsync(value);
        }
    }

    private async Task LoadAsync(string url)
    {
        var img = await ImageCache.Load(url);
        if (_url == url) Source = img;
    }
}
