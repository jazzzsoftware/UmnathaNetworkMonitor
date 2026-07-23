namespace NetworkMonitor.Core.Traffic
{
    public readonly record struct FlowClassification(FlowCategory Category, string? ServiceTag);
}
