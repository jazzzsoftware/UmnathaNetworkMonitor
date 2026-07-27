namespace NetworkMonitor.ViewModels
{
    internal readonly record struct LocalFlowIdentity(string ProcessName, string RemoteIp, int Protocol, int RemotePort);
}
