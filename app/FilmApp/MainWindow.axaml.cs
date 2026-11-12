using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace FilmApp;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:8010/"),
        Timeout = TimeSpan.FromSeconds(30),
    };

    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#8B93A7"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#E5766B"));
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#E5B454"));

    public MainWindow()
    {
        InitializeComponent();

        ResultsList.SelectionChanged += (_, _) => _ = ShowDetailsAsync(ResultsList.SelectedItem as MovieItem);
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) OnSearch(this, new RoutedEventArgs());
        };

        SetStatus("Готово · выберите подборку или найдите фильм");
        ShowEmptyState();
        Opened += async (_, _) => await Safe(async () =>
            Show(Parse(await Http.GetStringAsync("popular?n=20"), "score"), "Топ-20 по взвешенному рейтингу"));
    }

    private int UserId => int.TryParse(UserIdBox.Text, out var id) && id > 0 ? id : 1;

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = error ? ErrorBrush : MutedBrush;
    }

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double? Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static MovieItem Read(JsonElement e, string score = "")
    {
        var id = e.TryGetProperty("movieId", out var mid) ? mid.GetInt32()
            : e.TryGetProperty("movie_id", out var mid2) ? mid2.GetInt32() : 0;
        var year = Number(e, "year");
        var votes = Number(e, "votes");
        return new MovieItem
        {
            Id = id,
            Title = Text(e, "title"),
            Genres = Text(e, "genres"),
            ScoreText = score,
            PosterUrl = Text(e, "poster_url"),
            Overview = Text(e, "overview"),
            Year = year is null ? null : (int)year.Value,
            Votes = votes is null ? 0 : (int)votes.Value,
            Avg = Number(e, "avg_rating") ?? Number(e, "mean"),
        };
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
                var value = Number(e, key);
                if (value is not null) score = value.Value.ToString("0.###");
            }
            list.Add(Read(e, score));
        }
        return list;
    }

    private async Task Safe(Func<Task> action)
    {
        BusyBar.IsVisible = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка: " + ex.Message, true);
        }
        finally
        {
            BusyBar.IsVisible = false;
        }
    }

    private void Show(List<MovieItem> items, string header)
    {
        ResultsList.ItemsSource = items;
        ResultsHeader.Text = header;
        ResultsCount.Text = items.Count.ToString();
        SetStatus($"Найдено: {items.Count}");
        if (items.Count > 0) ResultsList.SelectedIndex = 0;
        else ShowEmptyState();
    }

    private void ShowEmptyState()
    {
        DetailPoster.Source = null;
        PosterFallback.Text = "?";
        DetailTitle.Text = "Ничего не выбрано";
        DetailSubtitle.Text = "Найдите фильм или откройте подборку";
        GenresList.ItemsSource = null;
        DetailAvg.Text = "—";
        DetailVotes.Text = "—";
        DetailScore.Text = "—";
        DetailOverview.Text = "Выберите фильм в списке слева — здесь появится постер и описание.";
    }

    private async Task ShowDetailsAsync(MovieItem? movie)
    {
        if (movie is null)
        {
            ShowEmptyState();
            return;
        }

        FillDetails(movie);
        if (movie.Id <= 0) return;

        var details = await LoadDetailsAsync(movie.Id, movie.ScoreText);
        if (details is null) return;
        if ((ResultsList.SelectedItem as MovieItem)?.Id != movie.Id) return;

        FillDetails(details);
    }

    private void FillDetails(MovieItem movie)
    {
        DetailTitle.Text = movie.Title;
        DetailSubtitle.Text = string.IsNullOrEmpty(movie.MetaText) ? "Описание фильма" : movie.MetaText;
        PosterFallback.Text = movie.Initial;
        PosterLoader.SetUrl(DetailPoster, movie.PosterUrl);
        GenresList.ItemsSource = string.IsNullOrWhiteSpace(movie.GenresText)
            ? null
            : movie.GenresText.Split(" · ", StringSplitOptions.RemoveEmptyEntries);

        DetailAvg.Text = movie.Avg is double avg ? avg.ToString("0.00") : "—";
        DetailVotes.Text = movie.Votes > 0 ? movie.Votes.ToString() : "—";
        DetailScore.Text = string.IsNullOrEmpty(movie.ScoreText) ? "—" : movie.ScoreText;
        DetailOverview.Text = string.IsNullOrWhiteSpace(movie.Overview)
            ? "Описание отсутствует."
            : movie.Overview;
    }

    private async Task<MovieItem?> LoadDetailsAsync(int id, string score)
    {
        try
        {
            var json = await Http.GetStringAsync($"movies/{id}");
            using var doc = JsonDocument.Parse(json);
            return Read(doc.RootElement, score);
        }
        catch
        {
            return null;
        }
    }

    private async void OnSearch(object? sender, RoutedEventArgs e) => await Safe(async () =>
    {
        var q = Uri.EscapeDataString(SearchBox.Text ?? "");
        Show(Parse(await Http.GetStringAsync($"movies?query={q}&limit=20"), "mean"),
            string.IsNullOrWhiteSpace(SearchBox.Text) ? "Все фильмы" : $"Поиск: {SearchBox.Text}");
    });

    private async void OnPopular(object? sender, RoutedEventArgs e) => await Safe(async () =>
        Show(Parse(await Http.GetStringAsync("popular?n=20"), "score"), "Топ-20 по взвешенному рейтингу"));

    private async void OnSimilarSvd(object? sender, RoutedEventArgs e) => await Similar("svd");

    private async void OnSimilarContent(object? sender, RoutedEventArgs e) => await Similar("content");

    private async Task Similar(string by) => await Safe(async () =>
    {
        var movie = ResultsList.SelectedItem as MovieItem;
        if (movie is null || movie.Id <= 0)
        {
            SetStatus("Сначала выберите фильм в списке слева", true);
            return;
        }

        var title = Uri.EscapeDataString(movie.Title);
        var name = by == "svd" ? "по оценкам" : "по жанрам и описанию";
        Show(Parse(await Http.GetStringAsync($"similar?title={title}&n=20&by={by}"), "similarity"),
            $"Похожие на «{movie.Title}» {name}");
    });

    private async void OnMyRecs(object? sender, RoutedEventArgs e) => await Safe(async () =>
        Show(Parse(await Http.GetStringAsync($"ratings/{UserId}/recommendations?n=20"), "score"),
            $"Рекомендации для профиля {UserId}"));

    private async void OnMyRatings(object? sender, RoutedEventArgs e) => await Safe(async () =>
    {
        var json = await Http.GetStringAsync($"ratings/{UserId}");
        var items = new List<MovieItem>();
        using var doc = JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var item = Read(e, "★ " + e.GetProperty("rating").GetDouble().ToString("0.#"));
            items.Add(item);
        }

        if (items.Count == 0)
        {
            Show(items, $"Оценки профиля {UserId}");
            SetStatus($"У профиля {UserId} пока нет сохранённых оценок");
            return;
        }

        Show(items, $"Оценки профиля {UserId}");
    });

    private async void OnSaveRating(object? sender, RoutedEventArgs e) => await Safe(async () =>
    {
        var movie = ResultsList.SelectedItem as MovieItem;
        if (movie is null || movie.Id <= 0)
        {
            SetStatus("Сначала выберите фильм в списке слева", true);
            return;
        }

        var rating = RatingBox.SelectedIndex + 1;
        var body = new StringContent(
            JsonSerializer.Serialize(new { user_id = UserId, movie_id = movie.Id, rating }),
            Encoding.UTF8, "application/json");
        var response = await Http.PostAsync("ratings", body);
        if (!response.IsSuccessStatusCode)
            throw new Exception(await response.Content.ReadAsStringAsync());

        SetStatus($"Сохранено: «{movie.Title}» — {rating}");
        StatusText.Foreground = OkBrush;
    });

    private async void OnHelp(object? sender, RoutedEventArgs e) => await Safe(async () =>
    {
        var json = await Http.GetStringAsync("help");
        var items = new List<MovieItem>();
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            items.Add(new MovieItem
            {
                Title = p.Name,
                Overview = p.Value.GetString() ?? "",
                ScoreText = "API",
            });
        }
        Show(items, "Методы API");
    });
}
