using System.Text;
using EntMap.Constants;
using EntMap.Extensions;
using EntMap.SourceGeneration.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace EntMap.Generators;

[Generator]
public class MapperSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Стабильный, независимый шаг для инфраструктуры. Он сработает принудительно для каждого проекта.
        context.RegisterPostInitializationOutput(_ =>
        {
            // На этапе пост-инициализации мы не знаем имя сборки, поэтому используем дефолтный базовый namespace,
            // Но сам файл регистратора мы упакуем в реальный namespace сборки на основном этапе.
        });

        // 1. Сбор всех вызовов во всех проектах приложения
        var detectedMappingPairs = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsMapperMapCall(s),
                transform: static (ctx, _) => GetMappingPairSafe(ctx.Node, ctx.SemanticModel))
            .Where(static pairs => pairs != null)
            .SelectMany(static (x, _) => x!);

        // 2. Исключение дубликатов по строковому представлению
        var uniquePairs = detectedMappingPairs.Collect().SelectMany(static (pairs, _) =>
            pairs.GroupBy(p => new { Src = p.Source, Tgt = p.Target })
                .Select(g => g.First())
        );

        // 2. ИСПРАВЛЕНИЕ: Выделенный, надежный предикат для инлайн-вызовов.
        // Он гарантирует, что узел — это вызов метода, содержащий "Map" и имеющий generic-блок.
        var detectedInlineCalls = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsInlineMapCallSyntax(s),
                // Передаем весь синтаксический контекст ctx вместо ручного разделения на Node и Model
                transform: static (ctx, _) => ctx.GetInlineMappingCallSafe(ctx.SemanticModel))
            .Where(static call => call != null);

        // 3. Объединяем стандартные пары и инлайн-вызовы для генерации в один шаг
        var combinedData = uniquePairs.Collect()
            .Combine(detectedInlineCalls.Collect())
            .Combine(context.CompilationProvider);
        
        // 3. Вывод кода — этот шаг сработает ВСЕГДА, даже если пар 0.
        context.RegisterSourceOutput(combinedData, static (spc, tuple) =>
        {
            var allPairs = tuple.Left.Left;
            var inlineCalls = tuple.Left.Right;
            var compilation = tuple.Right;

            var currentNamespace = compilation.AssemblyName ?? GlobalMapperConstants.MainNamespace;

            var encoding = Encoding.UTF8;
            const string extension = ".g.cs";
            
            // Модифицируем метод сборки: передаем туда коллекцию inlineCalls
            var builder = new EntMap.Builders.GlobalMapperSourceBuilder(allPairs, currentNamespace);
            var code = builder.BuildWithInline(inlineCalls!); 
            
            // Генерируем регистратор
            //var code = allPairs.GenerateGlobalMapperCode(currentNamespace);
            spc.AddSource($"{MapperSourceGeneratorConstants.EntMapGlobalRegisterClassName}{extension}", SourceText.From(code, encoding));
        });
    }

    // ИСПРАВЛЕНИЕ: Точечный синтаксический предикат специально под инлайн-маппинг
    private static bool IsInlineMapCallSyntax(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation) return false;
        
        // Инлайн-маппинг всегда принимает ровно 2 аргумента: (source, cfg => ...)
        if (invocation.ArgumentList.Arguments.Count != 2) return false;

        var exprStr = invocation.Expression.ToString();
        // Проверяем, что в вызове фигурирует Map и generic-скобки
        return exprStr.Contains("Map") && exprStr.Contains("<") && exprStr.Contains(">");
    }
    
    private static bool IsMapperMapCall(SyntaxNode node)
    {
        switch (node)
        {
            case InvocationExpressionSyntax invocation:
            {
                var text = invocation.Expression.ToString();
                return text.Contains(MapperProfileConstants.CreateMapMethodName) || text.Contains(MapperProfileConstants.MapMethodName) || text.Contains(MapperProfileConstants.MemberMethodName);
            }
            case AssignmentExpressionSyntax assignment:
            {
                var text = assignment.Right.ToString();
                return text.Contains(MapperProfileConstants.CreateMapMethodName) || text.Contains(MapperProfileConstants.MapMethodName) || text.Contains(MapperProfileConstants.MemberMethodName);
            }
            default:
                return false;
        }
    }

    private static List<MappingPairData>? GetMappingPairSafe(SyntaxNode node, SemanticModel model)
    {
        try
        {
            // 1. Распаковываем discard (_ = ...) если он есть
            var invocation = node as InvocationExpressionSyntax;
            if (node is AssignmentExpressionSyntax { Right: InvocationExpressionSyntax rightInv })
            {
                invocation = rightInv;
            }

            if (invocation == null) return null;

            // 2. Спускаемся к самому первому вызову в цепочке Fluent API
            var currentExpression = invocation.Expression;
            var rootInvocation = invocation;

            while (currentExpression is MemberAccessExpressionSyntax mae)
            {
                if (mae.Expression is InvocationExpressionSyntax parentInvocation)
                {
                    rootInvocation = parentInvocation;
                    currentExpression = parentInvocation.Expression;
                }
                else
                {
                    break;
                }
            }

            //if (rootInvocation == null) return null;

            // 3. СИНТАКСИЧЕСКИЙ АНАЛИЗ (Без жесткой привязки к Semantic Model)
            // Получаем текстовое имя вызываемого метода (например, "CreateMap" или "Map")
            var methodName = string.Empty;
            GenericNameSyntax? genericName = null;

            switch (rootInvocation.Expression)
            {
                case MemberAccessExpressionSyntax rootMae:
                    methodName = rootMae.Name.Identifier.ValueText;
                    genericName = rootMae.Name as GenericNameSyntax;
                    break;
                case GenericNameSyntax gName:
                    methodName = gName.Identifier.ValueText;
                    genericName = gName;
                    break;
                case IdentifierNameSyntax ins:
                    methodName = ins.Identifier.ValueText;
                    break;
            }

            INamedTypeSymbol? sourceType = null;
            INamedTypeSymbol? targetType = null;

            switch (methodName)
            {
                // Ситуация А: Это объявление в профиле: CreateMap<LoanDto, LoanModel>()
                case MapperProfileConstants.CreateMapMethodName when genericName is { TypeArgumentList.Arguments.Count: 2 }:
                {
                    var sourceSyntax = genericName.TypeArgumentList.Arguments[0];
                    var targetSyntax = genericName.TypeArgumentList.Arguments[1];

                    sourceType = model.GetTypeInfo(sourceSyntax).Type as INamedTypeSymbol;
                    targetType = model.GetTypeInfo(targetSyntax).Type as INamedTypeSymbol;
                    break;
                }
                // Ситуация Б: Это вызов в коде: EntMapper.Map<List<LoanModel>>(response)
                case MapperProfileConstants.MapMethodName when genericName is { TypeArgumentList.Arguments.Count: 1 }:
                {
                    if (rootInvocation.ArgumentList.Arguments.Count == 1)
                    {
                        var targetSyntax = genericName.TypeArgumentList.Arguments[0];
                        var argExpression = rootInvocation.ArgumentList.Arguments[0].Expression;

                        var targetGeneric = model.GetTypeInfo(targetSyntax).Type as INamedTypeSymbol;

                        if (model.GetTypeInfo(argExpression).Type is INamedTypeSymbol sourceGeneric && targetGeneric != null)
                        {
                            // Счищаем обертки коллекций List<T> -> T
                            sourceType = sourceGeneric.ExtractItemTypeSymbol() ?? sourceGeneric;
                            targetType = targetGeneric.ExtractItemTypeSymbol() ?? targetGeneric;
                        }
                    }

                    break;
                }
            }

            if (sourceType == null || targetType == null) return null;

            // Быстрая проверка на корректность типов (исключаем незаполненные ErrorType)
            if (sourceType.TypeKind == TypeKind.Error || targetType.TypeKind == TypeKind.Error) return null;
            if (sourceType.SpecialType != SpecialType.None || targetType.SpecialType != SpecialType.None) return null;

            var result = new List<MappingPairData>();
            var processedPairs = new HashSet<string>();

            DiscoverMappingsRecursive(sourceType, targetType, result, processedPairs, model.Compilation);

            return result;
        }
        catch
        {
            return null;
        }
    }


    private static void DiscoverMappingsRecursive(INamedTypeSymbol source, INamedTypeSymbol target, List<MappingPairData> result, HashSet<string> processedPairs, Compilation compilation)
    {
        while (true)
        {
            var key = $"{source.ToDisplayString()}->{target.ToDisplayString()}";
            if (!processedPairs.Add(key)) return;

            var sourceItem = source.ExtractItemTypeSymbol();
            var targetItem = target.ExtractItemTypeSymbol();

            if (sourceItem != null && targetItem != null)
            {
                source = sourceItem;
                target = targetItem;
                continue;
                //result.Add(CreatePairData(source, target, compilation));
            }

            var currentPair = CreatePairData(source, target, compilation);
            result.Add(currentPair);

            var targetProps = target.GetAllProperties().Where(p => p.SetMethod != null);
            var sourceProps = source.GetAllProperties();

            var sourceList = sourceProps.ToList();
            
            foreach (var tp in targetProps)
            {
                var sp = sourceList.FirstOrDefault(p => string.Equals(p.Name, tp.Name, StringComparison.OrdinalIgnoreCase));
                if (sp == null) continue;

                var tpClean = tp.Type.WithNullableAnnotation(NullableAnnotation.None);
                var spClean = sp.Type.WithNullableAnnotation(NullableAnnotation.None);

                var isTargetSimple = tpClean.IsValueType || tpClean.SpecialType == SpecialType.System_String || tpClean.TypeKind == TypeKind.Enum;
                var isSourceSimple = spClean.IsValueType || spClean.SpecialType == SpecialType.System_String || spClean.TypeKind == TypeKind.Enum;

                if (!isTargetSimple && !isSourceSimple && tpClean is INamedTypeSymbol tpNamed && spClean is INamedTypeSymbol spNamed)
                {
                    DiscoverMappingsRecursive(spNamed, tpNamed, result, processedPairs, compilation);
                }
            }

            break;
        }
    }

    private static MappingPairData CreatePairData(INamedTypeSymbol source, INamedTypeSymbol target, Compilation compilation)
    {
        var data = new MappingPairData
        {
            Source = source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Target = target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        };

        var customExpressions = FindCustomExpressionsForPair(compilation, source, target);

        var sourceProps = source.GetAllProperties();
        foreach (var sp in sourceProps) data.SourcePropertyNames.Add(sp.Name);

        var targetProps = target.GetAllProperties().Where(p => p.SetMethod != null);
        foreach (var tp in targetProps)
        {
            var cleanType = tp.Type.WithNullableAnnotation(NullableAnnotation.None);
            var isSimple = cleanType.IsValueType || cleanType.SpecialType == SpecialType.System_String || cleanType.TypeKind == TypeKind.Enum;
            var isEnum = cleanType is INamedTypeSymbol tn && (tn.AllInterfaces.Any(i => i.Name == "IEnumerable") || tn.Name == "IEnumerable") && tn.IsGenericType;

            var propInfo = new PropertyMappingInfo
            {
                Name = tp.Name,
                TypeDisplay = tp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsSimple = isSimple,
                IsRequired = tp.IsRequired,
                IsEnumerable = isEnum
            };

            if (isEnum && cleanType is INamedTypeSymbol tgtNamed)
            {
                propInfo.ItemTypeDisplay = tgtNamed.TypeArguments.FirstOrDefault()?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            if (customExpressions.TryGetValue(tp.Name, out var expressionText))
            {
                propInfo.HasCustomExpression = true;
                propInfo.CustomExpressionText = expressionText;
            }

            data.TargetProperties.Add(propInfo);
        }
        return data;
    }

    private static Dictionary<string, string> FindCustomExpressionsForPair(Compilation compilation, INamedTypeSymbol source, INamedTypeSymbol target)
    {
        var customRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            var semanticModel = compilation.GetSemanticModel(tree);

            // Находим все вызовы ForMember в текущем синтаксическом дереве
            var forMemberInvocations = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: MapperProfileConstants.MemberMethodName });

            foreach (var invocation in forMemberInvocations)
            {
                // Спускаемся по Fluent-цепочке вниз от текущего ForMember до самого корня (CreateMap)
                var currentExpr = invocation.Expression;
                InvocationExpressionSyntax? rootCreateMapInvocation = null;

                while (currentExpr is MemberAccessExpressionSyntax maeChain)
                {
                    if (maeChain.Expression is InvocationExpressionSyntax parentInv)
                    {
                        rootCreateMapInvocation = parentInv;
                        currentExpr = parentInv.Expression;
                    }
                    else
                    {
                        break;
                    }
                }

                if (rootCreateMapInvocation == null) continue;

                // Теперь проверяем, является ли корень вызовом CreateMap
                var rootMethodName = string.Empty;
                GenericNameSyntax? genericName = null;

                switch (rootCreateMapInvocation.Expression)
                {
                    case GenericNameSyntax gName:
                        rootMethodName = gName.Identifier.ValueText;
                        genericName = gName;
                        break;
                    case MemberAccessExpressionSyntax { Name: GenericNameSyntax maeGName }:
                        rootMethodName = maeGName.Identifier.ValueText;
                        genericName = maeGName;
                        break;
                }

                if (rootMethodName != MapperProfileConstants.CreateMapMethodName || genericName == null || genericName.TypeArgumentList.Arguments.Count != 2) continue;

                // Извлекаем семантические типы из CreateMap<TSource, TTarget>
                var sourceSyntax = genericName.TypeArgumentList.Arguments[0];
                var targetSyntax = genericName.TypeArgumentList.Arguments[1];

                var evaluatedSource = semanticModel.GetTypeInfo(sourceSyntax).Type as INamedTypeSymbol;
                var evaluatedTarget = semanticModel.GetTypeInfo(targetSyntax).Type as INamedTypeSymbol;

                if (evaluatedSource == null || evaluatedTarget == null) continue;

                // =========================================================================
                // ДОБАВЛЕННЫЙ БЛОК: Безопасная проверка, что этот CreateMap взят именно из EntMap
                // =========================================================================
                if (semanticModel.GetSymbolInfo(rootCreateMapInvocation).Symbol is IMethodSymbol createMapSymbol)
                {
                    var containingTypeDisplay = createMapSymbol.ContainingType?.ToDisplayString();
                    // Если метод CreateMap принадлежит чужому классу (не из EntMap), то пропускаем
                    if (containingTypeDisplay == null || !containingTypeDisplay.StartsWith($"{GlobalMapperConstants.MainNamespace}.")) continue;
                }
                // =========================================================================

                // Проверяем, совпадает ли пара в CreateMap с той, которую мы сейчас генерируем
                if (!SymbolEqualityComparer.Default.Equals(evaluatedSource, source) ||
                    !SymbolEqualityComparer.Default.Equals(evaluatedTarget, target)) continue;

                switch (invocation.ArgumentList.Arguments.Count)
                {
                    // Если совпало, вытаскиваем кастомное выражение из ForMember
                    case 2:
                    {
                        var destArg = invocation.ArgumentList.Arguments[0].Expression;
                        var mapFromArg = invocation.ArgumentList.Arguments[1].Expression;
                        string? targetPropertyName = null;

                        if (destArg is LambdaExpressionSyntax { Body: MemberAccessExpressionSyntax propAccess })
                        {
                            targetPropertyName = propAccess.Name.Identifier.ValueText;
                        }

                        if (!string.IsNullOrEmpty(targetPropertyName))
                        {
                            string finalizedExpression;

                            if (mapFromArg is LambdaExpressionSyntax sourceLambda)
                            {
                                var bodyStr = sourceLambda.Body.ToString();

                                var paramName = sourceLambda switch
                                {
                                    SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
                                    ParenthesizedLambdaExpressionSyntax paren => paren.ParameterList.Parameters
                                        .FirstOrDefault()
                                        ?.Identifier.ValueText,
                                    _ => null
                                };

                                finalizedExpression = !string.IsNullOrEmpty(paramName) ? bodyStr.Replace(paramName + ".", "data.") : bodyStr;
                            }
                            else
                            {
                                finalizedExpression = mapFromArg.ToString();
                            }

                            customRules[targetPropertyName!] = finalizedExpression;
                        }

                        break;
                    }
                }
            }
        }
        return customRules;
    }
}