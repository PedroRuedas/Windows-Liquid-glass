namespace LiquidGlass.Services;

/// <summary>
/// Ordem dos apps abertos não fixados, guardada entre uma execução e outra da dock. Cada app é
/// lembrado pela chave do grupo (AppUserModelId ou caminho do executável), e só a ordem arrastada
/// pelo usuário é gravada.
/// </summary>
public static class RunningOrder
{
    /// <summary>Quantos apps a ordem guarda, no máximo; os fechados há mais tempo saem primeiro.</summary>
    public const int Capacity = 64;

    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>A chave que vale guardar: janelas sem app conhecido ("hwnd:…") não voltam iguais.</summary>
    public static string? KeyOf(string? groupKey) =>
        groupKey is null || groupKey.StartsWith("hwnd:", StringComparison.Ordinal) ? null : groupKey;

    /// <summary>
    /// Junta os apps que acabaram de abrir aos que já estão na dock, sem mexer nestes: cada novo
    /// entra antes do primeiro que vem depois dele na ordem salva. Sem posição salva, vai para o fim.
    /// </summary>
    public static List<T> Arrange<T>(IEnumerable<T> existing, IEnumerable<T> added, Func<T, string?> key, IReadOnlyList<string> saved)
    {
        var rank = new Dictionary<string, int>(Comparer);
        for (var index = 0; index < saved.Count; index++)
        {
            rank.TryAdd(saved[index], index);
        }

        int Rank(T item) => key(item) is { } k && rank.TryGetValue(k, out var position) ? position : int.MaxValue;

        var result = existing.ToList();
        foreach (var item in added.OrderBy(Rank))
        {
            var position = Rank(item);
            var at = position == int.MaxValue ? -1 : result.FindIndex(other => Rank(other) > position);
            result.Insert(at < 0 ? result.Count : at, item);
        }

        return result;
    }

    /// <summary>
    /// Grava a ordem atual dos apps abertos nas posições que eles já ocupavam na ordem salva,
    /// sem mexer nos fechados, que voltam para o mesmo lugar quando abrirem de novo.
    /// </summary>
    public static List<string> Remember(IReadOnlyList<string> saved, IReadOnlyList<string> current)
    {
        var open = new HashSet<string>(current, Comparer);
        var result = saved.Distinct(Comparer).ToList();
        var known = new HashSet<string>(result, Comparer);
        result.AddRange(current.Where(known.Add));

        var slots = Enumerable.Range(0, result.Count).Where(index => open.Contains(result[index])).ToList();
        var ordered = current.Distinct(Comparer).ToList();
        for (var index = 0; index < slots.Count; index++)
        {
            result[slots[index]] = ordered[index];
        }

        for (var index = result.Count - 1; result.Count > Capacity && index >= 0; index--)
        {
            if (!open.Contains(result[index]))
            {
                result.RemoveAt(index);
            }
        }

        return result;
    }
}
