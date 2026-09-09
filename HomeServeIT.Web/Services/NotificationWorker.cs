using HomeServeIT.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeServeIT.Web.Services;

public sealed class NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalSeconds = 30;
            try
            {
                await using (var settingsScope = scopes.CreateAsyncScope())
                {
                    var settings = await settingsScope.ServiceProvider.GetRequiredService<ApplicationSettingsService>()
                        .GetAsync(stoppingToken);
                    intervalSeconds = Math.Clamp(settings.NotificationRefreshIntervalSeconds, 15, 3600);
                    if (!settings.NotificationRefreshEnabled)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
                        continue;
                    }
                }

                string? cursor = null;
                while (!stoppingToken.IsCancellationRequested)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var ids = await context.Users.AsNoTracking().Where(u => !u.IsArchived
                        && (cursor == null || string.Compare(u.Id, cursor) > 0))
                        .OrderBy(u => u.Id).Select(u => u.Id).Take(50).ToListAsync(stoppingToken);
                    if (ids.Count == 0) break;
                    foreach (var id in ids)
                    {
                        await using var userScope = scopes.CreateAsyncScope();
                        await userScope.ServiceProvider.GetRequiredService<NotificationService>().SynchronizeUserAsync(id, stoppingToken);
                    }
                    cursor = ids[^1];
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                // Do not include account details, SQL, or connection configuration in logs.
                logger.LogWarning("Notification refresh failed ({ErrorType}); it will retry next cycle.", error.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }
}
