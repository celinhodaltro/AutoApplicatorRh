namespace AutoApplicator.Infrastructure.Automation;

public sealed class LinkedInDedupService
{
    private readonly HashSet<string> _processedIds = [];
    private readonly object _lock = new();

    public bool TryAdd(string externalId)
    {
        lock (_lock) return _processedIds.Add(externalId);
    }

    public void Reset() { lock (_lock) _processedIds.Clear(); }
    public int Count { get { lock (_lock) return _processedIds.Count; } }
}
