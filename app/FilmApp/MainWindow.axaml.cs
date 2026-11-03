using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace FilmApp;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new() {         BaseAddress = new Uri("http://127.0.0.1:8010/") };

    public MainWindow()
    {
        InitializeComponent();
        SetStatus("API: http://127.0.0.1:8010");
    }

    private int UserId => int.TryParse(UserIdBox.Text, out var id) ? id : 1;

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = error ? Brushes.IndianRed : Brushes.Gray;
    }

    private static List<MovieItem> Parse(string json, params string[] scoreKeys)
    {
        var list = new List<MovieItem>();
        using var doc = JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var score = "";
            foreach (var key in scoreKeys)
            {
                if (e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
                    score = v.GetDouble().ToString("0.###");
            }

            list.Add(new MovieItem
            {
                Id = e.GetProperty("movieId").GetInt32(),
                Title = e.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                Genres = e.TryGetProperty("genres", out var g) ? g.GetString() ?? "" : "",
                ScoreText = score,
            });
        }
        return list;
    }

    private async Task Safe(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка: " + ex.Message, true);
        }
    }

    private void Show(List<MovieItem> items, string header)
    {
        RecsList.ItemsSource = items;
        RecsHeader.Text = header;
        SetStatus($"Найдено: {items.Count}");
    }

    private async void OnSearch(object? sender, RoutedEventArgs e) => await Safe(async () =>
    {
        var q = Uri.EscapeDataString(SearchBox.Text ?? "");
        MoviesList.ItemsSource = Parse(await Http.GetStringAsync($"movies?query={q}&limit=20"), "mean");
        SetStatus("Выберите фильм в списке слева");
    });

    private async void OnPopular(object? sender, RoutedEventArgs e) => await Safe(async () =>
        Show(Parse(await Http.GetStringAsync("popular?n=10"), "score"), "Топ-10 популярных фильмов"));

    private MovieItem? SelectedMovie => MoviesList.SelectedItem as MovieItem;

    private async void OnSimilarSvd(object? sender, RoutedEventArgs e) => await Similar("svd");

    private async void OnSimilarContent(object? sender, RoutedEventArgs e) => await Similar("content");

    private async Task Similar(string by) => await Safe(async () =>
    {
        var movie = SelectedMovie;
        if (movie is null) { SetStatus("Сначала выберите фильм слева", true); return; }
        var title = Uri.EscapeDataString(movie.Title);
        var name = by == "svd" ? "по оценкам (SVD)" : "по контенту";
        Show(Parse(await Http.GetStringAsync($"similar?title={title}&n=10&by={by}"), "similarity"),
            $"Похожие на «{movie.Title}» ({name})");
    });

    private async void OnMyRecs(object? sender, RoutedEventArgs e) => await Safe(async () =>
    {
        var items = Parse(await Http.GetStringAsync($"ratings/{UserId}/recommendations?n=10"), "score");
        Show(items, $"Рекомендации для пользователя {UserId} по сохранённым оценкам");
    });

    private async void OnMyRatings(object? sender, RoutedEventArgs e) => await Safe(async () =>
    {
        var json = await Http.GetStringAsync($"ratings/{UserId}");
        var items = new List<MovieItem>();
        using var doc = JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            items.Add(new MovieItem
            {
                Id = e.GetProperty("movie_id").GetInt32(),
                Title = e.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                Genres = "",
                ScoreText = e.GetProperty("rating").GetDouble().ToString("0.#"),
            });
        }
        Show(items, $"Оценки пользователя {UserId} (фильм — оценка)");
    });

    private async void OnSaveRating(object? sender, RoutedEventArgs e) => await Safe(async () =>
    {
        var movie = SelectedMovie;
        if (movie is null) { SetStatus("Сначала выберите фильм слева", true); return; }
        var rating = RatingBox.SelectedIndex + 1;
        var body = new StringContent(
            JsonSerializer.Serialize(new { user_id = UserId, movie_id = movie.Id, rating }),
            Encoding.UTF8, "application/json");
        var response = await Http.PostAsync("ratings", body);
        if (!response.IsSuccessStatusCode)
            throw new Exception(await response.Content.ReadAsStringAsync());
        SetStatus($"Сохранено: «{movie.Title}» — {rating}");
    });

    private async void OnHelp(object? sender, RoutedEventArgs e) => await Safe(async () =>
    {
        var json = await Http.GetStringAsync("help");
        var lines = new List<MovieItem>();
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            lines.Add(new MovieItem { Title = p.Name, Genres = p.Value.GetString() ?? "" });
        }
        Show(lines, "Справка по командам API");
    });
}
