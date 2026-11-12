using System.Collections.Generic;

namespace FilmApp;

public class MovieItem
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string Genres { get; init; } = "";
    public string ScoreText { get; init; } = "";
    public string PosterUrl { get; init; } = "";
    public string Overview { get; init; } = "";
    public int? Year { get; init; }
    public int Votes { get; init; }
    public double? Avg { get; init; }

    public string Initial => string.IsNullOrWhiteSpace(Title) ? "?" : Title.Trim()[..1].ToUpper();

    public string GenresText => Genres.Replace("|", " · ");

    public string MetaText
    {
        get
        {
            var parts = new List<string>();
            if (Year is int year) parts.Add(year.ToString());
            if (Votes > 0) parts.Add($"{Votes} оценок");
            if (Avg is double avg) parts.Add($"★ {avg:0.00}");
            return string.Join(" · ", parts);
        }
    }

    public override string ToString() => Title;
}
