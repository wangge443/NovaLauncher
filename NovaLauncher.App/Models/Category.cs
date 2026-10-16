namespace NovaLauncher.App.Models;

public sealed class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public long SortOrder { get; set; }
}
