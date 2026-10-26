using EntMap.Constants;
using EntMap.SourceGeneration.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections;
using System.Collections.Immutable;
using System.Text;

namespace EntMap
{
    [Generator]
    public class MapperSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Стабильный, независимый шаг для инфраструктуры. Он сработает принудительно для каждого проекта.
            context.RegisterPostInitializationOutput(ctx =>
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

            // 3. Вывод кода — этот шаг сработает ВСЕГДА, даже если пар 0.
            context.RegisterSourceOutput(uniquePairs.Collect().Combine(context.CompilationProvider), static (spc, tuple) =>
            {
                var allPairs = tuple.Left;
                var compilation = tuple.Right;

                string currentNamespace = compilation.AssemblyName ?? "EntMap";

                // Генерируем базовую инфраструктуру прямо в этом же шаге, чтобы гарантировать синхронность namespace!
                var infrastructureCode = MapperSourceGeneratorConstants.GetInfrastructureSource(currentNamespace);
                spc.AddSource("EntMapGlobalInfrastructure.g.cs", SourceText.From(infrastructureCode, Encoding.UTF8));

                // Генерируем регистратор
                var code = GenerateGlobalMapperCode(allPairs, currentNamespace);
                spc.AddSource("EntMapGlobalRegistrar.g.cs", SourceText.From(code, Encoding.UTF8));
            });
        }

        private static bool IsMapperMapCall(SyntaxNode node)
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                var text = invocation.Expression.ToString();
                return text.Contains("CreateMap") || text.Contains("Map") || text.Contains("ForMember");
            }

            if (node is AssignmentExpressionSyntax assignment)
            {
                var text = assignment.Right.ToString();
                return text.Contains("CreateMap") || text.Contains("Map") || text.Contains("ForMember");
            }

            return false;
        }

        private static List<MappingPairData>? GetMappingPairSafe(SyntaxNode node, SemanticModel model)
        {
            try
            {
                // 1. Распаковываем discard (_ = ...) если он есть
                InvocationExpressionSyntax? invocation = node as InvocationExpressionSyntax;
                if (node is AssignmentExpressionSyntax assignment && assignment.Right is InvocationExpressionSyntax rightInv)
                {
                    invocation = rightInv;
                }

                if (invocation == null) return null;

                // 2. Спускаемся к самому первому вызову в цепочке Fluent API
                var currentExpression = invocation.Expression;
                InvocationExpressionSyntax? rootInvocation = invocation;

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

                if (rootInvocation == null) return null;

                // 3. СИНТАКСИЧЕСКИЙ АНАЛИЗ (Без жесткой привязки к Semantic Model)
                // Получаем текстовое имя вызываемого метода (например, "CreateMap" или "Map")
                string methodName = string.Empty;
                GenericNameSyntax? genericName = null;

                if (rootInvocation.Expression is MemberAccessExpressionSyntax rootMae)
                {
                    methodName = rootMae.Name.Identifier.ValueText;
                    genericName = rootMae.Name as GenericNameSyntax;
                }
                else if (rootInvocation.Expression is GenericNameSyntax gName)
                {
                    methodName = gName.Identifier.ValueText;
                    genericName = gName;
                }
                else if (rootInvocation.Expression is IdentifierNameSyntax ins)
                {
                    methodName = ins.Identifier.ValueText;
                }

                INamedTypeSymbol? sourceType = null;
                INamedTypeSymbol? targetType = null;

                // Ситуация А: Это объявление в профиле: CreateMap<LoanDto, LoanModel>()
                if (methodName == "CreateMap" && genericName != null && genericName.TypeArgumentList.Arguments.Count == 2)
                {
                    var sourceSyntax = genericName.TypeArgumentList.Arguments[0];
                    var targetSyntax = genericName.TypeArgumentList.Arguments[1];

                    sourceType = model.GetTypeInfo(sourceSyntax).Type as INamedTypeSymbol;
                    targetType = model.GetTypeInfo(targetSyntax).Type as INamedTypeSymbol;
                }
                // Ситуация Б: Это вызов в коде: EntMapper.Map<List<LoanModel>>(response)
                else if (methodName == "Map" && genericName != null && genericName.TypeArgumentList.Arguments.Count == 1)
                {
                    if (rootInvocation.ArgumentList.Arguments.Count == 1)
                    {
                        var targetSyntax = genericName.TypeArgumentList.Arguments[0];
                        var argExpression = rootInvocation.ArgumentList.Arguments[0].Expression;

                        var targetGeneric = model.GetTypeInfo(targetSyntax).Type as INamedTypeSymbol;
                        var sourceGeneric = model.GetTypeInfo(argExpression).Type as INamedTypeSymbol;

                        if (sourceGeneric != null && targetGeneric != null)
                        {
                            // Счищаем обертки коллекций List<T> -> T
                            sourceType = ExtractItemTypeSymbol(sourceGeneric) ?? sourceGeneric;
                            targetType = ExtractItemTypeSymbol(targetGeneric) ?? targetGeneric;
                        }
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
            string key = $"{source.ToDisplayString() ?? ""}->{target.ToDisplayString() ?? ""}";
            if (processedPairs.Contains(key)) return;
            processedPairs.Add(key);

            var sourceItem = ExtractItemTypeSymbol(source);
            var targetItem = ExtractItemTypeSymbol(target);

            if (sourceItem != null && targetItem != null)
            {
                DiscoverMappingsRecursive(sourceItem, targetItem, result, processedPairs, compilation);
                //result.Add(CreatePairData(source, target, compilation));
                return;
            }

            var currentPair = CreatePairData(source, target, compilation);
            result.Add(currentPair);

            var targetProps = GetAllProperties(target).Where(p => p.SetMethod != null);
            var sourceProps = GetAllProperties(source);

            foreach (var tp in targetProps)
            {
                var sp = sourceProps.FirstOrDefault(p => string.Equals(p.Name, tp.Name, StringComparison.OrdinalIgnoreCase));
                if (sp == null) continue;

                var tpClean = tp.Type.WithNullableAnnotation(NullableAnnotation.None);
                var spClean = sp.Type.WithNullableAnnotation(NullableAnnotation.None);

                bool isTargetSimple = tpClean.IsValueType || tpClean.SpecialType == SpecialType.System_String || tpClean.TypeKind == TypeKind.Enum;
                bool isSourceSimple = spClean.IsValueType || spClean.SpecialType == SpecialType.System_String || spClean.TypeKind == TypeKind.Enum;

                if (!isTargetSimple && !isSourceSimple && tpClean is INamedTypeSymbol tpNamed && spClean is INamedTypeSymbol spNamed)
                {
                    DiscoverMappingsRecursive(spNamed, tpNamed, result, processedPairs, compilation);
                }
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

            var sourceProps = GetAllProperties(source);
            foreach (var sp in sourceProps) data.SourcePropertyNames.Add(sp.Name);

            var targetProps = GetAllProperties(target).Where(p => p.SetMethod != null);
            foreach (var tp in targetProps)
            {
                var cleanType = tp.Type.WithNullableAnnotation(NullableAnnotation.None);
                bool isSimple = cleanType.IsValueType || cleanType.SpecialType == SpecialType.System_String || cleanType.TypeKind == TypeKind.Enum;
                bool isEnum = cleanType is INamedTypeSymbol tn && (tn.AllInterfaces.Any(i => i.Name == "IEnumerable") || tn.Name == "IEnumerable") && tn.IsGenericType;

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

                if (customExpressions.TryGetValue(tp.Name, out string expressionText))
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

                // Находим все вызовы ForMember в текущем синтаксическом детеве
                var forMemberInvocations = root.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Where(i => i.Expression is MemberAccessExpressionSyntax mae && mae.Name.Identifier.ValueText == "ForMember");

                foreach (var invocation in forMemberInvocations)
                {
                    // Спускаемся по Fluent-цепочке вниз от текущего ForMember до самого корня (CreateMap)
                    ExpressionSyntax currentExpr = invocation.Expression;
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
                    string rootMethodName = string.Empty;
                    GenericNameSyntax? genericName = null;

                    if (rootCreateMapInvocation.Expression is GenericNameSyntax gName)
                    {
                        rootMethodName = gName.Identifier.ValueText;
                        genericName = gName;
                    }
                    else if (rootCreateMapInvocation.Expression is MemberAccessExpressionSyntax rootMae && rootMae.Name is GenericNameSyntax maeGName)
                    {
                        rootMethodName = maeGName.Identifier.ValueText;
                        genericName = maeGName;
                    }

                    if (rootMethodName != "CreateMap" || genericName == null || genericName.TypeArgumentList.Arguments.Count != 2) continue;

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
                        if (containingTypeDisplay == null || !containingTypeDisplay.StartsWith("EntMap.")) continue;
                    }
                    // =========================================================================

                    // Проверяем, совпадает ли пара в CreateMap с той, которую мы сейчас генерируем
                    if (!SymbolEqualityComparer.Default.Equals(evaluatedSource, source) ||
                        !SymbolEqualityComparer.Default.Equals(evaluatedTarget, target)) continue;

                    // Если совпало, вытаскиваем кастомное выражение из ForMember
                    if (invocation.ArgumentList.Arguments.Count == 2)
                    {
                        var destArg = invocation.ArgumentList.Arguments[0].Expression;
                        var mapFromArg = invocation.ArgumentList.Arguments[1].Expression;
                        string? targetPropertyName = null;

                        if (destArg is LambdaExpressionSyntax destLambda && destLambda.Body is MemberAccessExpressionSyntax propAccess)
                        {
                            targetPropertyName = propAccess.Name.Identifier.ValueText;
                        }

                        if (!string.IsNullOrEmpty(targetPropertyName))
                        {
                            string finalizedExpression = string.Empty;

                            if (mapFromArg is LambdaExpressionSyntax sourceLambda)
                            {
                                string bodyStr = sourceLambda.Body.ToString();
                                string? paramName = null;

                                if (sourceLambda is SimpleLambdaExpressionSyntax simple)
                                    paramName = simple.Parameter.Identifier.ValueText;
                                else if (sourceLambda is ParenthesizedLambdaExpressionSyntax paren)
                                    paramName = paren.ParameterList.Parameters.FirstOrDefault()?.Identifier.ValueText;

                                if (!string.IsNullOrEmpty(paramName))
                                {
                                    finalizedExpression = bodyStr.Replace(paramName + ".", "data.");
                                }
                                else
                                {
                                    finalizedExpression = bodyStr;
                                }
                            }
                            else
                            {
                                finalizedExpression = mapFromArg.ToString();
                            }

                            customRules[targetPropertyName!] = finalizedExpression;
                        }
                    }
                }
            }
            return customRules;
        }


        private static INamedTypeSymbol? ExtractItemTypeSymbol(INamedTypeSymbol type)
        {
            if (type.IsGenericType && (type.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T || type.Name == "List" || type.Name == "List`1"))
            {
                return type.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
            }
            var enumerableInterface = type.AllInterfaces.FirstOrDefault(i => i.IsGenericType && i.ConstructedFrom.ToDisplayString().StartsWith("System.Collections.Generic.IEnumerable"));
            return enumerableInterface?.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        }
        private static IEnumerable<IPropertySymbol> GetAllProperties(INamedTypeSymbol typeSymbol)
        {
            var currentType = typeSymbol;
            while (currentType != null && currentType.SpecialType != SpecialType.System_Object)
            {
                var props = currentType.GetMembers().OfType<IPropertySymbol>();
                foreach (var prop in props) yield return prop;
                currentType = currentType.BaseType;
            }
        }
        private static string GenerateGlobalMapperCode(ImmutableArray<MappingPairData> pairs, string namespaceName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine($"// Всего обнаружено уникальных пар для кодогенерации: {pairs.Length}");
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    internal static partial class {MapperSourceGeneratorConstants.ClassName}");
            sb.AppendLine("    {");
            sb.AppendLine("        static partial void InitMappers()");
            sb.AppendLine("        {");
            foreach (var pair in pairs)
            {
                // Убрали нижнее подчеркивание из названия, делаем имя идентичным объявлению ниже
                string safeMethodName = $"Map{pair.Source.Replace(".", "").Replace("<", "").Replace(">", "").Replace(" ", "").Replace("[]", "Array").Replace("global::", "").Replace(":", "")}To{pair.Target.Replace(".", "").Replace("<", "").Replace(">", "").Replace(" ", "").Replace("[]", "Array").Replace("global::", "").Replace(":", "")}";
                sb.AppendLine($"            _mappers[(typeof({pair.Source}), typeof({pair.Target}))] = new System.Func<object, {pair.Target}>(src => {safeMethodName}(({pair.Source})src));");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
            foreach (var pair in pairs)
            {
                string safeMethodName = $"Map{pair.Source.Replace(".", "").Replace("<", "").Replace(">", "").Replace(" ", "").Replace("[]", "Array").Replace("global::", "").Replace(":", "")}To{pair.Target.Replace(".", "").Replace("<", "").Replace(">", "").Replace(" ", "").Replace("[]", "Array").Replace("global::", "").Replace(":", "")}";

                sb.AppendLine($"        private static {pair.Target} {safeMethodName}({pair.Source} data)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (data == null) return null!;");
                sb.AppendLine($"            bool isRoot = EntMap.MappingContext.Cache.Count == 0;");
                sb.AppendLine("            try {");
                sb.AppendLine($"                if (EntMap.MappingContext.Cache.TryGetValue(data, out var cached)) return ({pair.Target})cached;");
                sb.AppendLine($"                var dto = ({pair.Target})System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof({pair.Target}));");
                sb.AppendLine($"                EntMap.MappingContext.Cache[data] = dto;");
                sb.AppendLine("                ");

                var assignmentLines = new List<string>();
                foreach (var targetProp in pair.TargetProperties)
                {
                    if (targetProp.HasCustomExpression)
                    {
                        assignmentLines.Add($"                dto.{targetProp.Name} = {targetProp.CustomExpressionText};");
                        continue;
                    }
                    var matchingSourcePropName = pair.SourcePropertyNames.FirstOrDefault(name => string.Equals(name, targetProp.Name, StringComparison.OrdinalIgnoreCase));
                    if (matchingSourcePropName == null)
                    {
                        if (targetProp.IsRequired) assignmentLines.Add($"                dto.{targetProp.Name} = default!;");
                        continue;
                    }
                    if (targetProp.IsSimple)
                    {
                        assignmentLines.Add($"                dto.{targetProp.Name} = ({targetProp.TypeDisplay})data.{matchingSourcePropName};");
                    }
                    else if (targetProp.IsEnumerable)
                    {
                        assignmentLines.Add($"                dto.{targetProp.Name} = data.{matchingSourcePropName} != null ? [.. data.{matchingSourcePropName}.Select(x => {namespaceName}.{MapperSourceGeneratorConstants.ClassName}.Map<{targetProp.ItemTypeDisplay}>(x))] : new();");
                    }
                    else
                    {
                        assignmentLines.Add($"                dto.{targetProp.Name} = data.{matchingSourcePropName} != null ? {namespaceName}.{MapperSourceGeneratorConstants.ClassName}.Map<{targetProp.TypeDisplay}>(data.{matchingSourcePropName}) : default!;");
                    }
                }
                sb.AppendLine(string.Join("\n", assignmentLines));
                sb.AppendLine("                return dto;");
                sb.AppendLine("            } finally {");
                sb.AppendLine($"                if (isRoot) EntMap.MappingContext.Clear();");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

    }
}
