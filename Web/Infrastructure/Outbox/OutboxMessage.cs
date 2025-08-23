namespace Web.Infrastructure.Outbox;

public sealed class OutboxMessage
{
	public Guid Id { get; set; }
	public required string TenantKey { get; set; }
	public required string Type { get; set; }
	public required string Payload { get; set; }
	public DateTimeOffset OccurredOnUtc { get; set; }
	public DateTimeOffset? ProcessedOnUtc { get; set; }
	public int Attempts { get; set; }
	public string? Error { get; set; }
}