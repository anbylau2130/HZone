namespace HZone.Core.Entities;

public class Journal : Content
{
    public string Title { get; set; } = null!;

    public string MarkdownBody { get; set; } = null!;
}
