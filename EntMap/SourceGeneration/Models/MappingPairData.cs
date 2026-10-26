namespace EntMap.SourceGeneration.Models
{
    public class MappingPairData
    {
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public List<PropertyMappingInfo> TargetProperties { get; set; } = new();
        public List<string> SourcePropertyNames { get; set; } = new();
    }
}