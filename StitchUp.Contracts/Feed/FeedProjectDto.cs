namespace StitchUp.Contracts.Feed;

public class FeedProjectDto
{
    public Guid ProjectId { get; set; }

    public Guid AuthorUserId { get; set; }

    public string AuthorUserName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public List<FeedClipDto> Clips { get; set; } = new();
}

