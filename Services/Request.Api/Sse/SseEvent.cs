namespace ReportingPlatform.RequestApi.Sse;

/// <summary>A single SSE event to be written to the client.</summary>
public sealed record SseEvent(string Name, string DataJson);
