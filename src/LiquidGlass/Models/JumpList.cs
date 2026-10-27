namespace LiquidGlass.Models;

/// <summary>Grupo da lista de atalhos de um app. As tarefas ("Nova janela anônima") não têm nome.</summary>
public sealed record JumpListCategory(string? Name, IReadOnlyList<JumpListEntry> Entries)
{
    public bool IsTasks => Name is null;
}

/// <summary>
/// Item da lista de atalhos. <paramref name="Link"/> é o atalho .lnk serializado, como o app o
/// registrou; <paramref name="ParsingName"/>, um arquivo ou pasta aberto direto pelo shell.
/// </summary>
public sealed record JumpListEntry(string Title, ReadOnlyMemory<byte> Link, string? ParsingName, bool IsSeparator = false);
