using Makaretu.Dns;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.Core.Scanning;

namespace NetworkMonitor.Services.Scanning
{
    public class MdnsProbe
    {
        public async Task<IReadOnlyDictionary<string, MdnsInfo>> DiscoverAsync(TimeSpan window, CancellationToken ct)
        {
            IReadOnlyDictionary<string, MdnsInfo> result = new Dictionary<string, MdnsInfo>();

            try
            {
                List<Message> messages = new List<Message>();
                object gate = new object();

                using MulticastService multicast = new MulticastService();
                using ServiceDiscovery serviceDiscovery = new ServiceDiscovery(multicast);

                void OnAnswer(object? sender, MessageEventArgs eventArgs)
                {

                    lock (gate)
                    {
                        messages.Add(eventArgs.Message);
                    }

                }

                multicast.AnswerReceived += OnAnswer;
                multicast.Start();
                serviceDiscovery.QueryAllServices();

                try
                {
                    await Task.Delay(window, ct);
                }
                catch (OperationCanceledException)
                {
                }

                multicast.AnswerReceived -= OnAnswer;

                List<Message> snapshot;

                lock (gate)
                {
                    snapshot = new List<Message>(messages);
                }

                result = Flatten(snapshot);
            }
            catch (Exception exception)
            {
                AppLog.Error("MdnsProbe.DiscoverAsync", exception);
            }

            return result;
        }

        private static IReadOnlyDictionary<string, MdnsInfo> Flatten(IReadOnlyList<Message> messages)
        {
            List<MdnsAddressRecord> addresses = new List<MdnsAddressRecord>();
            List<MdnsPointerRecord> pointers = new List<MdnsPointerRecord>();
            List<MdnsServiceRecord> services = new List<MdnsServiceRecord>();
            List<MdnsTextRecord> texts = new List<MdnsTextRecord>();

            foreach (Message message in messages)
            {

                foreach (ResourceRecord record in message.Answers.Concat(message.AdditionalRecords))
                {

                    if (record is ARecord addressRecord)
                    {
                        addresses.Add(new MdnsAddressRecord(addressRecord.Name.ToString(), addressRecord.Address.ToString()));
                    }
                    else if (record is PTRRecord pointerRecord)
                    {
                        pointers.Add(new MdnsPointerRecord(pointerRecord.Name.ToString(), pointerRecord.DomainName.ToString()));
                    }
                    else if (record is SRVRecord serviceRecord)
                    {
                        services.Add(new MdnsServiceRecord(serviceRecord.Name.ToString(), serviceRecord.Target.ToString()));
                    }
                    else if (record is TXTRecord textRecord)
                    {
                        texts.Add(new MdnsTextRecord(textRecord.Name.ToString(), textRecord.Strings));
                    }

                }

            }

            IReadOnlyDictionary<string, MdnsInfo> result = MdnsResponseParser.Parse(addresses, pointers, services, texts);

            return result;
        }
    }
}
