using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Web.Infrastructure.Outbox;

namespace Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
	public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.Entity<OutboxMessage>(b =>
		{
			b.ToTable("OutboxMessages");
			b.HasKey(x => x.Id);
			b.Property(x => x.TenantKey).IsRequired();
			b.Property(x => x.Type).IsRequired();
			b.Property(x => x.Payload).IsRequired();
			b.Property(x => x.OccurredOnUtc).HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
			b.Property(x => x.ProcessedOnUtc).HasConversion(v => v.HasValue ? v.Value.UtcDateTime : (DateTime?)null, v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : (DateTimeOffset?)null);
		});
	}
}
