namespace HZone.Api.Endpoints;

public record CreateSayRequest(string Text, List<Guid> RecipientIds);

public record CreateJournalRequest(string Title, string Markdown, List<Guid> RecipientIds);

public record RecipientResponse(Guid UserId, string Nickname);

public record ContentResponse(
    Guid Id,
    string Kind,
    Guid AuthorId,
    string? Text,
    string? Title,
    string? MarkdownBody,
    List<RecipientResponse> Recipients,
    DateTime CreatedAt);

public sealed record RecipientsResult(List<RecipientResponse> Recipients, string? Error);
