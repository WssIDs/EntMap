namespace EntMap.SourceGeneration.Models
{
    public class PropertyMappingInfo
    {
        public string Name { get; set; } = string.Empty;
        public string TypeDisplay { get; set; } = string.Empty;
        public bool IsSimple { get; set; }
        public bool IsRequired { get; set; }
        public bool IsEnumerable { get; set; }
        public string? ItemTypeDisplay { get; set; }

        public bool HasCustomExpression { get; set; }
        public string CustomExpressionText { get; set; } = string.Empty;
    }
}