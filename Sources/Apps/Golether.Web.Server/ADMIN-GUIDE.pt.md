# Servidor de encontro do Golether: guia do administrador

Este guia está na pasta `docs` de cada pacote do servidor e serve igualmente para Windows, Linux e macOS. Onde os passos
diferem, são dados para cada sistema em separado. O mesmo guia noutros idiomas: [English](ADMIN-GUIDE.md),
[Deutsch](ADMIN-GUIDE.de.md), [Español](ADMIN-GUIDE.es.md), [Français](ADMIN-GUIDE.fr.md), [Русский](ADMIN-GUIDE.ru.md).

- [1. O que o servidor faz](#1-o-que-o-servidor-faz)
- [2. O que é preciso](#2-o-que-é-preciso)
- [3. O que está no pacote](#3-o-que-está-no-pacote)
- [4. Instalação](#4-instalação)
- [5. Definições](#5-definições)
- [6. Router e firewall](#6-router-e-firewall)
- [7. O que dizer aos anfitriões](#7-o-que-dizer-aos-anfitriões)
- [8. Gerir o serviço](#8-gerir-o-serviço)
- [9. Verificar que funciona](#9-verificar-que-funciona)
- [10. Atualização](#10-atualização)
- [11. Remoção](#11-remoção)
- [12. Segurança](#12-segurança)
- [13. Resolução de problemas](#13-resolução-de-problemas)

## 1. O que o servidor faz

O Golether é um programa para ver vídeos em conjunto. Normalmente, o anfitrião e os participantes estão atrás de routers
domésticos sem portas abertas (um IP “cinzento”, o NAT do fornecedor) e não se conseguem alcançar diretamente. O servidor
de encontro apresenta-os:

- o anfitrião mantém uma “sala” da sua sessão no servidor;
- um participante, chegado pela ligação do convite, pede ao servidor para o juntar ao anfitrião;
- o servidor diz a ambos o endereço público do outro, e eles abrem por si uma ligação **direta** através dos seus
  routers;
- na mesma porta, mas por UDP, o servidor responde a pedidos STUN: é assim que as câmaras e a voz também se ligam
  diretamente.

O filme, a sincronização, a conversa, as câmaras e a voz **não** passam pelo servidor; só passam mensagens de serviço
curtas no momento da ligação. A carga é mínima; um servidor serve centenas de sessões em simultâneo.

## 2. O que é preciso

| | |
|---|---|
| Rede | um endereço IPv4 público na máquina, ou num router onde se possa reencaminhar uma porta |
| Portas | **TCP 47900** e **UDP 47900** a partir de fora (o número é configurável); a porta 47980 é apenas local |
| Windows | Windows 10 / Windows Server 2016 ou mais recente, x64 ou arm64 |
| Linux | x64 ou arm64 com glibc (Debian 11+, Ubuntu 20.04+, RHEL/Alma/Rocky 8+ e semelhantes) e systemd para executar como serviço |
| macOS | macOS 14 ou mais recente, Apple silicon ou Intel |
| Recursos | cerca de 100 MB de disco e 60–100 MB de memória |

Não é preciso instalar o .NET: vem dentro do pacote.

## 3. O que está no pacote

| Ficheiro | Para que serve |
|---|---|
| `golether-server` (`golether-server.exe` no Windows) | inicia o servidor; o único ficheiro na raiz; o mesmo programa funciona numa consola e como serviço |
| `bin/` | o próprio programa, o runtime do .NET e as bibliotecas; não é preciso olhar para lá |
| `config/appsettings.json` | as definições: endereço, porta, limites (ver a [secção 5](#5-definições)) |
| `config/monitor.json` | as definições do próprio monitor (o seu idioma); nunca tocam no servidor |
| `docs/ADMIN-GUIDE.md`, `docs/ADMIN-GUIDE.<code>.md` | este guia em inglês e em alemão (`de`), espanhol (`es`), francês (`fr`), português (`pt`) e russo (`ru`) |
| `docs/README.txt`, `docs/README.<code>.txt` | uma nota curta para este pacote em concreto, nos mesmos idiomas |
| `service/install-service.sh` | Linux e macOS: instala, atualiza e remove o serviço |
| `service/golether-server.service` | Linux: a unidade do systemd (instalada pelo `install-service.sh`) |
| `service/org.golether.server.plist` | macOS: a tarefa do launchd (instalada pelo `install-service.sh`) |
| `golether-server-monitor` (`golether-server-monitor.exe` no Windows) | inicia o Golether Server Monitor, junto ao servidor; o monitor em si está em `monitor/`. O monitor: o estado do servidor e o seu registo de eventos numa janela, iniciar e parar o serviço, as definições (ver a [secção 9](#9-verificar-que-funciona)) |

```
GoletherServer/
  golether-server(.exe)
  golether-server-monitor(.exe)
  bin/        o programa e as bibliotecas
  config/     appsettings.json, monitor.json
  docs/       ADMIN-GUIDE.md, ADMIN-GUIDE.<code>.md, README.txt, README.<code>.txt
  service/    apenas Linux e macOS
  monitor/    Golether Server Monitor (os ficheiros do programa)
```

O instalador do Windows coloca os ficheiros da mesma forma; os seus próprios ficheiros de desinstalação estão na pasta
`uninstall`.

Para Windows existe também um instalador à parte, `GoletherServer-<version>-setup.exe`.

## 4. Instalação

Escolha o caminho para o seu sistema. Em todos os casos, é preciso abrir as portas depois da instalação
([secção 6](#6-router-e-firewall)).

### Windows: o instalador (recomendado)

Execute `GoletherServer-<version>-setup.exe` (são precisos direitos de administrador). O assistente fala inglês, alemão,
espanhol, francês, português e russo; a sua primeira janela propõe o idioma do Windows (o inglês, se o assistente não o
falar), e pode escolher outro. O instalador:

- coloca o servidor em `C:\Program Files\Golether Server`;
- regista o serviço **GoletherServer** (“Golether rendezvous server”): arranca com o computador e reinicia 5 segundos
  depois de uma falha;
- adiciona uma regra da firewall do Windows para o programa (TCP e UDP, qualquer porta);
- adiciona atalhos para as definições, para este guia e para a página de estado em “Iniciar → Golether Server”.

O serviço e a regra da firewall podem ser desativados com as caixas do passo “Selecionar tarefas adicionais”.

### Windows: a partir do arquivo zip

Descompacte o arquivo, por exemplo em `C:\GoletherServer`, e execute `golether-server.exe` a partir da raiz da pasta: o
servidor funciona enquanto a janela da consola estiver aberta. Para o executar como serviço, execute no PowerShell
**como administrador**, a partir da raiz da pasta do servidor:

```powershell
$exe = Join-Path (Get-Location) 'golether-server.exe'
New-Service -Name GoletherServer -DisplayName 'Golether rendezvous server' -BinaryPathName "`"$exe`"" -StartupType Automatic
sc.exe failure GoletherServer reset= 86400 actions= restart/5000/restart/5000/restart/60000
New-NetFirewallRule -DisplayName 'Golether rendezvous server' -Direction Inbound -Program $exe -Action Allow
Start-Service GoletherServer
```

Não mova a pasta depois: o serviço inicia o programa pelo seu caminho completo.

### Linux: um serviço systemd

```bash
tar -xzf GoletherServer-<version>-linux-x64.tar.gz
cd GoletherServer
sudo ./service/install-service.sh
```

O script copia o servidor para `/opt/golether-server`, instala a unidade `golether-server.service`, ativa-a no arranque e
inicia o serviço. Com o `ufw` ativado, o script abre ele próprio a porta das definições; para `firewalld` e `iptables`,
ver a [secção 6](#6-router-e-firewall). O serviço é executado como um utilizador temporário sem privilégios
(`DynamicUser`) com proteção do sistema de ficheiros; o servidor não precisa de direitos de root.

Sem serviço: `./golether-server` na raiz da pasta do pacote (`Ctrl+C` para-o).

### macOS: um serviço launchd

```bash
tar -xzf GoletherServer-<version>-osx-arm64.tar.gz     # osx-x64 para Intel
cd GoletherServer
sudo ./service/install-service.sh
```

O script copia o servidor para `/usr/local/golether-server`, retira a quarentena dos ficheiros transferidos, assina o
programa com uma assinatura local (o macOS em Apple silicon exige uma), instala a tarefa
`/Library/LaunchDaemons/org.golether.server.plist` e inicia-a. O servidor é executado como o utilizador `nobody` e
reinicia depois de uma falha.

Sem serviço: `./golether-server` na raiz da pasta do pacote. Se o macOS disser que o programa não pode ser aberto,
execute aí `xattr -dr com.apple.quarantine .` e `codesign --force --sign - golether-server bin/golether-server`. No
primeiro arranque, a firewall do macOS (se estiver ativada) pergunta se deve permitir ligações de entrada: permita-as.

## 5. Definições

Todas as definições estão em `config/appsettings.json` dentro da pasta do servidor:

| Sistema | Onde fica o ficheiro depois de instalar como serviço |
|---|---|
| Windows | `C:\Program Files\Golether Server\config\appsettings.json` |
| Linux | `/opt/golether-server/config/appsettings.json` |
| macOS | `/usr/local/golether-server/config/appsettings.json` |

Se o ficheiro faltar (apagado por engano, por exemplo), o servidor não falha: volta a escrevê-lo com as definições
predefinidas abaixo e indica-o no seu registo. Se não for possível escrever na pasta, o servidor arranca na mesma com
esses mesmos valores predefinidos. Uma chave que falte no ficheiro também fica com o seu valor predefinido.

```json
{
  "Rendezvous": {
    "ListenAddress": "0.0.0.0",
    "Port": 47900,
    "EnableStun": true
  },
  "Urls": "http://127.0.0.1:47980"
}
```

| Parâmetro `Rendezvous:…` | Predefinição | O que define |
|---|---|---|
| `ListenAddress` | `0.0.0.0` | o endereço IP **desta máquina** em que escutar: `0.0.0.0` é qualquer interface IPv4, um endereço concreto só esse, `::` é IPv6 e IPv4 |
| `Port` | `47900` | a porta: TCP para os encontros e o mesmo número por UDP para STUN |
| `EnableStun` | `true` | se deve responder a STUN por UDP; sem isso, as câmaras e a voz de redes diferentes ligam-se pior |
| `MaxConnections` | `1024` | ligações abertas ao mesmo tempo |
| `MaxRooms` | `1024` | sessões em curso ao mesmo tempo |
| `MaxPendingPerRoom` | `16` | ligações à espera da resposta de um anfitrião |
| `MaxConnectionsPerMinute` | `120` | novas ligações a partir de um IP por minuto |
| `MaxStunPerMinute` | `600` | pedidos STUN a partir de um IP por minuto |
| `FirstMessageTimeout` | `00:00:10` | quanto tempo esperar pela primeira mensagem depois de uma ligação |
| `AnswerTimeout` | `00:00:20` | quanto tempo um anfitrião pode demorar a responder à ligação de um participante |
| `IdleTimeout` | `00:01:30` | quanto tempo um anfitrião pode ficar em silêncio antes de a sua sala ser libertada |

| Parâmetro | Predefinição | O que define |
|---|---|---|
| `Urls` | `http://127.0.0.1:47980` | o endereço da página de estado `/health`; mantenha `127.0.0.1` para que só seja visível nesta máquina |
| `Logging:LogLevel:Default` | `Information` | o nível de pormenor do registo; `Debug` regista cada ligação |
| `Logging:Console:FormatterOptions:TimestampFormat` | `yyyy-MM-dd HH:mm:ss.fff zzz ` | a marca no início de cada linha do registo na consola e no ficheiro: a data, a hora do servidor e a diferença em relação ao UTC (`2026-10-11 16:14:12.250 +07:00`). No systemd e no Visualizador de Eventos do Windows, a hora de uma entrada é definida pelo próprio sistema |

Qualquer parâmetro também pode ser definido sem editar o ficheiro, com uma variável de ambiente (um duplo sublinhado em
vez dos dois pontos) ou um argumento da linha de comandos; estes têm prioridade sobre o ficheiro:

```bash
./golether-server --Rendezvous:Port=3478                      # argumento, qualquer sistema
Rendezvous__Port=3478 ./golether-server                        # variável, Linux e macOS
```

```powershell
$env:Rendezvous__Port = '3478'; .\golether-server.exe          # variável, Windows
```

**Reinicie o serviço depois de editar `config/appsettings.json`** ([secção 8](#8-gerir-o-serviço)). Se uma definição
estiver errada, o servidor não arranca e enumera os erros (na consola ou no registo do serviço).

## 6. Router e firewall

**Ambos** os protocolos têm de estar acessíveis a partir de fora na porta do servidor.

**Router.** Se o IP público pertencer ao router e o servidor estiver atrás dele, reencaminhe para o endereço local do
servidor:

| Porta externa | Protocolo | Para onde |
|---|---|---|
| 47900 | TCP | `<IP local do servidor>:47900` |
| 47900 | UDP | `<IP local do servidor>:47900` |

Não reencaminhe a porta 47980 para fora. Em servidores na nuvem (VPS), abra as mesmas portas no grupo de segurança ou na
firewall do painel do fornecedor.

**Firewall do sistema:**

| Sistema | Comandos |
|---|---|
| Windows | o instalador trata disso; à mão: `New-NetFirewallRule -DisplayName 'Golether rendezvous server' -Direction Inbound -Program '<pasta do servidor>\golether-server.exe' -Action Allow` |
| Linux, ufw | `sudo ufw allow 47900/tcp && sudo ufw allow 47900/udp` (com o ufw ativado, o `install-service.sh` trata disso) |
| Linux, firewalld | `sudo firewall-cmd --permanent --add-port=47900/tcp --add-port=47900/udp && sudo firewall-cmd --reload` |
| Linux, iptables | `sudo iptables -A INPUT -p tcp --dport 47900 -j ACCEPT && sudo iptables -A INPUT -p udp --dport 47900 -j ACCEPT` |
| macOS | “Definições do Sistema → Rede → Firewall → Opções”: permitir ligações de entrada para `golether-server` (se a firewall estiver ativada) |

## 7. O que dizer aos anfitriões

Os anfitriões escolhem **“O meu próprio servidor”** no seletor “Modo de ligação” do cartão “Mostrar um filme” do Golether
e escrevem o endereço do servidor no campo que aparece, como `endereço:porta`. Aos participantes não é preciso dizer
nada: o endereço chega-lhes no convite.

(A posição “Automaticamente” desse seletor procura o servidor do próprio projeto Golether, não o seu: um anfitrião que
queira usar o seu servidor tem de escolher “O meu próprio servidor”.)

Dê o endereço com que o servidor é visto **a partir da Internet**, não o `ListenAddress`:

| Onde está o servidor | O que os anfitriões escrevem |
|---|---|
| Um VPS ou uma máquina com IP público na interface | esse IP (ou um nome de domínio): `203.0.113.7:47900` |
| Em casa atrás de um router com reencaminhamento | o IP externo do router, **não** `192.168.x.x`: `198.51.100.20:47900` |
| A porta foi alterada | a porta nova: `203.0.113.7:3478` |

Se o IP externo do router mudar, arranje um nome de DNS dinâmico para ele e dê esse nome.

## 8. Gerir o serviço

| Ação | Windows (PowerShell como administrador) | Linux | macOS |
|---|---|---|---|
| Estado | `Get-Service GoletherServer` | `systemctl status golether-server` | `sudo launchctl print system/org.golether.server` |
| Iniciar | `Start-Service GoletherServer` | `sudo systemctl start golether-server` | `sudo launchctl kickstart system/org.golether.server` |
| Parar | `Stop-Service GoletherServer` | `sudo systemctl stop golether-server` | `sudo launchctl bootout system/org.golether.server` |
| Reiniciar | `Restart-Service GoletherServer` | `sudo systemctl restart golether-server` | `sudo launchctl kickstart -k system/org.golether.server` |
| Registo | “Visualizador de Eventos → Registos do Windows → Aplicação”, origem `golether-server` | `journalctl -u golether-server -f` | `tail -f /Library/Logs/golether-server.log` |

Depois de parar com `bootout` no macOS, o serviço volta a ser iniciado com
`sudo launchctl bootstrap system /Library/LaunchDaemons/org.golether.server.plist` (ou reiniciando o computador).

O mesmo no Windows a partir da linha de comandos clássica (`cmd` como administrador):

| Ação | Comando |
|---|---|
| Estado | `sc query GoletherServer` |
| Iniciar | `sc start GoletherServer` |
| Parar | `sc stop GoletherServer` |

O programa `Golether Server Monitor` faz tudo isto com botões e mostra o que o servidor está a fazer; ver a
[secção 9](#9-verificar-que-funciona).

**O serviço volta sozinho.** Arranca com o computador, e o instalador disse ao Windows para o reiniciar 5 segundos depois
de uma falha. Terminar o processo no Gestor de Tarefas conta como uma falha, por isso um minuto depois o servidor está de
novo em execução. Pare-o antes como serviço (os comandos acima ou `services.msc`).

**O `golether-server.exe` iniciado a partir de uma consola diz que a porta está ocupada.** Normalmente está ocupada pelo
próprio serviço, que já está em execução. Não é precisa uma segunda cópia: o serviço faz o mesmo trabalho. Para executar o
servidor numa consola (para ver as suas mensagens, por exemplo), pare primeiro o serviço e volte a iniciá-lo depois.

## 9. Verificar que funciona

### Golether Server Monitor

O mais simples é o monitor: “Golether Server Monitor” no menu Iniciar no Windows, `golether-server-monitor` junto a
`golether-server` na pasta do servidor no Linux (é preciso um ambiente de trabalho) e no macOS. Mostra:

- **o estado** por palavras: em execução como serviço, em execução à mão (não como serviço), parado, o serviço não está
  instalado; há quanto tempo o servidor está em execução, as salas, as ligações, os encontros e as recusas;
- **o registo de eventos** em colunas: hora | endereço IP | evento | pormenores (o anfitrião, uma etiqueta da sala, porque
  é que um pedido foi recusado). Os eventos mais recentes ficam no topo; a caixa de pesquisa encontra um endereço, um
  anfitrião, uma sala ou um evento; os separadores do cabeçalho mudam a largura das colunas; um clique com o botão
  direito copia as linhas selecionadas;
- os botões **Iniciar** e **Parar** do serviço e **Exportar o registo** para um ficheiro;
- o separador **Definições**: os campos de `config/appsettings.json` com explicações, verificados pelas mesmas regras que
  no arranque do servidor. “Guardar e reiniciar” escreve o ficheiro e reinicia um serviço em execução.

O idioma do monitor (English, Deutsch, Español, Français, Português ou Русский, cada um com a sua bandeira) escolhe-se na
sua janela e muda de imediato. Fica guardado em `config/monitor.json` junto às definições do servidor (um ficheiro próprio
do monitor: alterá-lo nunca reinicia o servidor), e qualquer utilizador o pode mudar. No primeiro arranque, o monitor
fala o idioma do sistema (o inglês, se não falar esse).

Iniciar, parar e guardar precisam de direitos de administrador: o Windows pede-os através do UAC, o Linux através do
`pkexec`, o macOS pede a palavra-passe. Para ver não são precisos direitos especiais.

O registo fica na memória do servidor (os últimos 5000 eventos) e começa vazio em cada arranque do servidor. Nada dele é
escrito em disco, a menos que prima **Exportar o registo**: nesse caso, exatamente o que o monitor mostra vai para o
ficheiro que escolher. O monitor lê o registo pelo endereço de estado deste computador; outros computadores não o
conseguem ler.

### À mão

**Na própria máquina**, a página de estado:

```bash
curl http://127.0.0.1:47980/health                               # Linux, macOS
```

```powershell
Invoke-RestMethod http://127.0.0.1:47980/health                   # Windows
```

```json
{ "status": "ok", "version": "0.3.0", "protocol": 1, "uptimeSeconds": 3600,
  "rooms": 2, "connections": 3, "meetingsRequested": 14, "meetingsIntroduced": 13,
  "refused": 0, "stunAnswered": 211 }
```

| Campo | Significado |
|---|---|
| `rooms` | sessões em curso com este servidor |
| `connections` | ligações abertas |
| `meetingsRequested` / `meetingsIntroduced` | quantas ligações os participantes pediram / a quantos lados o servidor passou o endereço do outro |
| `refused` | recusas por causa dos limites |
| `stunAnswered` | respostas STUN (aumenta enquanto há sessões com câmaras) |

**A partir de fora**, a partir de outra máquina noutra rede (por exemplo, um portátil ligado à Internet do telemóvel):

```bash
nc -vz 203.0.113.7 47900                                         # Linux, macOS: a porta TCP está aberta?
```

```powershell
Test-NetConnection 203.0.113.7 -Port 47900                        # Windows: TcpTestSucceeded : True
```

O UDP verifica-se com um cliente STUN, por exemplo `stunclient 203.0.113.7 47900` (o pacote `stuntman-client` no Linux).
A verificação final: um anfitrião indica o servidor e cria uma sessão, e a lista de eventos do Golether mostra “O servidor
de encontro está pronto”.

## 10. Atualização

| Sistema | Como atualizar |
|---|---|
| Windows | execute o instalador da nova versão: para o serviço, substitui os ficheiros e volta a iniciá-lo; `config\appsettings.json` é mantido |
| Windows, zip | pare o serviço, substitua tudo exceto a pasta `config`, inicie o serviço |
| Linux, macOS | descompacte a nova versão e volte a executar `sudo ./service/install-service.sh`; `config/appsettings.json` é mantido |

Versões diferentes do servidor e da aplicação são compatíveis enquanto a versão do protocolo for a mesma (o campo
`protocol` de `/health`).

## 11. Remoção

| Sistema | Como remover |
|---|---|
| Windows | “Definições → Aplicações → Golether Server → Desinstalar”: o serviço e a regra da firewall são removidos |
| Windows, zip | `Stop-Service GoletherServer; sc.exe delete GoletherServer; Remove-NetFirewallRule -DisplayName 'Golether rendezvous server'`, depois apague a pasta |
| Linux, macOS | `sudo ./service/install-service.sh uninstall` a partir da pasta do pacote ou da pasta instalada |

Não se esqueça de fechar o reencaminhamento da porta no router.

## 12. Segurança

- **O servidor não vê as sessões.** Tudo o que viaja entre o anfitrião e os participantes é cifrado entre os seus
  dispositivos; o servidor só conhece endereços IP, as chaves públicas dos anfitriões e a hora das ligações. O servidor
  não se pode fazer passar por um anfitrião: o participante verifica a chave do anfitrião a partir do convite.
- **Uma sala não pode ser usurpada:** está ligada à chave do dispositivo do anfitrião, e o registo é assinado.
- **Direitos mínimos.** O serviço no Linux e no macOS não precisa de direitos de root (um utilizador temporário /
  `nobody`); no Windows, o serviço é executado com a conta do sistema, como a maioria dos serviços.
- **Os limites** do número de ligações, de salas e da frequência de pedidos a partir de um IP protegem contra a
  sobrecarga. Perante um ataque, baixe `MaxConnectionsPerMinute` e `MaxStunPerMinute`.
- **A página `/health`** não contém endereços nem nomes; mesmo assim, mantenha-a em `127.0.0.1`.
- **O registo de eventos** (`/journal`, lido pelo monitor) contém os endereços IP dos dispositivos, os identificadores
  curtos dos anfitriões e etiquetas das salas (não os seus nomes: uma etiqueta não permite a ninguém chegar ao anfitrião).
  Só é servido a este computador, seja qual for o endereço da página de estado, e vive apenas na memória.
- O servidor não guarda nada em disco além do registo. Só a pasta `config` precisa de cópia de segurança.
- Atualize o servidor juntamente com a aplicação Golether.

## 13. Resolução de problemas

| O que vê | O que verificar |
|---|---|
| O serviço não arranca | o registo do serviço ([secção 8](#8-gerir-o-serviço)): uma definição errada, ou o servidor não consegue escutar porque a porta está ocupada por outro programa (`netstat -ano \| findstr 47900` no Windows, `sudo ss -lntup \| grep 47900` no Linux, `sudo lsof -i :47900` no macOS) |
| O servidor iniciado a partir de uma consola diz que a porta está ocupada, e volta depois de ser terminado no Gestor de Tarefas | o serviço já está em execução e é reiniciado depois de cada falha: pare-o como serviço ([secção 8](#8-gerir-o-serviço)) |
| O anfitrião vê que não é possível chegar ao servidor de encontro | o serviço está em execução; `/health` responde; a porta TCP está reencaminhada e aberta ([secção 9](#9-verificar-que-funciona), a verificação a partir de fora); ao anfitrião foi dado o endereço externo, não o local |
| Um participante vê que o anfitrião não está no servidor de encontro | a sessão continua em curso; a lista de eventos do anfitrião disse que o servidor estava pronto; `rooms` em `/health` é maior do que zero |
| Os routers não deixaram passar a ligação direta | o servidor fez a sua parte (`meetingsIntroduced` aumenta), mas a rede de um dos lados não permite ligações diretas (um NAT simétrico, comum em operadores móveis). O servidor não pode resolver isto: é preciso um túnel AmneziaWG ou uma porta reencaminhada do lado do anfitrião |
| O filme passa, não há câmaras | a porta UDP está reencaminhada e aberta; `EnableStun` = `true`; `stunAnswered` aumenta |
| `refused` aumenta depressa | alguém está a chocar com os limites: muitos participantes atrás de um IP, por isso aumente `MaxConnectionsPerMinute`; atividade suspeita, por isso veja o registo no nível `Debug` |
| macOS: o programa não pode ser aberto | na raiz da pasta do servidor: `xattr -dr com.apple.quarantine .` e `codesign --force --sign - golether-server bin/golether-server` |
| Linux: `Permission denied` no arranque | na raiz da pasta do servidor: `chmod +x golether-server bin/golether-server service/install-service.sh` (se o arquivo foi descompactado no Windows e copiado) |
