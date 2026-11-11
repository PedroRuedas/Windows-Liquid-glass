using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using LiquidGlass.Models;

namespace LiquidGlass.Resources;

/// <summary>
/// Os textos da interface. O português do Brasil fica em Strings.resx (idioma neutro) e o inglês em
/// Strings.en.resx. Para um texto novo: uma entrada em cada .resx e uma linha aqui com o mesmo nome.
/// Os testes conferem que os dois idiomas têm as mesmas chaves e os mesmos marcadores {0}.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Resources = new("LiquidGlass.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>O idioma do Windows ao abrir a dock, antes de qualquer escolha nas preferências.</summary>
    private static readonly CultureInfo WindowsCulture = CultureInfo.CurrentUICulture;

    /// <summary>Idioma dos textos e das datas. Definido uma vez ao abrir a dock, por <see cref="Apply"/>.</summary>
    public static CultureInfo Culture { get; set; } = Resolve(AppLanguage.Automatic, WindowsCulture);

    /// <summary>Usa o idioma escolhido nas preferências (ou o do Windows, em Automático).</summary>
    public static void Apply(AppLanguage language) => Culture = Resolve(language, WindowsCulture);

    /// <summary>Se a escolha resulta no idioma em uso; senão, ela só vale depois de reiniciar a dock.</summary>
    public static bool IsInUse(AppLanguage language) => Resolve(language, WindowsCulture).Equals(Culture);

    /// <summary>
    /// Português com o Windows em português; inglês em qualquer outro idioma, já que é o que mais
    /// gente entende. Mantém a variante regional do Windows (pt-PT, en-GB) para as datas.
    /// </summary>
    public static CultureInfo Resolve(AppLanguage language, CultureInfo windows)
    {
        var windowsLanguage = windows.TwoLetterISOLanguageName;
        var wanted = language switch
        {
            AppLanguage.Portuguese => "pt",
            AppLanguage.English => "en",
            _ => windowsLanguage == "pt" ? "pt" : "en",
        };

        if (windowsLanguage == wanted && !windows.IsNeutralCulture)
        {
            return windows;
        }

        return CultureInfo.GetCultureInfo(wanted == "pt" ? "pt-BR" : "en-US");
    }

    /// <summary>Texto por chave, para listas lidas de uma tabela. Sem a chave, devolve a própria chave.</summary>
    public static string Lookup(string key) => Resources.GetString(key, Culture) ?? key;

    /// <summary>Iniciar</summary>
    public static string StartTitle => Get();

    /// <summary>Áreas de trabalho</summary>
    public static string Desktops => Get();

    /// <summary>Lixeira</summary>
    public static string RecycleBin => Get();

    /// <summary>Apps em segundo plano</summary>
    public static string BackgroundApps => Get();

    /// <summary>Explorador de Arquivos</summary>
    public static string FileExplorer => Get();

    /// <summary>Configurações</summary>
    public static string SettingsApp => Get();

    /// <summary>Nova janela</summary>
    public static string NewWindow => Get();

    /// <summary>Abrir</summary>
    public static string Open => Get();

    /// <summary>Remover da Dock</summary>
    public static string RemoveFromDock => Get();

    /// <summary>Manter na Dock</summary>
    public static string KeepInDock => Get();

    /// <summary>Fechar todas as janelas</summary>
    public static string CloseAllWindows => Get();

    /// <summary>Trocar ícone…</summary>
    public static string ChangeIcon => Get();

    /// <summary>Restaurar ícone original</summary>
    public static string ResetIcon => Get();

    /// <summary>Escolha uma imagem ou um programa</summary>
    public static string ChooseIconTitle => Get();

    /// <summary>Imagens e programas|{0}|Todos os arquivos (*.*)|*.*</summary>
    public static string ChooseIconFilter(string extensions) => Format([extensions]);

    /// <summary>Fechar</summary>
    public static string Close => Get();

    /// <summary>Personalizar a Dock…</summary>
    public static string CustomizeDockMenu => Get();

    /// <summary>Sair do Liquid Glass</summary>
    public static string QuitDock => Get();

    /// <summary>Tarefas</summary>
    public static string JumpListTasks => Get();

    /// <summary>Não foi possível abrir “{0}”. O arquivo ou o programa pode ter sido movido ou apagado.</summary>
    public static string JumpListOpenFailed(string title) => Format([title]);

    /// <summary>Procurando atualizações…</summary>
    public static string MenuCheckingForUpdates => Get();

    /// <summary>Baixando atualização… {0:0%}</summary>
    public static string MenuDownloadingUpdate(double progress) => Format([progress]);

    /// <summary>Instalando atualização…</summary>
    public static string MenuInstallingUpdate => Get();

    /// <summary>Atualizar para a versão {0}</summary>
    public static string MenuUpdateTo(Version version) => Format([version]);

    /// <summary>Procurar atualizações…</summary>
    public static string MenuCheckForUpdates => Get();

    /// <summary>A versão {0} está disponível.</summary>
    public static string UpdateAvailable(Version version) => Format([version]);

    /// <summary>Você já tem a versão mais recente ({0}).</summary>
    public static string UpdateUpToDate(Version version) => Format([version]);

    /// <summary>Não foi possível procurar atualizações. Verifique a conexão e tente de novo.</summary>
    public static string UpdateCheckFailed => Get();

    /// <summary>Baixando a versão {0}…</summary>
    public static string UpdateDownloading(Version version) => Format([version]);

    /// <summary>Instalando… a dock volta sozinha em alguns segundos.</summary>
    public static string UpdateInstalling => Get();

    /// <summary>O arquivo baixado não confere com o publicado. A atualização foi cancelada.</summary>
    public static string UpdateHashMismatch => Get();

    /// <summary>Não foi possível baixar ou iniciar a atualização. Tente de novo mais tarde.</summary>
    public static string UpdateDownloadFailed => Get();

    /// <summary>Bateria: {0}%</summary>
    public static string BatteryPercent(int percent) => Format([percent]);

    /// <summary>economia de energia</summary>
    public static string BatterySaver => Get();

    /// <summary>carregando</summary>
    public static string BatteryCharging => Get();

    /// <summary>carregada</summary>
    public static string BatteryCharged => Get();

    /// <summary>na tomada</summary>
    public static string BatteryPluggedIn => Get();

    /// <summary>{0} h {1:00} min restantes</summary>
    public static string BatteryHoursLeft(int hours, int minutes) => Format([hours, minutes]);

    /// <summary>{0} min restantes</summary>
    public static string BatteryMinutesLeft(int minutes) => Format([minutes]);

    /// <summary>Wi-Fi: {0} · sinal {1}%</summary>
    public static string NetworkWifi(string name, int signal) => Format([name, signal]);

    /// <summary>conectado</summary>
    public static string NetworkConnected => Get();

    /// <summary>Rede cabeada: conectada</summary>
    public static string NetworkWired => Get();

    /// <summary>Wi-Fi: desconectado</summary>
    public static string NetworkWifiDisconnected => Get();

    /// <summary>Sem conexão de rede</summary>
    public static string NetworkNone => Get();

    /// <summary>Segundo plano</summary>
    public static string TrayTitle => Get();

    /// <summary>Nenhum app na bandeja agora.</summary>
    public static string TrayEmpty => Get();

    /// <summary>Clique para abrir · botão direito para mais opções</summary>
    public static string TrayHint => Get();

    /// <summary>1 app</summary>
    public static string TrayOneApp => Get();

    /// <summary>{0} apps</summary>
    public static string TrayAppCount(int count) => Format([count]);

    /// <summary>Fechar janela</summary>
    public static string CloseWindow => Get();

    /// <summary>Anterior</summary>
    public static string MediaPrevious => Get();

    /// <summary>Tocar ou pausar</summary>
    public static string MediaPlayPause => Get();

    /// <summary>Próxima</summary>
    public static string MediaNext => Get();

    /// <summary>Mais 1 janela no menu do botão direito</summary>
    public static string MoreWindowsOne => Get();

    /// <summary>Mais {0} janelas no menu do botão direito</summary>
    public static string MoreWindows(int count) => Format([count]);

    /// <summary>Nova área de trabalho</summary>
    public static string NewDesktop => Get();

    /// <summary>Solte a janela em outra área de trabalho</summary>
    public static string CubeDropOnOther => Get();

    /// <summary>Soltar aqui cria uma área de trabalho com a janela</summary>
    public static string CubeDropCreates => Get();

    /// <summary>Mover para a área de trabalho {0}</summary>
    public static string CubeMoveTo(int number) => Format([number]);

    /// <summary>Nova área de trabalho · clique ou Enter para criar</summary>
    public static string CubeNewHint => Get();

    /// <summary>Área de trabalho {0}</summary>
    public static string CubeDesktop(int number) => Format([number]);

    /// <summary>Pesquisar aplicativos, configurações e documentos</summary>
    public static string StartSearchPlaceholder => Get();

    /// <summary>Fixado</summary>
    public static string StartPinned => Get();

    /// <summary>Pesquise um app e escolha "Fixar no Iniciar" no botão direito para ele aparecer aqui.</summary>
    public static string StartPinnedEmpty => Get();

    /// <summary>Recente</summary>
    public static string StartRecent => Get();

    /// <summary>Os arquivos que você abrir aparecem aqui.</summary>
    public static string StartRecentEmpty => Get();

    /// <summary>Energia</summary>
    public static string StartPower => Get();

    /// <summary>Procurando…</summary>
    public static string StartSearching => Get();

    /// <summary>Nada encontrado para "{0}".</summary>
    public static string StartNoResults(string query) => Format([query]);

    /// <summary>Não foi possível abrir "{0}".</summary>
    public static string StartOpenFailed(string name) => Format([name]);

    /// <summary>Mover para o início</summary>
    public static string MoveToTop => Get();

    /// <summary>Desafixar do Iniciar</summary>
    public static string UnpinFromStart => Get();

    /// <summary>Fixar no Iniciar</summary>
    public static string PinToStart => Get();

    /// <summary>Abrir local do arquivo</summary>
    public static string OpenFileLocation => Get();

    /// <summary>Todos os apps</summary>
    public static string StartAllApps => Get();

    /// <summary>Voltar</summary>
    public static string StartBack => Get();

    /// <summary>Bloquear</summary>
    public static string PowerLock => Get();

    /// <summary>Sair</summary>
    public static string PowerSignOut => Get();

    /// <summary>Suspender</summary>
    public static string PowerSleep => Get();

    /// <summary>Hibernar</summary>
    public static string PowerHibernate => Get();

    /// <summary>Desligar</summary>
    public static string PowerShutDown => Get();

    /// <summary>Reiniciar</summary>
    public static string PowerRestart => Get();

    /// <summary>O Windows não deixou "{0}" agora.</summary>
    public static string PowerFailed(string action) => Format([action]);

    /// <summary>Aplicativos</summary>
    public static string GroupApps => Get();

    /// <summary>Configurações</summary>
    public static string GroupSettings => Get();

    /// <summary>Documentos</summary>
    public static string GroupDocuments => Get();

    /// <summary>Agora</summary>
    public static string TimeNow => Get();

    /// <summary>Há {0} min</summary>
    public static string TimeMinutesAgo(int minutes) => Format([minutes]);

    /// <summary>Há {0} h</summary>
    public static string TimeHoursAgo(int hours) => Format([hours]);

    /// <summary>Ontem</summary>
    public static string TimeYesterday => Get();

    /// <summary>d 'de' MMM</summary>
    public static string DateFormatThisYear => Get();

    /// <summary>d 'de' MMM 'de' yyyy</summary>
    public static string DateFormatOtherYear => Get();

    /// <summary>Cristalino</summary>
    public static string PresetCrystal => Get();

    /// <summary>Fosco</summary>
    public static string PresetFrosted => Get();

    /// <summary>Escuro</summary>
    public static string PresetDark => Get();

    /// <summary>Prisma</summary>
    public static string PresetPrism => Get();

    /// <summary>Personalizar a Dock</summary>
    public static string SettingsTitle => Get();

    /// <summary>Versão {0}</summary>
    public static string SettingsVersion(string version) => Format([version]);

    /// <summary>Restaurar padrões</summary>
    public static string RestoreDefaults => Get();

    /// <summary>Aparência</summary>
    public static string PageAppearance => Get();

    /// <summary>Vidro</summary>
    public static string PageGlass => Get();

    /// <summary>Animações</summary>
    public static string PageAnimations => Get();

    /// <summary>Comportamento</summary>
    public static string PageBehavior => Get();

    /// <summary>Apps fixados</summary>
    public static string PagePinnedApps => Get();

    /// <summary>Posição na tela</summary>
    public static string Position => Get();

    /// <summary>Borda onde a dock fica ancorada.</summary>
    public static string PositionDescription => Get();

    /// <summary>Embaixo</summary>
    public static string EdgeBottom => Get();

    /// <summary>À esquerda</summary>
    public static string EdgeLeft => Get();

    /// <summary>À direita</summary>
    public static string EdgeRight => Get();

    /// <summary>Dock em todos os monitores</summary>
    public static string AllMonitors => Get();

    /// <summary>Uma dock em cada tela, todas com os mesmos apps. Desligado, só no monitor principal.</summary>
    public static string AllMonitorsDescription => Get();

    /// <summary>Tamanho dos ícones</summary>
    public static string IconSize => Get();

    /// <summary>Espaçamento entre ícones</summary>
    public static string IconSpacing => Get();

    /// <summary>Comprimento da dock</summary>
    public static string DockLength => Get();

    /// <summary>Estende o vidro ao longo da tela. Em 0% ele só envolve os ícones.</summary>
    public static string DockLengthDescription => Get();

    /// <summary>Distância da borda</summary>
    public static string EdgeMargin => Get();

    /// <summary>Espaço entre a dock e a borda da tela.</summary>
    public static string EdgeMarginDescription => Get();

    /// <summary>Arredondamento</summary>
    public static string Roundness => Get();

    /// <summary>De cantos retos até o formato de pílula.</summary>
    public static string RoundnessDescription => Get();

    /// <summary>Elementos</summary>
    public static string SectionElements => Get();

    /// <summary>Nome do app ao passar o mouse</summary>
    public static string ShowLabels => Get();

    /// <summary>Indicador de app aberto</summary>
    public static string RunningIndicator => Get();

    /// <summary>Ponto abaixo dos apps em execução.</summary>
    public static string RunningIndicatorDescription => Get();

    /// <summary>Miniaturas das janelas</summary>
    public static string WindowPreviews => Get();

    /// <summary>Ao parar o mouse sobre um app aberto, mostra as janelas dele para alternar entre elas.</summary>
    public static string WindowPreviewsDescription => Get();

    /// <summary>Botão Iniciar</summary>
    public static string StartButton => Get();

    /// <summary>Botão das áreas de trabalho</summary>
    public static string DesktopsButton => Get();

    /// <summary>Abre o cubo das áreas de trabalho, como no Fedora.</summary>
    public static string DesktopsButtonDescription => Get();

    /// <summary>Botão que mostra os ícones da bandeja do sistema.</summary>
    public static string BackgroundAppsDescription => Get();

    /// <summary>Rede</summary>
    public static string Network => Get();

    /// <summary>Wi-Fi com a força do sinal, ou rede cabeada. O clique abre a lista de redes.</summary>
    public static string NetworkDescription => Get();

    /// <summary>Bateria</summary>
    public static string Battery => Get();

    /// <summary>Carga e tempo restante. Só aparece em computadores com bateria.</summary>
    public static string BatteryDescription => Get();

    /// <summary>Lixeira no final da dock</summary>
    public static string RecycleBinAtEnd => Get();

    /// <summary>Estilos prontos</summary>
    public static string Presets => Get();

    /// <summary>Óptica</summary>
    public static string SectionOptics => Get();

    /// <summary>Refração</summary>
    public static string Refraction => Get();

    /// <summary>Quanto a borda curva distorce o que está atrás.</summary>
    public static string RefractionDescription => Get();

    /// <summary>Desfoque</summary>
    public static string Blur => Get();

    /// <summary>Deixa o fundo mais fosco.</summary>
    public static string BlurDescription => Get();

    /// <summary>Refletir as janelas atrás da dock</summary>
    public static string LiveBackdrop => Get();

    /// <summary>Quando uma janela passa por baixo, o vidro mostra e distorce o que ela exibe, ao vivo. Usa um pouco da placa de vídeo só enquanto há uma janela por baixo.</summary>
    public static string LiveBackdropDescription => Get();

    /// <summary>Este Windows não permite capturar janelas sem a borda amarela; o vidro mostra o papel de parede.</summary>
    public static string LiveBackdropUnavailable => Get();

    /// <summary>Dispersão de cores</summary>
    public static string ChromaticAberration => Get();

    /// <summary>Franja de arco-íris nas bordas, como em um prisma.</summary>
    public static string ChromaticAberrationDescription => Get();

    /// <summary>Brilho da borda</summary>
    public static string Highlight => Get();

    /// <summary>Reflexo de luz no contorno do vidro.</summary>
    public static string HighlightDescription => Get();

    /// <summary>Saturação</summary>
    public static string Saturation => Get();

    /// <summary>Intensidade das cores vistas através do vidro.</summary>
    public static string SaturationDescription => Get();

    /// <summary>Sombra</summary>
    public static string Shadow => Get();

    /// <summary>Cor</summary>
    public static string SectionColor => Get();

    /// <summary>Tom do vidro</summary>
    public static string Tint => Get();

    /// <summary>Intensidade do tom</summary>
    public static string TintStrength => Get();

    /// <summary>Cor do vidro</summary>
    public static string TintMode => Get();

    /// <summary>O tom do vidro pode acompanhar o modo claro ou escuro e a cor de destaque do Windows.</summary>
    public static string TintModeDescription => Get();

    /// <summary>Personalizada</summary>
    public static string TintModeCustom => Get();

    /// <summary>Tema do Windows (claro ou escuro)</summary>
    public static string TintModeWindows => Get();

    /// <summary>Cor de destaque do Windows</summary>
    public static string TintModeAccent => Get();

    /// <summary>Ampliação</summary>
    public static string Magnification => Get();

    /// <summary>Os ícones crescem quando o mouse passa por cima.</summary>
    public static string MagnificationDescription => Get();

    /// <summary>Tamanho máximo</summary>
    public static string MaximumSize => Get();

    /// <summary>Alcance</summary>
    public static string MagnificationRange => Get();

    /// <summary>Quantos ícones vizinhos também crescem.</summary>
    public static string MagnificationRangeDescription => Get();

    /// <summary>Movimento</summary>
    public static string SectionMotion => Get();

    /// <summary>Quicar ao abrir um app</summary>
    public static string BounceOnLaunch => Get();

    /// <summary>Quicar quando um app pede atenção</summary>
    public static string BounceOnAttention => Get();

    /// <summary>Por exemplo, uma mensagem nova em segundo plano.</summary>
    public static string BounceOnAttentionDescription => Get();

    /// <summary>Fechar e minimizar no estilo do GNOME</summary>
    public static string AnimateWindows => Get();

    /// <summary>A janela encolhe ao fechar e voa até o ícone dela na dock ao minimizar.</summary>
    public static string AnimateWindowsDescription => Get();

    /// <summary>Cubo ao trocar de área de trabalho</summary>
    public static string AnimateDesktopSwitch => Get();

    /// <summary>Ctrl+Win+setas giram o cubo das áreas de trabalho, como no Fedora.</summary>
    public static string AnimateDesktopSwitchDescription => Get();

    /// <summary>Velocidade das animações</summary>
    public static string AnimationSpeed => Get();

    /// <summary>Ocultar a dock automaticamente</summary>
    public static string AutoHide => Get();

    /// <summary>A dock aparece quando o mouse encosta na borda.</summary>
    public static string AutoHideDescription => Get();

    /// <summary>Reservar espaço para a dock</summary>
    public static string ReserveSpace => Get();

    /// <summary>Janelas maximizadas param antes da dock. Desligado, elas usam a tela inteira e passam por baixo do vidro.</summary>
    public static string ReserveSpaceDescription => Get();

    /// <summary>Barra de tarefas do Windows</summary>
    public static string WindowsTaskbar => Get();

    /// <summary>Ela volta ao normal sempre que a dock é fechada.</summary>
    public static string WindowsTaskbarDescription => Get();

    /// <summary>Oculta</summary>
    public static string TaskbarHidden => Get();

    /// <summary>Ocultar automaticamente</summary>
    public static string TaskbarAutoHide => Get();

    /// <summary>Visível</summary>
    public static string TaskbarVisible => Get();

    /// <summary>Iniciar com o Windows</summary>
    public static string LaunchAtStartup => Get();

    /// <summary>Menu Iniciar de vidro</summary>
    public static string GlassStartMenu => Get();

    /// <summary>O botão Iniciar e a tecla Windows abrem o menu da dock, com pesquisa, fixados e recentes. Desligado, abrem o menu do Windows.</summary>
    public static string GlassStartMenuDescription => Get();

    /// <summary>Win+Tab abre o cubo das áreas de trabalho</summary>
    public static string WinTabOpensCube => Get();

    /// <summary>Usa o cubo da dock no lugar da Visão de Tarefas do Windows.</summary>
    public static string WinTabOpensCubeDescription => Get();

    /// <summary>Selos e progresso dos apps</summary>
    public static string ShowBadges => Get();

    /// <summary>Mensagens não lidas (Discord, Telegram, Teams…) e barras de download no ícone. Funciona com a barra do Windows oculta.</summary>
    public static string ShowBadgesDescription => Get();

    /// <summary>Win+1…9 abrem os apps da dock</summary>
    public static string WinNumberKeys => Get();

    /// <summary>Como na barra do Windows: Win+1 é o primeiro app, Win+2 o segundo. Com Shift, abre uma janela nova.</summary>
    public static string WinNumberKeysDescription => Get();

    /// <summary>Idioma</summary>
    public static string Language => Get();

    /// <summary>Em Automático, a dock segue o idioma do Windows.</summary>
    public static string LanguageDescription => Get();

    /// <summary>Automático</summary>
    public static string LanguageAutomatic => Get();

    /// <summary>Português (Brasil)</summary>
    public static string LanguagePortuguese => Get();

    /// <summary>English</summary>
    public static string LanguageEnglish => Get();

    /// <summary>O novo idioma vale depois de reiniciar a dock.</summary>
    public static string LanguageRestartHint => Get();

    /// <summary>Reiniciar a dock</summary>
    public static string RestartDock => Get();

    /// <summary>Informações de diagnóstico</summary>
    public static string Diagnostics => Get();

    /// <summary>Versão, Windows, monitores e erros recentes, para colar numa issue ao relatar um problema.</summary>
    public static string DiagnosticsDescription => Get();

    /// <summary>Copiado. Cole na issue com Ctrl+V.</summary>
    public static string DiagnosticsCopied => Get();

    /// <summary>Copiar</summary>
    public static string Copy => Get();

    /// <summary>Relatar um problema</summary>
    public static string ReportProblem => Get();

    /// <summary>Atualizações</summary>
    public static string SectionUpdates => Get();

    /// <summary>Procurar atualizações automaticamente</summary>
    public static string CheckForUpdatesAutomatically => Get();

    /// <summary>Ao abrir a dock e a cada 6 horas. A versão nova só é instalada quando você pedir.</summary>
    public static string CheckForUpdatesAutomaticallyDescription => Get();

    /// <summary>Atualizar agora</summary>
    public static string UpdateNow => Get();

    /// <summary>Procurar atualizações</summary>
    public static string CheckForUpdates => Get();

    /// <summary>Versão atual: {0}.</summary>
    public static string CurrentVersion(string version) => Format([version]);

    /// <summary>Dica: arraste atalhos, programas ou pastas para a dock. Uma pasta fixada abre como pilha, com os arquivos mais recentes.</summary>
    public static string PinnedAppsTip => Get();

    /// <summary>Adicionar app…</summary>
    public static string AddApp => Get();

    /// <summary>Importar da barra de tarefas</summary>
    public static string ImportFromTaskbar => Get();

    /// <summary>Adicionar pasta…</summary>
    public static string AddFolder => Get();

    /// <summary>Escolha uma pasta para a pilha</summary>
    public static string ChooseFolderTitle => Get();

    /// <summary>Abrir no Explorador</summary>
    public static string StackOpenInExplorer => Get();

    /// <summary>Esta pasta está vazia.</summary>
    public static string StackEmpty => Get();

    /// <summary>Mover para cima</summary>
    public static string MoveUp => Get();

    /// <summary>Mover para baixo</summary>
    public static string MoveDown => Get();

    /// <summary>Remover da dock</summary>
    public static string RemoveFromDockTooltip => Get();

    /// <summary>Escolha um aplicativo ou atalho</summary>
    public static string ChooseAppTitle => Get();

    /// <summary>Aplicativos e atalhos (*.exe;*.lnk)|*.exe;*.lnk|Todos os arquivos (*.*)|*.*</summary>
    public static string ChooseAppFilter => Get();

    private static string Get([CallerMemberName] string key = "") => Lookup(key);

    private static string Format(object?[] args, [CallerMemberName] string key = "") => string.Format(Culture, Lookup(key), args);
}
