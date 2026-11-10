using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LiquidGlass.Resources;

namespace LiquidGlass.Models;

public enum StartEntryKind
{
    App,
    Setting,
    File,
}

/// <summary>Um item do menu Iniciar: app fixado ou instalado, página das Configurações ou arquivo.</summary>
/// <param name="name">Nome exibido.</param>
/// <param name="target">O que é aberto: <c>shell:AppsFolder\…</c>, atalho, <c>ms-settings:</c> ou caminho.</param>
/// <param name="kind">Tipo do item, que decide o agrupamento e o menu do botão direito.</param>
/// <param name="iconSource">De onde vem o ícone (nome que o shell entende).</param>
/// <param name="subtitle">Segunda linha: pasta do arquivo, "há 2 h", "Aplicativo".</param>
public sealed partial class StartEntry(string name, string target, StartEntryKind kind, string iconSource, string? subtitle = null) : ObservableObject
{
    [ObservableProperty]
    private ImageSource? _icon;

    public string Name { get; } = name;

    public string Target { get; } = target;

    public StartEntryKind Kind { get; } = kind;

    public string IconSource { get; } = iconSource;

    public string? Subtitle { get; } = subtitle;

    /// <summary>Título do grupo nos resultados da pesquisa.</summary>
    public string Group => Kind switch
    {
        StartEntryKind.App => Strings.GroupApps,
        StartEntryKind.Setting => Strings.GroupSettings,
        _ => Strings.GroupDocuments,
    };
}
