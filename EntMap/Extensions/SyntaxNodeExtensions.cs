using EntMap.Constants;
using EntMap.SourceGeneration.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EntMap.Extensions;

public static class SyntaxNodeExtensions
{
    public static InlineMappingCallData? GetInlineMappingCallSafe(this GeneratorSyntaxContext syncContext, SemanticModel model)
    {
        try
        {
            var node = syncContext.Node;
            if (node is not InvocationExpressionSyntax invocation ||
                invocation.ArgumentList.Arguments.Count != 2) return null;

            GenericNameSyntax? genericName = null;
            if (invocation.Expression is GenericNameSyntax gName) genericName = gName;
            else if (invocation.Expression is MemberAccessExpressionSyntax mae &&
                     mae.Name is GenericNameSyntax maeGName) genericName = maeGName;

            if (genericName == null ||
                genericName.Identifier.ValueText != MapperProfileConstants.MapMethodName) return null;
            if (genericName.TypeArgumentList.Arguments.Count != 2) return null;

            // Извлекаем официальные данные интерцептора через новое Roslyn API для .NET 10
            // Метод GetInterceptableLocation возвращает стабильный токен расположения вызова
            var interceptableLocation =
                Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetInterceptableLocation(model, invocation);
            if (interceptableLocation == null) return null;

            var sourceSyntax = genericName.TypeArgumentList.Arguments[0];
            var targetSyntax = genericName.TypeArgumentList.Arguments[1];

            var sourceType = model.GetTypeInfo(sourceSyntax).Type as INamedTypeSymbol;
            var targetType = model.GetTypeInfo(targetSyntax).Type as INamedTypeSymbol;

            if (sourceType == null || targetType == null || sourceType.TypeKind == TypeKind.Error ||
                targetType.TypeKind == TypeKind.Error) return null;

            var configArg = invocation.ArgumentList.Arguments[1].Expression;
            if (configArg is not LambdaExpressionSyntax lambda) return null;

            var uniqueId = Math.Abs(interceptableLocation.Data.GetHashCode());
            var safeUniqueName = $"InlineMap_{sourceType.Name}_To_{targetType.Name}_{uniqueId}";

            var callData = new InlineMappingCallData
            {
                UniqueMethodName = safeUniqueName,
                SourceType = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                TargetType = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                // Записываем версию и данные opaque-токена
                InterceptorVersion = interceptableLocation.Version,
                InterceptorData = interceptableLocation.Data
            };

            // Заполняем базовые свойства
            foreach (var sp in sourceType.GetAllProperties()) callData.SourcePropertyNames.Add(sp.Name);
            foreach (var tp in targetType.GetAllProperties().Where(p => p.SetMethod != null))
            {
                var cleanType = tp.Type.WithNullableAnnotation(NullableAnnotation.None);
                callData.TargetProperties.Add(new PropertyMappingInfo
                {
                    Name = tp.Name,
                    TypeDisplay = tp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsSimple = cleanType.IsValueType || cleanType.SpecialType == SpecialType.System_String ||
                               cleanType.TypeKind == TypeKind.Enum,
                    IsRequired = tp.IsRequired
                });
            }

            // Парсим ForMember
            var forMemberCalls = lambda.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(i => i.Expression is MemberAccessExpressionSyntax mAccess &&
                            mAccess.Name.Identifier.ValueText == "ForMember");

            foreach (var subInv in forMemberCalls)
            {
                if (subInv.ArgumentList.Arguments.Count != 2) continue;
                var destArg = subInv.ArgumentList.Arguments[0].Expression;
                var mapFromArg = subInv.ArgumentList.Arguments[1].Expression;

                string? targetPropName = null;
                if (destArg is LambdaExpressionSyntax destLambda &&
                    destLambda.Body is MemberAccessExpressionSyntax propAccess)
                {
                    targetPropName = propAccess.Name.Identifier.ValueText;
                }

                if (!string.IsNullOrEmpty(targetPropName))
                {
                    string finalizedExpression;
                    if (mapFromArg is LambdaExpressionSyntax sourceLambda)
                    {
                        var bodyStr = sourceLambda.Body.ToString();
                        var paramName = sourceLambda switch
                        {
                            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
                            ParenthesizedLambdaExpressionSyntax paren => paren.ParameterList.Parameters.FirstOrDefault()
                                ?.Identifier.ValueText,
                            _ => null
                        };

                        finalizedExpression = !string.IsNullOrEmpty(paramName)
                            ? bodyStr.Replace(paramName + ".", "data.")
                            : bodyStr;
                    }
                    else
                    {
                        finalizedExpression = mapFromArg.ToString();
                    }

                    callData.CustomExpressions[targetPropName!] = finalizedExpression;
                }
            }

            return callData;
        }
        catch
        {
            return null;
        }
    }
}