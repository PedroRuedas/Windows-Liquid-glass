# Histórico de versões

Todas as mudanças relevantes do Liquid Glass Dock ficam registradas aqui. O formato segue o
[Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e as versões seguem o
[Versionamento Semântico](https://semver.org/lang/pt-BR/). As notas completas, com os instaladores
e o SHA-256 de cada um, estão nas [releases](https://github.com/PedroRuedas/Windows-Liquid-glass/releases).

## [Não lançado]

## [0.12.0] - 2026-11-10

### Adicionado
- **Fundo ao vivo:** quando uma janela passa por baixo da dock, o vidro mostra e refrata o que
  ela exibe, em tempo real, como a dock do macOS. A dock lê só as janelas que estão atrás do vidro
  (Windows.Graphics.Capture, sem a borda amarela), então continua aparecendo em prints e
  gravações. Sem janela por baixo, volta ao papel de parede, que não custa nada. Opção em
  Personalizar → Vidro.
- **Reservar espaço para a dock:** desligado, janelas maximizadas ocupam a tela inteira e passam
  por baixo do vidro. Opção em Personalizar → Comportamento.
- O diagnóstico mostra quantas janelas o fundo ao vivo está capturando e quanto tempo leva cada
  quadro.

### Alterado
- O demo do README virou um vídeo em WebP, com cores de verdade (o GIF tinha só 256), sobre um
  fundo colorido onde a refração aparece.

*English: the glass now refracts windows that pass under the dock in real time, like the macOS
Dock, without capturing the dock itself (it still shows up in screenshots). A new option lets
maximized windows use the whole screen and go under the dock. The README demo is now a
full-color WebP.*

## [0.11.0] - 2026-11-10

### Adicionado
- **Selos e progresso nos ícones:** as mensagens não lidas que os apps mostram na barra do
  Windows (Discord, Telegram, Teams…) e as barras de download aparecem no ícone da dock, com a
  barra do Windows oculta. Opção em Personalizar → Comportamento.
- **Controles de música nas miniaturas:** capa, título, artista e os botões de voltar,
  tocar/pausar e avançar do que o app está tocando.
- **Pilhas:** uma pasta fixada abre em grade com os arquivos mais recentes; o botão direito num
  item mostra-o no Explorador. "Adicionar pasta…" em Apps fixados.
- **Win+1…9** abrem os apps da dock pela posição; com Shift, uma janela nova.
- **Trocar ícone…** no botão direito de um app, e "Restaurar ícone original".
- **Dock em todos os monitores**, cada uma com o papel de parede do seu monitor no vidro. O
  menu Iniciar, as miniaturas e a lâmpada mágica usam a dock do monitor certo.
- **Cor do vidro** pode seguir o modo claro/escuro ou a cor de destaque do Windows.
- O diagnóstico ganhou a seção **Desempenho**: memória, processador, threads e objetos do sistema.

### Alterado
- O projeto passa a usar o SDK do Windows (para os controles de música); o instalador cresceu
  cerca de 4 MB.

*English: badges and download progress on dock icons, media controls in thumbnails, folder
stacks, Win+1…9 shortcuts, custom icons, a dock on every monitor, glass that follows the Windows
theme or accent color, and performance numbers in the diagnostic report.*

## [0.10.0] - 2026-11-10

### Adicionado
- **Todos os apps** no menu Iniciar de vidro: a lista de apps instalados em ordem alfabética,
  agrupada pela inicial, com "Fixar no Iniciar" no botão direito.
- Arrastar um app aberto para o meio dos fixados o fixa ali mesmo.
- A ordem dos apps abertos não fixados, quando arrastada, fica salva e volta depois de reiniciar a
  dock (um app fechado volta para o mesmo lugar ao abrir de novo).
- **Informações de diagnóstico** em Personalizar → Comportamento: "Copiar" põe na área de
  transferência a versão, o Windows, os monitores, as preferências principais e os últimos erros,
  com a pasta do usuário escondida. "Relatar um problema" copia e abre o formulário de bug.

### Removido das limitações conhecidas
- O menu Iniciar de vidro agora tem "Todos os apps".
- Apps abertos podem ser fixados arrastando, e a ordem deles é guardada ao reiniciar a dock.

*English: an "All apps" list in the glass Start menu, drag an open app among the pinned ones to pin
it, the order of open apps is remembered across restarts, and a "Diagnostic info" button copies a
report to paste into bug reports.*

## [0.9.0] - 2026-11-10

### Adicionado
- **Interface em inglês.** A dock segue o idioma do Windows: português no Windows em português,
  inglês nos demais. Para escolher, use Personalizar → Comportamento → Idioma; o botão "Reiniciar
  a dock" aplica o idioma novo na hora. *English: the dock now speaks English and follows your
  Windows display language.*
- O instalador também fala inglês e português, pelo idioma do Windows, e mostra a licença.
- Links para o projeto, o suporte e as atualizações em Configurações → Aplicativos.
- O `LiquidGlass.exe` aparece como "Liquid Glass Dock" no Gerenciador de Tarefas.

### Interno
- Código aberto sob a licença GPL-3.0, com guia de contribuição, código de conduta, política de
  segurança e modelos de issue e de pull request.
- README em inglês e um GIF da dock no topo dos dois.
- Integração contínua (build, testes e formatação a cada push) e Dependabot.
- Releases automáticas: esta é a primeira versão gerada pelo GitHub a partir da tag, com o
  instalador compilado a partir do código público.

## [0.8.0] - 2026-11-09

### Adicionado
- Botão de energia no menu Iniciar de vidro: bloquear, sair, suspender, hibernar (quando ligada
  no Windows), desligar e reiniciar. O rodapé do menu mostra o nome da conta.
- Desligar e reiniciar funcionam como no Windows: nada é fechado à força, e a inicialização rápida
  é respeitada.

## [0.7.1] - 2026-11-09

### Alterado
- O tamanho dos ícones agora vai de 16 a 96 px (antes, o mínimo era 32 px). Com ícones pequenos, o
  vidro, o indicador de app aberto e o separador encolhem junto, e a dock fica com cerca de metade
  da altura.

## [0.7.0] - 2026-11-04

### Adicionado
- Menu Iniciar de vidro, aberto pelo botão Iniciar da dock ou pela tecla Windows.
- Pesquisa de apps instalados, páginas das Configurações e arquivos pelo índice do Windows.
- Apps fixados trazidos do menu Iniciar do Windows na primeira vez, e arquivos recentes.
- Opção para voltar ao menu Iniciar do Windows.

## [0.6.0] - 2026-11-04

### Adicionado
- Reordenar os ícones da dock arrastando. A ordem dos fixados fica salva.

## [0.5.1] - 2026-10-27

### Corrigido
- Com muitas janelas do mesmo app, as miniaturas quebram em linhas e diminuem até caber na tela.
- A lista de atalhos é lida em segundo plano, e um item lento não trava mais a dock.
- Aviso quando um item da lista de atalhos não abre (arquivo movido ou apagado).
- Miniaturas mais estáveis: o título acompanha a janela e as miniaturas não saltam de lugar.
- Esc fecha as miniaturas; "Nova janela" do próprio app não aparece duplicada.
- Atalhos temporários não ficam mais esquecidos na pasta temporária.

### Interno
- Projeto de testes (`dotnet test`) com listas de atalhos, layout das miniaturas e menus.

## [0.5.0] - 2026-10-27

### Adicionado
- Listas de atalhos no botão direito ("Nova guia", "Nova janela anônima", pastas recentes), com o
  app aberto ou só fixado.
- Miniaturas vivas das janelas ao parar o mouse sobre um app aberto.

### Corrigido
- "Nova janela" de apps da Microsoft Store abre pelo app certo.

## [0.4.0] - 2026-10-27

### Adicionado
- Arrastar janelas entre áreas de trabalho no cubo.
- Atualização pela própria dock, com conferência do SHA-256 do instalador.

## [0.3.0] - 2026-10-27

### Adicionado
- Ícone de rede (Wi-Fi com força do sinal ou rede cabeada).
- Criar áreas de trabalho direto no cubo pela face "+".

### Alterado
- Lâmpada mágica desenhada pela placa de vídeo, mais lisa e na taxa de atualização do monitor.

## [0.2.0] - 2026-10-27

### Adicionado
- Áreas de trabalho num cubo 3D (Win+Tab) e troca animada com Ctrl+Win+← / →.
- Minimizar em "lâmpada mágica" e fechar encolhendo a janela.
- Bateria na dock.

### Alterado
- A dock parada devolve ao Windows a memória que não está usando.

## [0.1.0] - 2026-10-26

### Adicionado
- Primeira versão: vidro líquido com refração real do papel de parede, ampliação dos ícones,
  apps fixados e em execução, bandeja do sistema, personalização ao vivo, ocultar
  automaticamente e modo de eficiência quando a dock está parada.

[Não lançado]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.12.0...HEAD
[0.12.0]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.11.0...v0.12.0
[0.11.0]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.10.0...v0.11.0
[0.10.0]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.9.0...v0.10.0
[0.9.0]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.8.0...v0.9.0
[0.8.0]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.7.1...v0.8.0
[0.7.1]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.7.0...v0.7.1
[0.7.0]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.5.1...v0.6.0
[0.5.1]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/PedroRuedas/Windows-Liquid-glass/releases/tag/v0.1.0
