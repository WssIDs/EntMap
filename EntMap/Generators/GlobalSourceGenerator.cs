using System.Text;
using EntMap.Constants;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace EntMap.Generators;

[Generator]
public class GlobalSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var rootNamespaceProvider = context.AnalyzerConfigOptionsProvider
            .Select((options, _) =>
            {
                if (options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace)
                    && !string.IsNullOrWhiteSpace(rootNamespace))
                {
                    return rootNamespace;
                }

                return GlobalMapperConstants.MainNamespace; // Дефолтный namespace, если не удалось определить
            });
        
        context.RegisterSourceOutput(rootNamespaceProvider, (ctx, rootNamespace) =>
        {
            ctx.AddSource($"{GlobalSourceGeneratorConstants.EntMappingProfileClassName}.g.cs",
                SourceText.From(GlobalSourceGeneratorConstants.GetEntMappingProfileSource(rootNamespace), Encoding.UTF8));
            
            ctx.AddSource($"{GlobalSourceGeneratorConstants.MapContextClassName}.g.cs",
                SourceText.From(GlobalSourceGeneratorConstants.GetMapContextSource, Encoding.UTF8));
            
            ctx.AddSource($"{GlobalSourceGeneratorConstants.MappingContextClassName}.g.cs",
                SourceText.From(GlobalSourceGeneratorConstants.GetMappingContextSource, Encoding.UTF8));

            ctx.AddSource($"{AttributeSourceGeneratorConstants.MapperAttributeClassName}.g.cs",
                SourceText.From(AttributeSourceGeneratorConstants.GetInfrastructureSource, Encoding.UTF8));
        });
    }
}