namespace NetworkMonitor.Services.Platform
{
    public class InAppNotificationService
    {
        public event Action<string>? NotificationRequested;

        public void Show(string message)
        {
            NotificationRequested?.Invoke(message);
        }
    }
}
