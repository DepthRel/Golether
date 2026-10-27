# Protocolo de rede do Golether (versão 1)

## Transporte

TCP, a porta do anfitrião é **47800** por predefinição. Cada ligação:

1. TLS 1.3 (TLS 1.2 no Windows 10) com certificados dos dois lados. A validação da cadeia e do nome está desativada: o
   participante verifica o `PeerId` do certificado do anfitrião com o convite, o anfitrião calcula o `PeerId` do
   participante.
2. O preâmbulo do cliente, 6 bytes: `47 4C 54 48` (`GLTH`), versão `01`, finalidade (`01` Control, `02` MediaData,
   `03` Relay, `04` RelayShared).
3. A resposta do anfitrião, 1 byte: `01` significa aceite.

`PeerId` = SHA-256 em hexadecimal minúsculo do SubjectPublicKeyInfo DER da chave do dispositivo (ECDSA P-256).

## Control

Tramas: 4 bytes de comprimento (big-endian) e JSON UTF-8, no máximo 256 KiB. O tipo é dado pelo campo `$t`; os nomes dos
campos estão em camelCase. A é o anfitrião, P um participante.

| `$t` | Sentido | Campos | Significado |
|---|---|---|---|
| `hello` | P → A | `version`, `displayName`, `inviteToken`, `gallery` | a primeira mensagem |
| `pending` | A → P | — | à espera da decisão do anfitrião |
| `welcome` | A → P | `sessionName`, `participants[]`, `playback`, `media`, `mediaDataStreams`, `relay`, `relayOverSession`, `relayShared`, `reconnectTicket` | o participante foi admitido; `relay` são a porta e as credenciais do TURN do anfitrião, criadas para uma sessão; `relayOverSession` significa que o anfitrião aceita TURN sobre fluxos `Relay` e não é precisa uma porta à parte; `relayShared` significa que também os aceita todos num fluxo `RelayShared`; `reconnectTicket` é o bilhete para voltar a esta sessão sem um convite novo |
| `rejected` | A → P | `reason`, `message` | recusa, a ligação é fechada |
| `ping` | P → A | `clientSendTime` | uma sonda de relógio (t0, µs) |
| `pong` | A → P | `clientSendTime`, `hostReceiveTime`, `hostSendTime` | t0, t1, t2 |
| `request` | P → A | `request: {kind, position}` | uma intenção: `0` reproduzir, `1` pausar, `2` saltar, `3` reproduzir sem esperar pelos que não estão prontos |
| `state` | A → todos | `state: PlaybackState` | o estado oficial |
| `status` | P → A | `status: ParticipantStatus` | uma vez por segundo; o anfitrião substitui `peerId` pelo verificado |
| `participants` | A → todos | `participants[]`, `statuses[]` | uma vez por segundo e nas alterações |
| `media` | A → todos | `media: MediaDescriptor` | um ficheiro novo |
| `rtc` | qualquer | `peer`, `kind`, `payload` | sinalização WebRTC; o anfitrião reencaminha-a colocando o remetente verificado |
| `moderate` | A → P | `microphone`, `camera` | o anfitrião desliga os dispositivos de um participante (só desligar) |
| `chat` | P → A → todos | `id`, `kind` (`0` texto, `1` reação), `text`, `sender` | uma mensagem da conversa ou uma reação; o anfitrião preenche `sender` com o identificador verificado, o valor enviado é descartado |
| `draw` | P → A → todos | `strokeId`, `phase` (`0` início, `1` continuação, `2` fim), `points[]` (`x`, `y` como frações da imagem), `sender` | um traço da caneta sobre o vídeo; o anfitrião preenche `sender` com o identificador verificado |
| `avatar` | P → A → todos | `image` (PNG, base64; vazio repõe), `sender` | a imagem de um participante, mostrada com a câmara desligada; o anfitrião preenche `sender`; um participante novo recebe do anfitrião os avatares já escolhidos |
| `gallery` | A → P | `catalogId`, `title`, `total`, `offset`, `items[]` | uma página de uma pasta mostrada; um `catalogId` vazio termina a apresentação |
| `show` | P → A → todos | `catalogId`, `index`, `version`, `sender` | o elemento no ecrã de todos; o anfitrião numera a decisão e preenche `sender` |
| `thumb` | P → A → P | `catalogId`, `index`, `image` (JPEG, base64) | o pedido de uma miniatura (sem `image`) e a resposta |
| `bye` | qualquer | `reason` | saída; `reason` vai sempre vazio e o recetor ignora-o |

`PlaybackState`: `state` (`0` em pausa, `1` a reproduzir), `position` (`hh:mm:ss.fffffff`), `referenceTime` (µs segundo o
relógio do anfitrião), `rate`, `version`, `origin`, `cause` (`0` inicial, `1` reproduzir, `2` pausa, `3` salto,
`4` à espera dos participantes, `5` todos estão prontos, `6` o filme terminou, `7` reproduzir sem esperar pelos que não
estão prontos).

Tudo o que chegou pela rede é verificado antes de ser usado:

- **Um estado** só é aceite por um participante quando o modo é conhecido, a posição está entre zero e 30 dias e `rate`
  é um número finito de 0,5 a 2. Caso contrário, o estado é ignorado (no `welcome`, a entrada é recusada).
- **Uma intenção** com um `kind` desconhecido é descartada pelo anfitrião. A posição pedida é limitada à duração do
  ficheiro e, enquanto esta não for conhecida, aos mesmos 30 dias.
- **O estado de um participante** é limitado pelo anfitrião antes de qualquer cálculo ou difusão: tempos dentro de
  ±30 dias, ping de 0 a 600 000 ms, contadores de bytes não negativos. Um participante limita da mesma forma os estados
  de `participants`.
- **O nome do ficheiro** em `media` tem no máximo 255 caracteres, sem caracteres de controlo nem separadores de caminho
  (`/`, `\`). A regra é a mesma em todos os sistemas: o anfitrião substitui esses caracteres por `_` antes de enviar, e um
  nome válido no seu sistema (com dois pontos no Linux, por exemplo) é aceite pelos outros. O nome só é mostrado; nunca
  serve de caminho.

Motivos de uma recusa: `0` convite inválido, `1` recusado, `2` o anfitrião não respondeu, `3` a sessão está cheia,
`4` versão incompatível, `5` removido pelo anfitrião. Uma mensagem `rejected` com o motivo `5` também chega a meio de
uma sessão: é assim que o anfitrião remove um participante admitido.

O campo `message` de `rejected` é uma nota curta para o registo (o nome do motivo em inglês); o participante não o
mostra, mas formula o motivo no idioma da sua própria interface a partir do campo `reason`. O anfitrião envia `bye` sem
`reason` pela mesma razão: os textos para pessoas não viajam pela rede. Um participante ignora o campo mesmo quando um
anfitrião de outra compilação o preenche, e formula ele próprio o fim da sessão.

A ordem de uma ligação: `hello` → (`pending`) → `welcome` | `rejected`. O token de um convite funciona até ao fim da sua
validade e serve todos os convidados, mas traz no máximo 16 pedidos de entrada; os contactos de confiança podem ligar-se
sem token. Um dispositivo que o anfitrião removeu ou recusou precisa de um token emitido depois: com um anterior recebe de
imediato `rejected` com o motivo `5` ou `1`, sem perguntar ao anfitrião.

### Conversa e reações

- Texto: no máximo 100 caracteres depois da limpeza. São retirados os caracteres de controlo, os substitutos e os
  caracteres de formatação (exceto U+200D), não mais de duas quebras de linha seguidas, os espaços à volta são retirados.
- Uma reação: uma de um conjunto fixo (`👍 😂 😮 😢 ❤️ 🔥 👏`); caso contrário, a mensagem é descartada.
- `id` tem até 32 dígitos hexadecimais, escolhidos pelo remetente; o anfitrião não lhe toca.
- Frequência: no máximo 5 textos e 15 reações em 5 segundos por participante (uma janela deslizante no anfitrião).
- O anfitrião envia uma mensagem a todos, incluindo o remetente: a ordem é a mesma para todos. Nada é guardado em disco.

### A caneta sobre o vídeo

- Um traço é uma sequência de mensagens `draw` com um mesmo `strokeId`: um `begin`, qualquer número de `continue` e um
  `end`. Os pontos são juntados pelo remetente e saem em lotes aproximadamente a cada 60 ms, no máximo 128 pontos por
  mensagem.
- `strokeId` tem até 32 dígitos hexadecimais, escolhidos pelo remetente; o anfitrião não lhe toca.
- As coordenadas são frações da imagem (`0`–`1`), não píxeis da janela: o traço cai no mesmo sítio da imagem para todos.
  Os valores fora dos limites são ajustados aos limites; `NaN` e infinitos são descartados.
- Uma lista de pontos vazia só é permitida na fase `end`.
- Frequência: no máximo 40 mensagens por segundo e por participante (uma janela deslizante no anfitrião); o excesso é
  descartado.
- Um traço pertence a quem o começou: um fragmento com o mesmo `strokeId` de outro participante não é aceite pelo
  recetor.
- O recetor mantém um traço terminado durante 0,3 segundos e desvanece-o nos 1,2 seguintes; um traço inacabado (o autor
  desligou-se) desaparece ao fim de 20 segundos. São desenhados no máximo 24 traços de uma vez. Nada é guardado.

### Avatares

- Um participante envia o seu avatar numa mensagem `avatar` sem o campo `sender`; o anfitrião coloca o identificador
  verificado do remetente e envia-o a todos, incluindo o remetente, para que a ordem e a imagem sejam as mesmas para
  todos.
- `image` é um PNG de no máximo 128 KiB (para caber no limite de 256 KiB por mensagem juntamente com o base64) e no
  máximo 1024 × 1024 píxeis. Os bytes não são de confiança: são verificados a assinatura PNG, o tamanho do ficheiro e o
  tamanho da imagem do cabeçalho (IHDR); caso contrário, a mensagem é descartada; um ficheiro pequeno pode declarar uma
  imagem que ocupe gigabytes de memória. Um campo vazio repõe o avatar no círculo colorido com uma letra.
- Frequência: no máximo 5 mensagens em 10 segundos por participante (uma janela deslizante no anfitrião); o excesso é
  descartado.
- O anfitrião lembra-se do último avatar de cada participante e envia-os a um participante novo logo a seguir ao
  `welcome`, para que quem chega veja as imagens já escolhidas. Um avatar é esquecido quando o participante sai.
- Um avatar é mostrado em vez da câmara enquanto a câmara está desligada. O ficheiro do avatar escolhido fica em
  `data/avatars`.

### Voltar a uma sessão

`reconnectTicket` tem 48 dígitos hexadecimais, é emitido na admissão e é válido enquanto a sessão decorre. O participante
envia-o no campo `inviteToken` do `hello`; o anfitrião compara o bilhete com o emitido para esse dispositivo em tempo
constante e salta a verificação do convite. A confirmação do anfitrião é pedida como numa entrada normal. Quando o
anfitrião remove um participante, o bilhete é-lhe retirado.

### Uma pasta mostrada

- Só os participantes que anunciaram `gallery: true` em `hello` recebem `gallery`, `show` e `thumb`: um
  participante mais antigo cortaria a ligação perante um tipo desconhecido.
- `catalogId` são 32 dígitos hexadecimais, aleatórios em cada apresentação. Um elemento é `fileName`, `length`,
  `key`, `kind` (`0` imagem, `1` vídeo) e `folder`: a subpasta relativa à mostrada, nomes unidos por `/`, no
  máximo 128 caracteres, cada nome limpo como um nome de ficheiro. `key` são 64 dígitos hexadecimais, aleatórios em
  cada apresentação; os seus primeiros 8 bytes são a chave de ficheiro dos pedidos MediaData, por isso uma imagem é
  obtida pelos fluxos de média em partes verificadas. Um vídeo nunca é obtido assim.
- No máximo 2000 elementos, 50 por página; uma imagem tem no máximo 64 MiB. Uma página cujos números não batem certo,
  ou com um elemento que quebra uma regra, é rejeitada por inteiro.
- `show` de um participante é uma intenção (`version` e `sender` são ignorados). O anfitrião aceita no máximo 8 por
  segundo de um participante, numera a sua decisão e envia-a a todos com o remetente verificado. Um vídeo posto no ecrã
  passa a ser o filme: o anfitrião envia primeiro `media` e `state`, depois `show`. Uma imagem no ecrã põe o filme
  em pausa.
- Uma miniatura é um JPEG de no máximo 320 × 320 píxeis e 96 KiB, feito uma vez pelo anfitrião. Ele responde no
  máximo a 400 pedidos em 10 segundos por participante; o que não for um JPEG dentro dos limites chega como resposta
  vazia.
- Um participante que vê uma imagem à parte indica o seu índice em `galleryView` do seu estado, limitado pelo número
  de elementos; os outros mostram quem vê o quê.
- Quem chega recebe as páginas e o `show` atual logo a seguir a `welcome`.

## MediaData

Um pedido, 17 bytes: a operação (`01` GetChunk para uma parte, `02` GetHash só para o hash), o índice da parte (int64 BE),
a chave do ficheiro (uint64 BE, os primeiros 8 bytes do identificador rápido). A resposta a GetHash chega sem dados.

Uma resposta, 45 bytes e os dados: o estado (`0` OK, `1` fora do ficheiro, `2` outro ficheiro, `3` erro de leitura), o
índice (int64 BE), o comprimento (int32 BE), o SHA-256 dos dados (32 bytes).

O tamanho da parte é de 256 KiB por predefinição (são permitidos 64 KiB – 16 MiB). O identificador rápido =
SHA-256(comprimento int64 LE, o primeiro MiB, o MiB do meio, o último MiB).

## Relay

O fluxo transporta TURN (RFC 5766 em tramas TCP) sem alterações: o mesmo que o cliente enviaria para a porta do relé do
anfitrião, com as mesmas credenciais do `welcome`. O anfitrião só aceita um fluxo destes de um participante admitido e
fecha-o juntamente com a admissão. A atribuição é criada no endereço do anfitrião a que a ligação chegou.

Do lado do participante, os fluxos são abertos pelo `RelayTunnel`: escuta numa porta de `127.0.0.1` para a qual aponta o
`turn-server` das câmaras e, para cada ligação a ela, abre um fluxo `Relay` próprio pela rota que a sessão seguiu (um
endereço direto ou um encontro através do servidor de encontro). Se o `welcome` não tiver `relayOverSession` (um
anfitrião de uma versão anterior), o participante vai diretamente à porta do relé, como antes.

### RelayShared

Um fluxo `RelayShared` transporta de uma vez todas as ligações TURN de um participante, para que as câmaras e as vozes
precisem de uma só ligação através dos routers (através de um servidor de encontro, um só encontro) em vez de uma dúzia.
É uma sequência de tramas: o número do canal (4 bytes, big-endian), o tipo (1 byte), o comprimento da carga (2 bytes,
big-endian), a carga (no máximo 16384 bytes).

| Tipo | Enviado por | Significado |
|---|---|---|
| `1` open | participante | abre um canal; sem carga |
| `2` data | ambos | bytes do canal: o mesmo TURN em tramas TCP |
| `3` close | ambos | o canal terminou; sem carga |

O relé serve cada canal como um cliente próprio. No máximo estão abertos 16 canais ao mesmo tempo; um `open` acima disso é
respondido com `close`. Os dados para um canal que não existe são descartados; uma trama de outro tipo ou com uma carga
maior termina todo o fluxo. O participante usa este fluxo quando o `welcome` tem `relayShared` e abre um novo quando se
quebra; sem a indicação (um anfitrião de uma versão anterior), cada ligação recebe um fluxo `Relay` próprio. O preço da
partilha é que um pacote perdido retém todos os canais até ser reenviado.

## Troca de partes entre participantes

Funciona sobre o canal de dados WebRTC `golether` (SCTP sobre DTLS; as mensagens só ficam disponíveis depois de
verificado o certificado do outro lado). Todos os números são big-endian.

| Tipo | Conteúdo | Significado |
|---|---|---|
| `1` have | chave do ficheiro (8), número de intervalos (2), intervalos: início (4), comprimento (4) | que partes existem; a cada 2 s |
| `2` request | chave do ficheiro (8), número do pedido (4), índice da parte (4) | um pedido para enviar uma parte |
| `3` data | número do pedido (4), deslocamento (4), tamanho total (4), dados | uma porção de uma parte (de 16 KiB cada, por ordem) |
| `4` miss | número do pedido (4) | a parte não existe ou quem a dá está ocupado (no máximo 2 envios por participante) |

Uma parte recebida só é aceite quando o seu SHA-256 coincide com o hash do anfitrião (GetHash); caso contrário, a parte é
pedida ao anfitrião. O mesmo acontece quando a transferência para durante 5 segundos ou a parte não chegou inteira em
30 segundos: um participante que envia pouco de cada vez não consegue atrasar o filme para os outros.

## Convite

`golether://join/v1/` + base64url(JSON), no máximo 4096 caracteres:

```json
{ "token": "<22 caracteres base64url, 128 bits>", "expires": 1789000000,
  "host": "<PeerId>", "name": "You", "session": "Movie night",
  "endpoints": ["192.168.1.10:47800", "[2001:db8::5]:47800"],
  "meet": "203.0.113.7:47900", "room": "<22 caracteres base64url, 128 bits>" }
```

`token` e `expires` (segundos Unix) vêm primeiro: o resto é igual em cada convite de uma sessão, por isso um convite novo
distingue-se desde os primeiros caracteres. A ordem dos campos não significa nada para um leitor. `endpoints` contém de 1
a 16 endereços.

O mesmo convite como ligação web: `https://depthrel.github.io/Golether/join/#` + o mesmo base64url. O navegador não envia
ao servidor a parte depois do `#`; a página transforma-a em `golether://join/v1/…` e entrega-a à aplicação. A aplicação
aceita as duas formas.

Os campos `meet` e `room` são opcionais e vêm sempre juntos: o servidor de encontro do anfitrião e a sua sala nele (uma
nova para cada sessão). Com apenas um deles, ou com um mal formado, o convite é considerado danificado. As versões
antigas não leem estes campos. O protocolo do próprio servidor está descrito em
[RendezvousServer.md](RendezvousServer.md#protocolo-versão-1).

## Código de verificação

Os dois lados calculam o código por si; ele nunca viaja pela rede.

```
hash     = SHA-256(UTF-8("golether-sas-v1|" + low + "|" + high + "|" + context))
palavras = os 24 bits de hash[0..2] (big-endian), cortados em três números de 6 bits (0–63), o mais alto primeiro
número   = uint16 little-endian de hash[3..4] módulo 100
```

`low` e `high` são os dois `PeerId` por ordem ordinal, por isso o resultado não depende de quem o calcula. O contexto de
uma entrada é o token que o participante apresenta no `hello`: o token do convite, ou o `reconnectTicket` ao voltar. O
contexto de um túnel é o `offerId` da sua oferta (ver abaixo).

Um número de palavra só se transforma em palavra no ecrã, pelo pacote de idioma (`Verification.Word.00` …
`Verification.Word.63`). Cada idioma nomeia as mesmas 64 coisas pela mesma ordem, por isso o código é o mesmo em todos
os idiomas, e duas pessoas com idiomas de interface diferentes veem traduções das palavras uma da outra.

## Pacotes AmneziaWG

`golether-awg:<offer|answer>:v1:<base64url(corpo JSON)>.<base64url(assinatura)>`.
A assinatura: ECDSA P-256/SHA-256 (IEEE P1363) sobre a cadeia ASCII antes do ponto. Os espaços e as quebras de linha
dentro de um pacote são ignorados, por isso um pacote pode ser reencaminhado com quebras de linha.

O corpo de uma oferta: `offerId`, `hostName`, `hostCertificate` (DER, base64), `hostPublicKey` (AWG), `hostEndpoints[]`,
`hostAddress`, `assignedAddress`, `s1`, `s2`, `headers[4]`, `ephemeralKey` (SPKI P-256, base64), `expiresAt`.

O corpo de uma resposta: `offerId`, `participantName`, `participantCertificate`, `participantPublicKey`,
`participantEndpoints[]`, `ephemeralKey`.

PSK = base64(SHA-256(ECDH(chaves P-256 efémeras))): o segredo comum de duas chaves de uso único, passado pelo SHA-256. Um
pacote com o corpo incompleto ou com uma chave que não está na curva P-256 é considerado danificado.

Código de verificação = `VerificationCode(hostPeerId, participantPeerId, offerId)`.
