namespace LiquidGlass.Services;

/// <summary>
/// Reconhece a tecla Windows apertada e solta sozinha, o gesto que abre o menu Iniciar. Qualquer
/// outra tecla no meio (Win+E, Win+Tab, Ctrl+Win+seta) faz dela parte de um atalho, e então o
/// menu não abre. Sem estado do sistema, para ser testada sem teclado.
/// </summary>
internal sealed class StartKeyDetector
{
    private const uint VkLeftWin = 0x5B;
    private const uint VkRightWin = 0x5C;

    private bool _windowsDown;
    private bool _alone;

    /// <summary>
    /// Registra uma tecla e devolve <c>true</c> quando ela completa o gesto: é a tecla Windows sendo
    /// solta sem nenhuma outra tecla desde que foi apertada. <paramref name="otherModifiersDown"/>
    /// diz se Ctrl, Alt ou Shift já estavam apertados quando a tecla Windows desceu.
    /// </summary>
    public bool OnKey(uint virtualKey, bool isDown, bool otherModifiersDown = false)
    {
        var isWindows = virtualKey is VkLeftWin or VkRightWin;
        if (isWindows && isDown)
        {
            // A repetição automática de quem segura a tecla não recomeça o gesto.
            if (!_windowsDown)
            {
                _windowsDown = true;
                _alone = !otherModifiersDown;
            }

            return false;
        }

        if (isWindows)
        {
            var completed = _windowsDown && _alone;
            _windowsDown = false;
            _alone = false;
            return completed;
        }

        if (isDown && _windowsDown)
        {
            _alone = false;
        }

        return false;
    }
}
