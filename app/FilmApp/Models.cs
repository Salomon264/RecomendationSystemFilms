namespace FilmApp;

public class MovieItem
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string Genres { get; init; } = "";
    public string ScoreText { get; init; } = "";

    public override string ToString() => Title;
}
