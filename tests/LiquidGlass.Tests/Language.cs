using System.Globalization;
using LiquidGlass.Resources;

namespace LiquidGlass.Tests;

/// <summary>
/// Troca o idioma dos textos durante um teste e devolve o anterior no fim. O idioma é global, então
/// os testes que o usam ficam na coleção <see cref="LanguageCollection"/>, que não roda em paralelo.
/// </summary>
internal sealed class Language : IDisposable
{
    private readonly CultureInfo _previous = Strings.Culture;

    private Language(string culture) => Strings.Culture = CultureInfo.GetCultureInfo(culture);

    public static Language Portuguese() => new("pt-BR");

    public static Language English() => new("en-US");

    public void Dispose() => Strings.Culture = _previous;
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LanguageCollection
{
    public const string Name = "Idioma da interface";
}
