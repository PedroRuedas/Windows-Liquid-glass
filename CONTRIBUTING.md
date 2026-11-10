# Como contribuir

Obrigado pelo interesse no Liquid Glass Dock! Relatos de bugs, ideias, prints e pull requests são
todos bem-vindos.

> **English speakers:** issues and pull requests in English are welcome too.

## Relatar um bug

Antes de abrir uma issue, veja se o problema já não foi relatado nas
[issues abertas](https://github.com/PedroRuedas/Windows-Liquid-glass/issues) e se ele continua na
[última versão](https://github.com/PedroRuedas/Windows-Liquid-glass/releases/latest).

Use o modelo **Relatar um bug** e inclua:

- a versão da dock (botão direito na dock → Personalizar) e a do Windows (`winver`);
- os passos para reproduzir, o que aconteceu e o que você esperava;
- um print ou vídeo curto, se o problema for visual;
- quantos monitores você usa e a escala de cada um, se o problema for de posição ou tamanho.

Se a dock travar e a barra de tarefas do Windows sumir, rode `LiquidGlass.exe --quit` (ou o atalho
"Encerrar Liquid Glass Dock" no menu Iniciar) para devolver a barra.

## Sugerir um recurso

Use o modelo **Sugerir um recurso** e conte qual problema a ideia resolve. Recursos que imitam o
comportamento do macOS ou do GNOME são mais fáceis de avaliar com uma referência (vídeo, print ou
link).

## Preparar o ambiente

Requisitos: Windows 11 (64 bits) e o [.NET 10 SDK](https://dotnet.microsoft.com/download).
Qualquer editor serve: Visual Studio 2022, Rider ou VS Code com o C# Dev Kit.

```powershell
git clone https://github.com/PedroRuedas/Windows-Liquid-glass.git
cd Windows-Liquid-glass
dotnet run --project src/LiquidGlass
```

Ao rodar pelo código, a dock esconde a barra de tarefas do Windows como na versão instalada.
Para fechá-la, use o botão direito na dock → Sair, ou `dotnet run --project src/LiquidGlass -- --quit`.

O shader do vidro (`Effects/Shaders/LiquidGlass.fx`) só é recompilado quando o Windows SDK
(`fxc.exe`) está instalado. Sem ele, o build usa o `.ps` compilado que já está no repositório.

## Enviar um pull request

1. Faça um fork e crie um branch a partir do `main` (`git switch -c minha-mudanca`).
2. Faça a mudança. Mantenha o pull request focado num assunto só.
3. Rode as mesmas verificações da integração contínua:

   ```powershell
   dotnet build -c Release
   dotnet test -c Release
   dotnet format --verify-no-changes
   ```

   O build trata avisos como erros, então ele precisa terminar sem nenhum aviso.
4. Abra o pull request preenchendo o modelo. Para mudanças visuais, inclua um print ou vídeo de
   antes e depois.

### Estilo do código

- O `.editorconfig` define o estilo; `dotnet format` corrige a maior parte sozinho.
- O texto da interface, os comentários e as mensagens de commit são em português do Brasil.
- Mensagens de commit curtas, no imperativo e descrevendo o efeito para quem usa
  (por exemplo, "Reordenar os ícones da dock arrastando").
- A dock fica aberta o dia inteiro: evite timers, polling e alocações em caminhos que rodam com a
  dock parada. Veja "Consumo em segundo plano" no README.
- Chamadas Win32 ficam em `Interop/`; a lógica de cada recurso fica num serviço em `Services/`.

### Textos e traduções

Nenhum texto da interface fica escrito direto no código. Cada um tem uma chave em
`src/LiquidGlass/Resources/`:

1. Adicione a chave em `Strings.resx` (português) e em `Strings.en.resx` (inglês).
2. Adicione uma linha com o mesmo nome em `Strings.cs`: uma propriedade
   (`public static string Abrir => Get();`) ou, com valores, um método
   (`public static string Bateria(int percent) => Format([percent]);`).
3. Use `Strings.Chave` no C# ou `{x:Static res:Strings.Chave}` no XAML.

Os testes falham se uma chave faltar num dos idiomas ou se os marcadores `{0}` não baterem.

Quer traduzir a dock para outro idioma? Abra uma issue antes. Além de um `Strings.<idioma>.resx`
(por exemplo, `Strings.es.resx`), o idioma precisa entrar em `AppLanguage`, em `Strings.Resolve` e
na lista de Personalizar → Comportamento → Idioma.

### Testes

Os testes ficam em `tests/LiquidGlass.Tests` e usam xUnit. Lógica que não depende de janela
(layout, cálculos, leitura de arquivos) deve ganhar teste. Os testes não podem mexer no mouse nem
no teclado de verdade.

## Licença das contribuições

O projeto é distribuído sob a [GPL-3.0](LICENSE). Ao enviar um pull request, você concorda que a
sua contribuição seja distribuída sob a mesma licença.

## Código de conduta

Este projeto segue o [Código de Conduta](CODE_OF_CONDUCT.md). Ao participar, você concorda em
respeitá-lo.
