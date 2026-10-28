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
            var encoding = Encoding.UTF8;
            const string extension = ".g.cs";
            
            ctx.AddSource($"{GlobalSourceGeneratorConstants.EntMappingProfileClassName}{extension}",
                SourceText.From(GlobalSourceGeneratorConstants.GetEntMappingProfileSource(rootNamespace), Encoding.UTF8));
            
            ctx.AddSource($"{GlobalSourceGeneratorConstants.MapContextClassName}{extension}",
                SourceText.From(GlobalSourceGeneratorConstants.GetMapContextSource, Encoding.UTF8));
            
            ctx.AddSource($"{GlobalSourceGeneratorConstants.MappingContextClassName}{extension}",
                SourceText.From(GlobalSourceGeneratorConstants.GetMappingContextSource, Encoding.UTF8));

            ctx.AddSource($"{AttributeSourceGeneratorConstants.MapperAttributeClassName}{extension}",
                SourceText.From(AttributeSourceGeneratorConstants.GetInfrastructureSource, Encoding.UTF8));
            
            // Генерируем базовую инфраструктуру прямо в этом же шаге, чтобы гарантировать синхронность namespace!
            ctx.AddSource($"{GlobalMapperConstants.ClassName}{extension}", SourceText.From(MapperSourceGeneratorConstants.GetEntMapGlobalRegisterSource(rootNamespace), encoding));

            
            ctx.AddSource($"{MapperSourceGeneratorConstants.EntMapInlineGlobalRegisterClassName}{extension}", SourceText.From(MapperSourceGeneratorConstants.GetEntMapInlineGlobalRegisterSource(rootNamespace), Encoding.UTF8));
        });
    }
}