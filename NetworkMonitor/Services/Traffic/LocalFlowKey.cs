namespace NetworkMonitor.Services.Traffic
{
    public readonly record struct LocalFlowKey(int Pid, uint RemoteIp);
}
