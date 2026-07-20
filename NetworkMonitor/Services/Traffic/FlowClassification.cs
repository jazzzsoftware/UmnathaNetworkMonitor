namespace NetworkMonitor.Services.Traffic
{
    public readonly record struct FlowClassification(FlowCategory Category, string? ServiceTag);
}
