using Microsoft.UI.Dispatching;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace NetworkMonitor.Notifications
{
    // Every Windows toast the app raises is built here. Folding four near-identical copies of the
    // XML into one place is the smaller reason; the real one is that a toast's Activated handler
    // lives only as long as something holds the ToastNotification. Left in a local that falls out of
    // scope, the object can be collected while the toast is still on screen, and the click then does
    // nothing at all. Each toast is held until the platform reports it finished with — activated,
    // dismissed or failed.
    //
    // The platform raises those three events on its own thread, so the click callback is marshalled
    // back onto the UI thread before it reaches anything that navigates.
    public sealed class ToastPresenter
    {
        private const string TwoLineTemplate =
            "<toast><visual><binding template=\"ToastGeneric\"><text id=\"1\"/><text id=\"2\"/></binding></visual><audio silent=\"true\"/></toast>";

        private const string ThreeLineTemplate =
            "<toast><visual><binding template=\"ToastGeneric\"><text id=\"1\"/><text id=\"2\"/><text id=\"3\"/></binding></visual><audio silent=\"true\"/></toast>";

        private readonly string _aumid;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly HashSet<ToastNotification> _live = new HashSet<ToastNotification>();
        private readonly object _gate = new object();

        public ToastPresenter(string aumid, DispatcherQueue dispatcherQueue)
        {
            _aumid = aumid;
            _dispatcherQueue = dispatcherQueue;
        }

        public void Show(string title, string firstLine, string? secondLine, TimeSpan expiresIn, Action? onClick)
        {
            ToastNotification toast = Build(title, firstLine, secondLine, expiresIn);

            lock (_gate)
            {
                _live.Add(toast);
            }

            toast.Activated += (ToastNotification sender, object args) =>
            {
                Retire(sender);

                if (onClick is not null)
                {
                    _dispatcherQueue.TryEnqueue(() => onClick());
                }

            };

            toast.Dismissed += (ToastNotification sender, ToastDismissedEventArgs args) => Retire(sender);
            toast.Failed += (ToastNotification sender, ToastFailedEventArgs args) => Retire(sender);

            ToastNotificationManager.CreateToastNotifier(_aumid).Show(toast);
        }

        private ToastNotification Build(string title, string firstLine, string? secondLine, TimeSpan expiresIn)
        {
            XmlDocument toastXml = new XmlDocument();
            toastXml.LoadXml(secondLine is null ? TwoLineTemplate : ThreeLineTemplate);

            XmlNodeList textNodes = toastXml.GetElementsByTagName("text");
            textNodes[0].InnerText = title;
            textNodes[1].InnerText = firstLine;

            if (secondLine is not null)
            {
                textNodes[2].InnerText = secondLine;
            }

            ToastNotification toast = new ToastNotification(toastXml)
            {
                ExpirationTime = DateTimeOffset.Now.Add(expiresIn)
            };

            return toast;
        }

        private void Retire(ToastNotification toast)
        {

            lock (_gate)
            {
                _live.Remove(toast);
            }

        }
    }
}
