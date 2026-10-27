namespace EntMap.SourceGeneration.Models;

public sealed class PropertyMappingInfo
{
    public string Name { get; init; } = string.Empty;
    public string TypeDisplay { get; init; } = string.Empty;
    public bool IsSimple { get; init; }
    public bool IsRequired { get; init; }
    public bool IsEnumerable { get; init; }
    public string? ItemTypeDisplay { get; set; }
    public bool HasCustomExpression { get; set; }
    public string CustomExpressionText { get; set; } = string.Empty;
}