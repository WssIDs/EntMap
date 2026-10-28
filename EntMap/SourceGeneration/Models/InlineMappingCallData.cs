namespace EntMap.SourceGeneration.Models;

public sealed class InlineMappingCallData
{
    public string UniqueMethodName { get; init; } = string.Empty;
    public string SourceType { get; init; } = string.Empty;
    public string TargetType { get; init; } = string.Empty;
    
    // Новые данные для стабильных интерцепторов .NET 10
    public int InterceptorVersion { get; init; }
    public string InterceptorData { get; init; } = string.Empty;

    public Dictionary<string, string> CustomExpressions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> SourcePropertyNames { get; } = [];
    public List<PropertyMappingInfo> TargetProperties { get; } = [];
}