using System.Collections.Immutable;
using EntMap.Builders;
using EntMap.SourceGeneration.Models;

namespace EntMap.Extensions;

public static class ImmutableArrayExtensions
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="pairs"></param>
    /// <param name="namespaceName"></param>
    /// <returns></returns>
    public static string GenerateGlobalMapperCode(this ImmutableArray<MappingPairData> pairs, string namespaceName)
    {
        var builder = new GlobalMapperSourceBuilder(pairs, namespaceName);
        return builder.Build();
    }
}