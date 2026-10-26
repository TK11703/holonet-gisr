using Gisr.Database.Records;

namespace Gisr.Api.Records;

// Expired and revoked grants are already useless; purging only keeps the table small.
public sealed class ViewGrantPurgeService(ViewGrantRepository grants, ILogger<ViewGrantPurgeService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken) => await grants.PurgeAsync(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var removed = await RunOnceAsync(stoppingToken);
                if (removed > 0)
                {
                    logger.LogInformation("Purged {Count} expired or revoked view grants", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("View grant purge failed with {ExceptionType}", ex.GetType().Name);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
