# Histórico de versões

Todas as mudanças relevantes do Liquid Glass Dock ficam registradas aqui. O formato segue o
[Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e as versões seguem o
[Versionamento Semântico](https://semver.org/lang/pt-BR/). As notas completas, com os instaladores
e o SHA-256 de cada um, estão nas [releases](https://github.com/PedroRuedas/Windows-Liquid-glass/releases).

## [Não lançado]

### Adicionado
- Interface em inglês. A dock segue o idioma do Windows (português no Windows em português,
  inglês nos demais), e a opção Personalizar → Comportamento → Idioma permite escolher, com um
  botão para reiniciar a dock no idioma novo. O instalador também fala os dois idiomas.
- Releases automáticas: uma tag `vX.Y.Z` gera o instalador nas máquinas do GitHub, calcula o
  SHA-256 e publica a release com as notas do CHANGELOG (veja "Publicar uma versão" no
  CONTRIBUTING). Com o segredo `WINGET_TOKEN`, a versão também é enviada ao winget.
- Licença GPL-3.0, guia de contribuição, código de conduta, política de segurança, modelos de
  issue e de pull request e integração contínua (build, testes e formatação a cada push).
- README em inglês (`README.en.md`).
- O instalador mostra a licença e traz links para o projeto, o suporte e as atualizações em
  Configurações → Aplicativos.

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

[Não lançado]: https://github.com/PedroRuedas/Windows-Liquid-glass/compare/v0.8.0...HEAD
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
