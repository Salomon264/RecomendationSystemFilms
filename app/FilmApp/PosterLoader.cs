using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace FilmApp;

public static class PosterLoader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly Dictionary<string, Bitmap?> Cache = new();
    private static readonly Dictionary<string, Task<Bitmap?>> Pending = new();

    public static readonly AttachedProperty<string?> UrlProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Url", typeof(PosterLoader));

    static PosterLoader()
    {
        UrlProperty.Changed.AddClassHandler<Image>(OnUrlChanged);
    }

    public static string? GetUrl(Image image) => image.GetValue(UrlProperty);

    public static void SetUrl(Image image, string? value) => image.SetValue(UrlProperty, value);

    private static async void OnUrlChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        var url = e.NewValue as string;
        image.Source = null;
        if (string.IsNullOrWhiteSpace(url)) return;

        var bitmap = await LoadAsync(url);
        if (bitmap is not null && GetUrl(image) == url)
            Dispatcher.UIThread.Post(() => image.Source = bitmap);
    }

    private static Task<Bitmap?> LoadAsync(string url)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(url, out var cached)) return Task.FromResult(cached);
            if (Pending.TryGetValue(url, out var running)) return running;
            var task = DownloadAsync(url);
            Pending[url] = task;
            return task;
        }
    }

    private static async Task<Bitmap?> DownloadAsync(string url)
    {
        foreach (var candidate in Candidates(url))
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(candidate);
                using var stream = new MemoryStream(bytes);
                var bitmap = new Bitmap(stream);
                lock (Cache)
                {
                    Cache[url] = bitmap;
                    Pending.Remove(url);
                }
                return bitmap;
            }
            catch
            {
            }
        }

        lock (Cache)
        {
            Cache[url] = null;
            Pending.Remove(url);
        }
        return null;
    }

    private static IEnumerable<string> Candidates(string url)
    {
        yield return url;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var host = uri.Host + uri.PathAndQuery;
            yield return "https://wsrv.nl/?url=" + Uri.EscapeDataString(host) + "&w=500&output=jpg";
        }
    }
}
