# Política de segurança

## Versões com suporte

Só a versão mais recente recebe correções de segurança. A dock procura atualizações sozinha, e a
última versão sempre está nas [releases](https://github.com/PedroRuedas/Windows-Liquid-glass/releases/latest).

| Versão | Suporte |
| --- | --- |
| A mais recente (0.10.x) | ✅ |
| Anteriores | ❌ |

## Como relatar uma vulnerabilidade

**Não abra uma issue pública para falhas de segurança.** Use o relato privado do GitHub:
na aba **Security** do repositório, clique em
[**Report a vulnerability**](https://github.com/PedroRuedas/Windows-Liquid-glass/security/advisories/new).

Inclua, se possível:

- a versão da dock e a do Windows;
- o que um atacante consegue fazer e em que condições;
- os passos para reproduzir ou uma prova de conceito.

Você receberá uma resposta assim que o relato for analisado. Se a falha for confirmada, a correção
sai numa nova versão, e você recebe o crédito nas notas da release, se quiser.

## O que interessa mais

A dock roda com os privilégios do seu usuário (sem administrador) e fica aberta o tempo todo.
Os pontos mais sensíveis são:

- **Atualizações:** a dock baixa o instalador das releases do GitHub por HTTPS e confere o SHA-256
  publicado pelo GitHub antes de executá-lo. Qualquer forma de fazer a dock instalar um arquivo
  diferente é uma vulnerabilidade.
- **Listas de atalhos e menu Iniciar:** itens lidos de arquivos do Windows e abertos pela dock.
- **Arquivos de configuração** em `%AppData%\LiquidGlass`.
