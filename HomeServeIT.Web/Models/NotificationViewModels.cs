namespace HomeServeIT.Web.Models;

public class NotificationFeedViewModel
{
    public IReadOnlyList<UserNotification> Notifications { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyDictionary<string, int> CategoryCounts { get; init; } = new Dictionary<string, int>();
    public string ActiveCategory { get; init; } = "All";
    public string Area { get; init; } = string.Empty;
    public int UnreadCount { get; init; }
    public int TotalCount { get; init; }

    public string AllNotificationsUrl => $"/{Area}/Notifications";
}
