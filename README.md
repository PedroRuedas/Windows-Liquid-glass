<div align="center">

# Liquid Glass Dock

**A dock no estilo *Liquid Glass* do macOS, com vidro que refrata de verdade, no lugar da barra de
tarefas do Windows 11.**

[![Última versão](https://img.shields.io/github/v/release/PedroRuedas/Windows-Liquid-glass?label=vers%C3%A3o&color=2ea44f)](https://github.com/PedroRuedas/Windows-Liquid-glass/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/PedroRuedas/Windows-Liquid-glass/total?label=downloads&color=blue)](https://github.com/PedroRuedas/Windows-Liquid-glass/releases)
[![CI](https://github.com/PedroRuedas/Windows-Liquid-glass/actions/workflows/ci.yml/badge.svg)](https://github.com/PedroRuedas/Windows-Liquid-glass/actions/workflows/ci.yml)
[![Licença GPL-3.0](https://img.shields.io/github/license/PedroRuedas/Windows-Liquid-glass?label=licen%C3%A7a)](LICENSE)
![Windows 11](https://img.shields.io/badge/Windows-11-0078D4?logo=windows11&logoColor=white)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)

### [⬇️ Baixar para Windows 11](https://github.com/PedroRuedas/Windows-Liquid-glass/releases/latest)

[Recursos](#recursos) · [Instalação](#instalação) · [Perguntas frequentes](#perguntas-frequentes) ·
[Contribuir](#contribuir) · [Histórico de versões](CHANGELOG.md) · [🇺🇸 English](README.en.md)

![A dock de vidro líquido sobre um fundo colorido: os ícones crescem quando o mouse passa, a Calculadora minimiza em lâmpada mágica até o ícone dela e uma janela desliza por baixo da dock, refratada ao vivo pelo vidro](docs/dock-demo.webp)

</div>

Ela substitui a barra de tarefas com vidro que refrata de verdade o papel de parede, ampliação dos
ícones ao passar o mouse, animações de abertura, menu Iniciar próprio, áreas de trabalho num cubo
3D e uma tela de personalização completa. Gratuita, de código aberto e sem precisar de
administrador para instalar.

**Destaques**
- Vidro líquido de verdade: o papel de parede é curvado nas bordas por um shader, como uma lente.
- Fundo ao vivo: janelas que passam por baixo da dock aparecem refratadas no vidro, em tempo real.
- Barra de tarefas completa: apps fixados e abertos, miniaturas das janelas, listas de atalhos
  ("Nova guia", "Nova janela anônima"), bandeja do sistema, rede e bateria.
- Áreas de trabalho num cubo 3D (Win+Tab), com troca animada entre áreas e janelas arrastáveis de
  uma área para outra.
- Minimizar em "lâmpada mágica" para dentro do ícone e fechar encolhendo a janela.
- Selos de mensagens não lidas e barras de download nos ícones, controles de música nas
  miniaturas, pastas em "pilha" e atalhos Win+1…9.
- Dock em todos os monitores, com o vidro acompanhando o tema claro ou escuro do Windows.
- Atualiza sozinha pelas releases do GitHub e gasta quase nada parada.

## Recursos

**Vidro líquido**
- Refração real: um pixel shader HLSL curva o papel de parede nas bordas, como uma lente.
- **Fundo ao vivo:** quando uma janela entra embaixo da dock (arrastada até lá, ou maximizada com
  "Reservar espaço para a dock" desligado), o vidro mostra e distorce o conteúdo dela em tempo
  real, como a dock do macOS. Sem janela por baixo, volta ao papel de parede.
- Dispersão de cores (aberração cromática), brilho especular no contorno, tom, saturação e sombra.
- Bordas suavizadas em qualquer arredondamento, de cantos retos até o formato de pílula.
- Estilos prontos: *Cristalino*, *Fosco*, *Escuro* e *Prisma*.

![Refração](docs/refraction.png)

![Fundo ao vivo: uma janela passa por baixo da dock e as cores dela aparecem refratadas no vidro](docs/live-backdrop.png)

**Animações dos ícones**
- Ampliação estilo macOS: o ícone sob o cursor cresce e os vizinhos acompanham em curva suave,
  com o ponto sob o mouse sempre fixo.
- Quique enquanto um app abre e quique de atenção quando um app em segundo plano pede atenção.
- Ícones entram e saem da dock com animação; o ícone escurece ao ser pressionado.
- Velocidade das animações ajustável.

**Comportamento de barra de tarefas**
- Apps fixados e apps em execução, agrupados por aplicativo (inclusive apps da Microsoft Store).
- Clique abre o app, traz a janela para frente, minimiza ou alterna entre janelas.
- Clique do meio abre uma nova janela; o botão direito mostra as janelas abertas, as ações e a
  lista de atalhos do app, como na barra do Windows: "Nova guia" e "Nova janela anônima" nos
  navegadores, "Nova janela" e pastas recentes no VS Code, e assim por diante.
- **Miniaturas das janelas:** pare o mouse sobre um app aberto para ver uma miniatura viva de cada
  janela dele (dois VS Codes, por exemplo). Clique numa para trazê-la; o X ou o botão do meio a fecha.
  Com muitas janelas, as miniaturas quebram em linhas e diminuem até todas caberem na tela.
- Arraste atalhos ou programas para a dock para fixá-los.
- **Reordenar ícones:** segure um app e arraste-o para outra posição. Os fixados trocam de lugar
  entre si e os abertos, entre si; as duas ordens ficam salvas, inclusive depois de reiniciar a
  dock. Arraste um app aberto para o meio dos fixados para fixá-lo ali.
- **Menu Iniciar de vidro:** o botão Iniciar e a tecla Windows abrem um menu da dock com pesquisa
  (apps, páginas das Configurações e arquivos do índice do Windows), os apps fixados (trazidos do
  menu Iniciar do Windows na primeira vez), os arquivos recentes e a lista de **todos os apps**,
  em ordem alfabética. No rodapé ficam o nome da conta e
  o botão de energia: bloquear, sair, suspender, hibernar (quando ligada no Windows), desligar e
  reiniciar. Dá para trocar de volta para o menu do Windows nas preferências.
- **Selos e progresso:** o número de mensagens não lidas que Discord, Telegram, Teams e outros
  apps mostram na barra do Windows aparece no canto do ícone, e downloads e cópias ganham uma
  barra de progresso embaixo dele.
- **Controles de música:** com o Spotify, o navegador ou outro player tocando, as miniaturas do
  app mostram a capa, o título e os botões de voltar, tocar/pausar e avançar.
- **Pilhas:** uma pasta fixada (arraste-a para a dock ou use "Adicionar pasta…") abre em grade
  com os arquivos mais recentes, como as Stacks do macOS.
- **Atalhos de teclado:** Win+1…9 abrem o app nessa posição da dock (ou trazem, minimizam e
  alternam as janelas dele, como o clique); com Shift, abrem uma janela nova.
- **Ícone personalizado:** botão direito → "Trocar ícone…" aceita uma imagem (PNG, ICO, JPG) ou
  o ícone de outro programa.
- **Vários monitores:** com "Dock em todos os monitores", cada tela tem a sua dock, com o papel de
  parede daquele monitor no vidro.
- Botão Iniciar e Lixeira.
- **Apps em segundo plano:** um botão abre um painel com os ícones da bandeja do sistema. Clique
  esquerdo e direito são repassados ao app, como na bandeja do Windows.
- Janelas maximizadas param antes da dock, e a dock some em jogos e vídeos em tela cheia.
- Opção de ocultar a dock automaticamente.
- Funciona com a dock embaixo, à esquerda ou à direita da tela.
- **Rede:** ícone de Wi-Fi com a força do sinal e o nome da rede (ou de rede cabeada); o clique
  abre a lista de redes do Windows.
- **Bateria:** carga, tempo restante e estado (carregando, economia de energia) num ícone discreto
  perto da Lixeira. Só aparece em computadores com bateria.

| Lista de atalhos no botão direito | Miniaturas das janelas |
| --- | --- |
| ![Menu do botão direito do Opera GX com Nova guia, Nova janela e Nova janela privada](docs/jump-list.png) | ![Miniatura viva de uma janela do VS Code acima do ícone](docs/window-previews.png) |

**Áreas de trabalho em cubo, como no Fedora (extensão Desktop Cube do GNOME)**
- Win+Tab (ou o botão da dock) afasta a câmera e mostra as áreas de trabalho como faces de um cubo,
  em círculo, com as janelas flutuando à frente do papel de parede desfocado.

  ![Cubo das áreas de trabalho: ele gira, uma área fecha pelo × e a janela dela passa para a vizinha, uma janela é arrastada para outra área e Ctrl+Win+setas trocam de área](docs/workspaces-demo.webp)

- Arraste com o mouse para girar o cubo (na vertical ele inclina); ao soltar, ele encaixa na área
  mais próxima, levando em conta o embalo. Setas e roda do mouse giram uma face; clique numa face
  para entrar nela. Sempre há uma face "+": pare nela e clique (ou Enter) para criar uma área; o
  cubo continua aberto e ganha outra face "+", para criar quantas quiser.
- Arraste uma janela para tirá-la da área dela e solte-a em outra face para movê-la para lá (na
  face "+", ela ganha uma área nova). Para chegar a uma área de trás, segure a janela perto da borda
  esquerda ou direita da tela, ou gire com a roda do mouse ou as setas; Esc ou soltar fora do cubo
  devolve a janela. Janelas fixadas em todas as áreas não se arrastam.
- **Fechar uma área de trabalho:** o "×" de vidro no canto da face da frente, a tecla Delete ou o
  clique do meio numa face. As janelas dela vão para a área vizinha, como na Visão de Tarefas; a
  última área não fecha.
- **Trocar de área de trabalho:** Ctrl+Win+← e → giram o cubo até a área vizinha, recuando um pouco
  no meio do giro, sem precisar abrir a visão geral.
- Minimizar é a "lâmpada mágica": a janela escorre em funil para dentro do ícone dela na dock (e se
  desenrola de lá ao restaurar pela dock). Fechar encolhe a janela enquanto ela some.

**Idiomas**
- Interface em português do Brasil e em inglês. A dock segue o idioma do Windows, e dá para
  escolher em Personalizar → Comportamento → Idioma.

**Personalização**

![Personalização](docs/settings.png)

Todas as mudanças aparecem na dock na hora:

| Seção | Opções |
| --- | --- |
| Aparência | posição (embaixo, esquerda, direita), comprimento da dock, tamanho dos ícones, espaçamento, distância da borda, arredondamento, rótulos, indicadores, miniaturas das janelas, botão Iniciar, botão das áreas de trabalho, apps em segundo plano, rede, bateria, Lixeira |
| Vidro | estilos prontos, refração, desfoque, dispersão de cores, brilho da borda, saturação, cor do vidro (personalizada, tema do Windows ou cor de destaque), sombra, cor e intensidade do tom |
| Animações | ampliação, tamanho máximo, alcance, quicar ao abrir, quicar por atenção, fechar e minimizar no estilo do GNOME, deslizar entre áreas, velocidade |
| Comportamento | ocultar automaticamente, barra de tarefas do Windows (oculta, auto-ocultar ou visível), iniciar com o Windows, Win+Tab abre as áreas de trabalho, atualizações (procurar, instalar, procurar automaticamente) |
| Apps fixados | adicionar, importar da barra de tarefas, reordenar e remover |

## Atalhos

| Onde | Ação | Efeito |
| --- | --- | --- |
| Ícone de um app | clique | abre o app, traz a janela, minimiza ou alterna entre as janelas dele |
| Ícone de um app | clique do meio | abre uma nova janela |
| Ícone de um app | segurar e arrastar | muda o app de posição na dock |
| Ícone de um app | botão direito | janelas abertas, lista de atalhos do app, fixar, fechar |
| Ícone de um app aberto | parar o mouse | miniaturas das janelas; clique numa para trazê-la |
| Miniatura | X ou clique do meio | fecha a janela |
| Miniaturas abertas | Esc | fecha as miniaturas |
| Qualquer parte da dock | botão direito | atualizar, personalizar, sair |
| Teclado | Win | abre e fecha o menu Iniciar de vidro |
| Menu Iniciar | digitar, ↑ ↓, Enter, Esc | pesquisa, escolhe, abre e fecha |
| App no menu Iniciar | botão direito | fixar ou desafixar do Iniciar, mover para o início |
| Botão de energia do menu Iniciar | clique | bloquear, sair, suspender, hibernar, desligar, reiniciar |
| Teclado | Win+Tab | abre e fecha o cubo das áreas de trabalho |
| Teclado | Ctrl+Win+← / → | vai para a área de trabalho vizinha |
| Cubo | arrastar, setas, roda do mouse | gira o cubo |
| Cubo | clique numa face ou Enter | entra na área (na face "+", cria uma) |
| Cubo | arrastar uma janela até outra face | move a janela para aquela área |
| Cubo | "×" da face, Delete ou clique do meio | fecha a área (as janelas vão para a vizinha) |
| Cubo | Esc | fecha o cubo (ou devolve a janela arrastada) |

## Instalação

**Requisitos:** Windows 11, 64 bits. A troca animada entre áreas de trabalho (Ctrl+Win+← / →)
usa uma interface do Windows 11 24H2 em diante; nas versões anteriores, esse atalho fica com o
próprio Windows.

1. Baixe `LiquidGlassDock-Setup-<versão>.exe` na
   [última release](https://github.com/PedroRuedas/Windows-Liquid-glass/releases/latest).
2. Execute. O instalador ainda não tem assinatura digital, então o Windows deve mostrar
   "O Windows protegeu o computador": clique em **Mais informações → Executar assim mesmo**.
   Se quiser conferir o arquivo, compare `Get-FileHash .\LiquidGlassDock-Setup-<versão>.exe`
   no PowerShell com o SHA-256 publicado na release.

O instalador:

- instala só para o seu usuário, sem pedir permissão de administrador
  (em `%LocalAppData%\Programs\Liquid Glass Dock`);
- não exige o .NET instalado: o app já vem com tudo o que precisa;
- marca "Iniciar com o Windows" por padrão, para a dock ficar sempre em segundo plano;
- cria no menu Iniciar os atalhos "Liquid Glass Dock" e "Encerrar Liquid Glass Dock";
- encerra a dock que estiver aberta antes de atualizar.

### Atualizar

A dock procura versões novas nas [releases do GitHub](https://github.com/PedroRuedas/Windows-Liquid-glass/releases)
ao abrir e a cada 6 horas. Quando há uma, o botão direito em qualquer parte da dock mostra
"Atualizar para a versão X": a dock baixa o instalador, confere o SHA-256 publicado pelo GitHub,
fecha (devolvendo a barra de tarefas), instala e abre de novo sozinha. O mesmo fica em
Personalizar → Comportamento → Atualizações, onde também dá para desligar a procura automática.
Nada é instalado sem você pedir.

Para desinstalar, use Configurações → Aplicativos. O desinstalador encerra a dock, devolve a
barra de tarefas do Windows e remove a inicialização automática. As preferências em
`%AppData%\LiquidGlass` são mantidas.

### Gerar o instalador

Requisitos: Windows 11 e .NET 10 SDK. O compilador do Inno Setup vem pelo NuGet, sem instalar
nada no sistema.

```powershell
dotnet publish src/LiquidGlass -p:PublishProfile=Installer
```

O instalador sai em `artifacts/`. O Windows SDK (`fxc.exe`) é opcional: o shader compilado já
está no repositório e só é recompilado quando o `.fx` muda.

## Como usar

Para rodar a partir do código:

```powershell
dotnet run --project src/LiquidGlass
```

| Comando | Efeito |
| --- | --- |
| `LiquidGlass.exe` | Inicia a dock (ou abre a personalização se ela já estiver rodando) |
| `LiquidGlass.exe --settings` | Inicia e abre a personalização |
| `LiquidGlass.exe --quit` | Encerra a dock e devolve a barra de tarefas (mesmo depois de um encerramento forçado) |

Também dá para abrir a personalização ou sair pelo botão direito em qualquer parte da dock.

### Consumo em segundo plano

Medido na versão do instalador (Windows 11, monitor de 179 Hz, GeForce RTX 5060):

| | Parada |
| --- | --- |
| CPU | 0,00 s de processador em 20 s |
| Memória privada | ~115 MB |
| Threads | 18 |

Quando ninguém usa a dock por 3 segundos, ela entra no modo de eficiência do Windows (EcoQoS e
prioridade abaixo do normal) e volta ao normal assim que o cursor chega nela. O fundo do vidro é
guardado em meia resolução, já que é desfocado, e a versão publicada é pré-compilada
(ReadyToRun) para não compilar código ao abrir.

O código do app ocupa só ~10 MB de memória. O resto é o motor gráfico e o driver de vídeo, que
qualquer app desenhado pela GPU carrega. Por isso trocar de linguagem economizaria pouco diante
do custo de reescrever tudo.

### A barra de tarefas do Windows sempre volta

Ao esconder a barra nativa, o estado original é salvo em
`%AppData%\LiquidGlass\taskbar-state.json`. Ele é restaurado ao sair, em caso de erro e, se o
processo for encerrado à força, na próxima vez que o app abrir. As preferências ficam em
`%AppData%\LiquidGlass\settings.json`.

## Como funciona

O blur do DWM cobre o retângulo inteiro de uma janela transparente, o que não permite cantos
arredondados nem refração. Por isso a dock desenha o próprio vidro:

1. `WallpaperService` captura o papel de parede exatamente como o Windows o desenha
   (`PaintDesktop`) e aplica um desfoque por caixas em segundo plano.
2. `LiquidGlassSurface` recorta o trecho do papel de parede que está atrás dela na tela.
3. `LiquidGlassEffect` (`Effects/Shaders/LiquidGlass.fx`) calcula a forma por *signed distance
   field* e desloca a amostragem perto da borda, como uma lente, além de dispersão de cores,
   brilho, tom e sombra.

Quando nenhuma janela está atrás do vidro, é só isso: nada é capturado continuamente. Quando uma
janela entra na área da dock, o `LiveBackdrop` pede ao Windows a imagem dessa janela
(Windows.Graphics.Capture, sem a borda amarela), copia da placa de vídeo só o trecho que fica
atrás do vidro, mistura com o papel de parede e desfoca, em meia resolução e numa thread própria.
Como a dock lê as janelas uma a uma, e não a tela, ela nunca captura a si mesma e continua
aparecendo normalmente em prints e gravações. Janelas que não mudam não geram quadros novos; com
uma janela se mexendo por baixo, o custo medido foi de ~1,4 ms por quadro e menos de 1% de CPU. O
Direct3D só é criado quando há janela por baixo e é liberado 15 s depois que ela sai.

### Bandeja do sistema

Os ícones da bandeja vêm da [ManagedShell](https://github.com/cairoshell/ManagedShell)
(Apache-2.0), a biblioteca usada pelo RetroBar e pelo Cairo Desktop. Ela registra uma janela de
bandeja própria para receber os ícones dos apps e repassa tudo ao Explorer, então a bandeja nativa
continua completa quando a dock fecha.

## Estrutura

```
src/LiquidGlass/
├── Controls/      MagnifyingPanel (ampliação), DockItemView (animações do ícone),
│                  LiquidGlassSurface, SettingCard
├── Effects/       LiquidGlassEffect + shader HLSL
├── Graphics/      Captura de tela e desfoque por caixas
├── Interop/       Chamadas Win32 (user32, shell32, dwmapi, gdi32, kernel32)
├── Models/        Configurações, apps fixados, presets
├── Services/      Janelas abertas, ícones, papel de parede, bandeja, barra nativa,
│                  app bar, listas de atalhos, áreas de trabalho virtuais, animação
│                  das janelas, atualizações, modo de eficiência, persistência
├── Themes/        Estilos da dock, menus, ícones e da janela de personalização
├── ViewModels/    Dock e personalização (MVVM com CommunityToolkit.Mvvm)
└── Views/         Janela da dock, painel da bandeja, miniaturas das janelas, cubo das
                   áreas de trabalho, lâmpada mágica, personalização, menus, ocultar
                   automaticamente
```

Os testes ficam em `tests/LiquidGlass.Tests` (listas de atalhos, layout das miniaturas e menus).
Os de listas de atalhos gravam uma lista de teste pelo próprio Windows e a apagam no fim:

```powershell
dotnet test
```

O código segue o `.editorconfig` do repositório. O build trata avisos como erros, e o
`dotnet format --verify-no-changes` precisa passar limpo. A [integração contínua](.github/workflows/ci.yml)
roda as três verificações em cada push e pull request.

## Perguntas frequentes

**A barra de tarefas do Windows sumiu e a dock não está aberta. E agora?**
Abra "Encerrar Liquid Glass Dock" no menu Iniciar do Windows (ou rode `LiquidGlass.exe --quit`).
A barra volta mesmo depois de um encerramento forçado. Veja
[A barra de tarefas do Windows sempre volta](#a-barra-de-tarefas-do-windows-sempre-volta).

**Funciona no Windows 10?**
Não. O instalador exige o Windows 11.

**Preciso instalar o .NET?**
Não. O instalador já traz tudo o que a dock precisa.

**A dock pesa no computador?**
Parada, ela não gasta processador e entra no modo de eficiência do Windows. Veja os números em
[Consumo em segundo plano](#consumo-em-segundo-plano).

**Por que o Windows avisa que o instalador é desconhecido?**
Porque ele ainda não tem assinatura digital, que é paga. O código e o processo de build são
públicos, e cada release publica o SHA-256 do instalador, que a própria dock confere ao se
atualizar.

**Posso voltar ao menu Iniciar ou à barra de tarefas do Windows?**
Sim. Em Personalizar → Comportamento dá para mostrar a barra do Windows, e a opção "Menu Iniciar
de vidro" devolve o menu do Windows. Ao fechar a dock, tudo volta ao normal.

## Limitações conhecidas

- O menu Iniciar de vidro não mostra a foto do usuário.
- Os selos só aparecem com a barra do Windows oculta, e só de apps que usam a barra de tarefas
  clássica (ITaskbarList): apps da Microsoft Store, como o WhatsApp, usam outro sistema. Um app
  que já estava aberto quando a dock começou pode só mostrar o selo depois de reaberto.
- A ordem dos fixados do menu Iniciar muda pelo botão direito ("Mover para o início"), não
  arrastando.
- Os recentes automáticos do Windows (por exemplo, os do Explorador e do Bloco de Notas) não
  entram no menu do botão direito: só as listas que o próprio app registra.
- Janelas fixadas em todas as áreas de trabalho não podem ser arrastadas no cubo.
- No fundo ao vivo, conteúdo protegido contra cópia (como vídeos de streaming com DRM) aparece
  escuro no vidro, como em qualquer captura de tela. Na primeira vez que uma janela passa por
  baixo da dock, o driver de vídeo é carregado e a memória sobe cerca de 30 MB.

Encontrou outra? [Abra uma issue](https://github.com/PedroRuedas/Windows-Liquid-glass/issues/new/choose).
Em Personalizar → Comportamento, "Relatar um problema" abre o formulário já com as informações de
diagnóstico copiadas.

## Contribuir

Bugs, ideias e pull requests são bem-vindos. O [guia de contribuição](CONTRIBUTING.md) explica
como preparar o ambiente, rodar os testes e enviar mudanças. Falhas de segurança seguem a
[política de segurança](SECURITY.md), e todos seguem o [código de conduta](CODE_OF_CONDUCT.md).

Se a dock te ajudou, deixe uma ⭐ no repositório: isso ajuda outras pessoas a encontrá-la.

## Assinatura de código

Assinatura de código gratuita fornecida pelo [SignPath.io](https://about.signpath.io), certificado
pelo [SignPath Foundation](https://signpath.org) (pedido em andamento; até a aprovação, as releases
saem sem assinatura).

- **Quem pode alterar o código e aprovar releases:** [PedroRuedas](https://github.com/PedroRuedas),
  mantenedor. Contribuições externas entram só por pull request revisado.
- **Como o instalador é gerado:** só pelo [workflow de release](.github/workflows/release.yml), nas
  máquinas do GitHub, a partir de uma tag do código público. Nenhum binário compilado em máquina
  pessoal é assinado.
- **Privacidade:** a dock só se comunica com outros sistemas para procurar versões novas nas
  releases do GitHub (`api.github.com`), ao abrir e a cada 6 horas (dá para desligar em
  Personalizar → Comportamento), e para baixar o instalador quando você pede a atualização. Nenhum
  dado pessoal é enviado.

## Licença

Este projeto é distribuído sob a [GNU General Public License v3.0](LICENSE). Você pode usar,
estudar, modificar e redistribuir o código, desde que as versões distribuídas também fiquem
sob a GPL-3.0 e com o código-fonte disponível.

## Licenças de terceiros

- [ManagedShell](https://github.com/cairoshell/ManagedShell): Apache License 2.0
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet): MIT
- [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (Direct3D 11): MIT
- [Inno Setup](https://jrsoftware.org/isinfo.php): gera o instalador (não faz parte do app)
