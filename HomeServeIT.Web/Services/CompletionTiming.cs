using HomeServeIT.Web.Models;

namespace HomeServeIT.Web.Services;

public static class CompletionTiming
{
    // ScheduledDate is an appointment target, not an actual start time. Until actual-start
    // tracking exists, early completion contributes zero elapsed days instead of a negative duration.
    public static double? ResolutionDays(ServiceRequest request) =>
        request.Status == "Completed" && request.CompletedDate is DateTime completed
        && completed <= DateTime.UtcNow
            ? Math.Max(0, (completed - request.ScheduledDate).TotalDays) : null;
    public static double AverageDays(IEnumerable<ServiceRequest> requests) =>
        requests.Select(ResolutionDays).Where(days => days.HasValue).Select(days => days!.Value).DefaultIfEmpty(0).Average();
}
