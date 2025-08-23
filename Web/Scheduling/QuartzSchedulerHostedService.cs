using Quartz;
using Quartz.Spi;
using Web.MultiTenancy;

namespace Web.Scheduling;

public sealed class QuartzSchedulerHostedService(ISchedulerFactory schedulerFactory, TenantRegistry registry) : IHostedService
{
	private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;
	private readonly TenantRegistry _registry = registry;
	private IScheduler? _scheduler;

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		_scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
		await _scheduler.Start(cancellationToken);

		foreach (var tenantKey in _registry.Tenants.Keys)
		{
			var job = JobBuilder.Create<TenantSampleJob>()
				.WithIdentity($"TenantSampleJob-{tenantKey}")
				.UsingJobData("tenantKey", tenantKey)
				.Build();

			var trigger = TriggerBuilder.Create()
				.WithIdentity($"TenantSampleTrigger-{tenantKey}")
				.StartNow()
				.WithSimpleSchedule(x => x.WithIntervalInSeconds(15).RepeatForever())
				.Build();

			await _scheduler.ScheduleJob(job, trigger, cancellationToken);
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		if (_scheduler is not null)
		{
			await _scheduler.Shutdown(waitForJobsToComplete: true, cancellationToken);
		}
	}
}