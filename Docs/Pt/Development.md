# Desenvolver o Golether

## Ambiente

- .NET SDK 10.0.100+ (`global.json`, rollForward `latestFeature`).
- VS Code com o C# Dev Kit e a extensão do Avalonia, ou Visual Studio / Rider.
- Os componentes nativos (libmpv, GStreamer) são preparados pelo utilitário `golether-components`, ver
  [Native/README.md](../../Native/README.md).
- Windows PowerShell 5.1 ou PowerShell 7 para `Scripts/*/*.ps1`, bash para `Scripts/*/*.sh`. Os scripts estão agrupados
  pelo que compilam: `Scripts/App`, `Scripts/Server`, `Scripts/Assets` (ver `Scripts/README.md`).

## Compilação e testes

```bash
dotnet build Golether.slnx
dotnet test --solution Golether.slnx                                    # todos os testes
dotnet test --project Tests/Golether.Sync.Tests/Golether.Sync.Tests.csproj  # um projeto
```

A variável `GOLETHER_TEST_SCREENSHOTS` (uma pasta) guarda imagens da janela principal dos testes headless para uma
verificação visual sem abrir janelas no ecrã.

A variável `GOLETHER_TEST_GST_LOG` (um caminho de ficheiro) ativa o registo detalhado do GStreamer nos testes de
conferência.

A variável `GOLETHER_TEST_AWG_PACKAGES` (uma pasta com ficheiros `amneziawg-<arch>-<version>.msi` das versões do Amnezia)
ativa a verificação que descompacta o motor de túneis a partir de um pacote real. Sem ela, esse teste é ignorado, por isso
uma execução normal não precisa de transferências. O pacote pode ser transferido do endereço em `ComponentCatalog`; o nome
do ficheiro também vem de lá.

Os testes são xUnit v3 sobre Microsoft.Testing.Platform (ativado em `global.json`). Os pacotes de teste comuns são
referenciados em `Directory.Build.props` para cada projeto `*.Tests`.

| Projeto | O que verifica |
|---|---|
| Golether.Components.Tests | o catálogo, as dicas para Linux/macOS, as importações PE, o instalador (soma, tamanho, cache), a reutilização do GStreamer |
| Golether.Conventions.Tests | as regras do AGENTS.md que os analisadores não conseguem verificar, sobre as fontes lidas com o Roslyn: um tipo por ficheiro, tipos aninhados privados, comentários XML, enumerações em pastas Enums, operadores lógicos no fim da linha, a ordem dos using, o tempo só através de TimeProvider, o esquema só através de migrações, projetos nas suas pastas da solução |
| Golether.Media.Conference.GStreamer.Tests | dois nós WebRTC por loopback com fontes de teste, validação de sinais, estatísticas da ligação e a mudança para o fluxo económico da câmara; ignorados sem GStreamer |
| Golether.Core.Tests | PeerId, PlaybackState, DriftCorrector, endereços, caminhos dos dados (incluindo uma instalação “para todos os utilizadores”) |
| Golether.Localization.Tests | pacotes de idioma: leitura, o catálogo, escolha do idioma pela cultura, o inglês de recurso, as mesmas chaves e marcadores em cada idioma, as 64 palavras distintas do código de verificação e uma bandeira em cada idioma, cada chave pedida existe, não há texto russo no código |
| Golether.Security.Tests | assinaturas, o armazém de chaves, convites (chave e ligação web, validade, o convite partilhado e o limite de pedidos), o endereço selado do servidor, o código de verificação, a admissão |
| Golether.Transports.Tests | tramas, mTLS por loopback, fixação da chave, NAT-PMP e UPnP com um router falso, mensagens e assinatura do servidor de encontro, TCP hole punching por loopback |
| Golether.Sync.Tests | relógios com uma RTT de 1240 ms, mensagens, authority, follower |
| Golether.Media.Streaming.Tests | transferência de partes, a cache, o fluxo para o leitor, hashes sem dados, troca de partes entre participantes e a verificação com hashes |
| Golether.Media.Player.Tests | o leitor simulado, o registo `golether://`, as associações do rato, o volume, as faixas de áudio e legendas, os intervalos carregados de uma libmpv real (ignorados sem ela) |
| Golether.Tunnels.AmneziaWG.Tests | chaves, parâmetros, `.conf`, pacotes, o controlador |
| Golether.Core.Data.Tests | migrações, armazéns do EF Core, o migrador |
| Golether.Session.Tests | o anfitrião e um participante no seu conjunto: admissão, o ficheiro, o início conjunto, a pausa, o convite partilhado, a remoção de um participante e a recusa, um convite expirado |
| Golether.UI.Tests | o fluxo dos túneis em duas bases de dados, modelos de vista, volume (o próprio e o de cada participante), desligar dispositivos, conversa e reações, faixas e legendas, continuar onde se parou, o relatório de diagnóstico, atualizações, escolher e guardar o idioma, textos em inglês, entrar por ligação, a instância única, a procura do servidor de encontro, a remoção de um participante |
| Golether.Web.Server.Tests | o servidor de encontro: definições, limites, salas e encontros, STUN, um encontro completo até um fluxo mTLS pela ligação perfurada, recusas (assinatura, sala alheia, sala vazia, anfitrião em silêncio) |
| Golether.Web.Server.UI.Tests | o monitor do servidor: o formulário das definições e o seu ficheiro, o registo e a sua pesquisa, a gestão do serviço no Windows, no Linux e no macOS, a janela em Avalonia Headless e as bandeiras do seu seletor de idioma |
| Golether.UI.Headless.Tests | a janela principal em Avalonia Headless: o menu do quadro de um participante, os separadores da conversa e dos eventos, o popup do volume, os cliques no vídeo, a linha de tempo fluida com o indicador de carregamento, as reações sobre o vídeo, a lista de idiomas e a mudança de todos os textos em tempo real, o modo de ligação e o bloco “Avançado” do ecrã inicial, as janelas do convite e das definições |

Os testes (exceto `Golether.Localization.Tests`) correm em russo: `Tests/Shared/UseRussianTexts.cs` ativa o pacote russo
para toda a execução, porque as expectativas estão escritas segundo os textos russos. As verificações de outro idioma
usam o seu próprio `Localizer` e ativam-no para um fluxo de trabalho através de `Texts.Scope(...)`, por isso os testes em
paralelo não se incomodam uns aos outros.

## Adicionar um idioma

1. Copie `Sources/Core/Golether.Localization/Languages/en.json` para `<code>.json` (um código ISO 639-1 de duas letras),
   preencha `Language.Code` e `Language.Name` (o nome do idioma nesse idioma) e traduza os valores. As chaves e os
   marcadores `{0}`, `{1}` não mudam; uma tradução que tenha perdido uma chave ou um marcador faz falhar
   `Golether.Localization.Tests`.
   As 64 palavras do código de verificação (`Verification.Word.00` … `Verification.Word.63`) são traduções das mesmas
   coisas em cada idioma: duas pessoas com idiomas de interface diferentes comparam palavras com o mesmo significado.
   Mantenha-as curtas, fáceis de dizer em voz alta e diferentes umas das outras.
2. Coloque uma bandeira em `Sources/Apps/Golether.UI/Assets/Flags/<code>.png` (120×80, proporção 3:2): desenhe-a em
   `Scripts/Assets/generate-flags.ps1`, que desenha todas as bandeiras (`en`, `de`, `fr`, `es`, `pt`, `ru`). O monitor do
   servidor mostra os mesmos ficheiros. Um teste verifica que cada idioma tem a sua bandeira.
3. É tudo para a aplicação e o monitor: o pacote é apanhado automaticamente, o idioma aparece nas listas e é escolhido
   pelo idioma do sistema no primeiro arranque.

O idioma da aplicação não é tudo o que uma pessoa lê. São traduzidos à parte:

- a página do projeto: mais uma entrada na `list` de `Sources/Apps/Golether.Pages/assets/languages.js`, a sua bandeira
  em `assets/flags/<code>.svg` e mais uma tabela em `assets/site.js` e `assets/join.js`;
- os assistentes de instalação: `[Languages]`, `[Messages]` e `[CustomMessages]` em `Scripts/App/golether.iss` e
  `Scripts/Server/golether-server.iss` (o Inno Setup traz as mensagens padrão de cada idioma usado);
- as notas dos pacotes: mais um modelo em `Scripts/App/Notes` e `Scripts/Server/Notes` (`README.<code>.txt`, ver
  `Scripts/README.md`);
- a documentação (ver abaixo).

Um texto novo: adicione a chave a **cada** ficheiro `Languages/*.json` e obtenha-a com `Texts.Get("Key")` no código ou
`{loc:Loc Key}` em XAML. Não pode haver cadeias em linguagem humana no código (o teste `NoRussianTextIsLeftInTheCode`).

## Documentação

Os documentos estão organizados por idioma: `Docs/En` é o conjunto principal, e `Docs/De`, `Docs/Es`, `Docs/Fr`,
`Docs/Pt` e `Docs/Ru` contêm o mesmo conjunto em alemão, espanhol, francês, português e russo. As regras:

- o conjunto de ficheiros e os seus nomes são iguais em cada idioma; uma alteração de significado entra em todos os
  idiomas ao mesmo tempo;
- as ligações de fora (o README) levam a `Docs/En`; a página do projeto abre o guia no idioma escolhido nela, e o inglês
  quando não há essa tradução (a tabela `docs` em `assets/languages.js`);
- as imagens são comuns e ficam em `Docs/images`;
- um idioma novo é uma pasta nova `Docs/<Code>` com o mesmo conjunto, mais uma linha na tabela `docs` da página.

O guia do administrador do servidor fica junto ao servidor, porque faz parte dos seus pacotes:
`Sources/Apps/Golether.Web.Server/ADMIN-GUIDE.md` é o inglês e `ADMIN-GUIDE.<code>.md` os restantes (`de`, `es`, `fr`,
`pt`, `ru`). Aplicam-se as mesmas regras: um conteúdo em cada idioma; outro idioma é mais um `ADMIN-GUIDE.<code>.md`, que
o projeto apanha sozinho.

## Executar a partir do VS Code

`.vscode/launch.json`:

| Perfil | O que faz |
|---|---|
| Golether: host | compila o `Golether.UI` com as suas dependências e inicia-o com os dados em `.vscode/dev/host/data` |
| Golether: participant | o mesmo com os dados em `.vscode/dev/guest/data` |
| Golether: host + participant | dois dispositivos independentes num computador; convide pelo endereço `127.0.0.1`. A regra da “instância única” não os incomoda: aplica-se dentro de uma pasta de dados |
| Rendezvous server | compila e inicia o `Golether.Web.Server` em `127.0.0.1:47900`; escolha “O meu próprio servidor” no seletor “Modo de ligação” do anfitrião e escreva esse endereço ([RendezvousServer.md](RendezvousServer.md)) |
| DbMigrator | compila e inicia o migrador com argumentos pedidos num aviso |

As tarefas de compilação (`.vscode/tasks.json`) chamam `dotnet build <csproj>`: só é compilado o projeto escolhido e
aquilo de que depende. Uma compilação completa da solução é uma tarefa à parte, `build: solution`.

Variáveis de ambiente:

| Variável | Finalidade |
|---|---|
| `GOLETHER_DATA_DIR` | a pasta dos dados (base de dados, chaves, túneis) em vez de `data` junto ao programa; com ela, os dados de compilações antigas não são movidos |
| `GOLETHER_LIBMPV` | um caminho explícito para a libmpv |

## Base de dados

O esquema só é criado pelas migrações do FluentMigrator. Para alterar o esquema:

1. Adicione `Scripts/000N_Name.Up.sql` e `000N_Name.Down.sql` a `Golether.Core.Data.Migrations.SQLite`.
2. Adicione uma classe `Versions/M000N_Name.cs` com `[Migration(N, "...")]`, derivada de `ScriptMigration`.
3. Atualize as entidades e o `GoletherDbContext` (o EF Core só faz corresponder o modelo às tabelas).

A aplicação aplica as migrações no arranque. À mão:

```bash
dotnet run --project Sources/Database/Golether.Dbs.SQLite.DbMigrator -- --database path/golether.db [--list | --down 0]
```

## Regras do código

Ver [AGENTS.md](../../AGENTS.md) (em inglês): um tipo público por ficheiro, um comentário XML em cada membro (CS1591 é um
erro), chavetas à volta de cada bloco, linhas em branco entre os passos lógicos de um método, operadores lógicos no fim
da linha, as enumerações em pastas `Enums`, as diretivas `using` nos blocos System, bibliotecas, Golether, apenas
dependências compatíveis com a GPL, o tempo através de `TimeProvider`/`IMonotonicClock`, as entradas
não fiáveis são sempre validadas.

## Pacotes

| Script | Resultado |
|---|---|
| `Scripts/App/publish-win.ps1 [-Runtime win-x64]` | `artifacts/publish/Golether-<version>-win-x64.zip` |
| `Scripts/App/publish-linux.sh [linux-x64] [Release] [out]` | `.tar.gz` e `.zip` (se o `zip` existir), `install-desktop-entry.sh`, `.deb` (onde existir `dpkg-deb`: Debian, Ubuntu, WSL) |
| `Scripts/App/publish-macos.sh [osx-arm64\|osx-x64] ...` | `Golether.app` (Info.plist, ícone, assinatura ad hoc) num arquivo |
| `Scripts/App/publish-linux.ps1 [-Runtime linux-x64]` | o mesmo arquivo Linux, compilado a partir do Windows no WSL (ver abaixo) |
| `Scripts/Assets/update-site-screenshot.ps1` | a imagem da página do projeto a partir de `Docs/images/screenshot.png` |
| `Scripts/Assets/generate-installer-art.ps1` | as imagens do assistente de instalação (um painel escuro com o logótipo e o logótipo do cabeçalho) em `artifacts/installer-art`; os dois scripts de compilação dos instaladores chamam-no sozinhos |
| `Scripts/Assets/generate-icon.ps1` | `golether.ico`, PNG 256/512 a partir da geometria de `golether.svg` |
| `Scripts/App/build-installer.ps1 [-SkipPublish]` (o arquivo é compilado pela mesma execução) | `artifacts/publish/Golether-<version>-setup.exe` (precisa do Inno Setup 6 ou mais recente; verificado com o 7.1) e uma entrada pronta para o ficheiro das atualizações |
| `Scripts/build-release.ps1 [-Skip <passos>] [-ContinueOnError]` | tudo o que uma versão compila no Windows, um a seguir ao outro, e `artifacts/publish/SHA256SUMS-<version>.txt` (ver abaixo) |

A compilação é autónoma; a máquina de destino não precisa de .NET. O conteúdo de `Native/<RID>/` vai para `app/native`.
A raiz do pacote do Windows contém o `Golether.exe` com o ícone da aplicação: é o mesmo host .NET (o target
`GoletherRootLauncher` em `Golether.UI.csproj`, a tarefa do SDK `CreateAppHost`), só que com o caminho relativo
`app\Golether.dll`. Assim, o pacote arranca com um duplo clique a partir de qualquer sítio, e o `Golether.exe` pode ter um
atalho ou ser afixado na barra de tarefas. Os pacotes de Linux e macOS são compilados no sistema de destino: só aí as
permissões de execução sobrevivem no arquivo.

Como compilá-los sem ter à mão uma máquina Linux ou um Mac:

- **Linux a partir do Windows.** `./Scripts/App/publish-linux.ps1` executa a compilação no WSL: o código-fonte é copiado
  para o sistema de ficheiros do Linux e compilado aí, e o `Golether-<version>-linux-x64.tar.gz` pronto volta para
  `artifacts/publish`. A primeira execução instala o .NET SDK em `~/.dotnet` da distribuição (com o script oficial, sem
  `sudo`).
- **Toda a versão a partir do Windows.** `./Scripts/build-release.ps1` executa `App/build-installer.ps1`,
  `App/publish-linux.ps1`, `Server/build-server-installer.ps1`, `Server/publish-server-linux.ps1` e
  `Server/publish-server-macos.ps1` um a seguir ao outro, cada um num processo de PowerShell próprio. A primeira falha
  para-o; `-ContinueOnError` executa o resto, `-Skip AppLinux,ServerMacOS` deixa passos de fora. No fim, lista os
  pacotes da versão com o tamanho e o SHA-256 e escreve `artifacts/publish/SHA256SUMS-<version>.txt` para a versão. A
  máquina precisa do Inno Setup, do WSL e do Git para Windows; cada passo diz o que falta.
- **No GitHub.** Correm lá dois workflows. **CI** (`.github/workflows/ci.yml`) compila a solução e corre todos os
  testes no Windows e no Linux a cada push para `main` e a cada pull request; com as regras de estilo e os testes de
  convenções, uma regra violada põe a execução a vermelho. **Packages** (`.github/workflows/packages.yml`) compila
  todos os pacotes de uma versão: a aplicação para Windows (instalador e `.zip`), Linux (`.deb`, `.tar.gz`, `.zip`) e
  macOS (Apple silicon e Intel), e o servidor para Windows, Linux e macOS. Uma execução à mão (Actions → Packages →
  Run workflow) dá-os como artefactos da execução. Uma etiqueta de versão igual a `<Version>` de
  `Directory.Build.props` (`git tag 0.3.2; git push origin 0.3.2`) anexa-os também, com `SHA256SUMS-<version>.txt`, à
  versão dessa etiqueta, criada como rascunho: escreva as notas, verifique os ficheiros, publique e só depois atualize
  `updates.json` da página do projeto. A assinatura de código entra nos trabalhos de Windows deste workflow.

## Depurar a rede

- Duas instâncias numa máquina: o perfil “Golether: host + participant”, com o endereço `127.0.0.1:47800` no convite.
- Ping elevado e perdas: no Windows [clumsy](https://jagt.github.io/clumsy/), no Linux
  `tc qdisc add dev lo root netem delay 600ms loss 2%`.
- Sem a libmpv, usa-se o leitor simulado: a posição segue o relógio, por isso a sincronização pode ser verificada sem
  vídeo.
