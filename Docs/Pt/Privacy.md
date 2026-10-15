# Golether: privacidade

O Golether não tem contas, nem publicidade, nem telemetria: não recolhe estatísticas sobre si nem sobre o que vê. O
filme, as vozes, as câmaras, o chat e os desenhos passam diretamente entre os computadores dos participantes,
cifrados, e nenhum servidor do projeto os vê.

Esta página enumera tudo o que o Golether guarda e cada endereço a que se liga.

## O que fica no seu computador

Tudo o que se segue fica na pasta `data` da aplicação (veja «Onde ficam os dados» no
[guia do utilizador](UserGuide.md)) e nunca sai do computador por si só:

- a chave do dispositivo: prova aos outros que é o mesmo dispositivo;
- os contactos: o nome e a impressão digital da chave de cada dispositivo cujo código comparou, se confia nele, o seu
  último endereço de rede, as suas notas e quando foi visto pela primeira e pela última vez;
- as definições, os túneis AmneziaWG com as suas chaves, os componentes descarregados;
- o registo da aplicação em `data/logs`, guardado 7 dias. Contém ligações, erros e endereços de rede dos
  participantes, nunca o conteúdo de uma sessão.

As mensagens do chat e os desenhos não são guardados em lado nenhum: quando a sessão é fechada, desaparecem. Um
relatório de diagnóstico só é guardado quando o pede; contém o registo e as definições, por isso veja-o antes de o
enviar a alguém.

## O que recebem os outros participantes

Os participantes da sua sessão veem o seu nome, o seu avatar, o seu endereço de rede (uma ligação direta precisa
dele) e a impressão digital da chave do seu dispositivo; com a câmara, o microfone ou o chat ligados, recebem também
esses. O anfitrião serve o filme aos participantes, que podem passar partes dele uns aos outros. O código de
verificação é comparado de viva voz e não é enviado para lado nenhum.

## A que se liga o Golether

| A quê | Quando | O que o outro lado fica a saber |
|---|---|---|
| O servidor de encontro do projeto (o seu endereço vem do `config.json` da página do projeto) | quando um anfitrião abre uma sessão através do servidor e quando um participante entra nessa sessão | os endereços de rede do anfitrião e do participante, a impressão digital da chave do anfitrião, a etiqueta aleatória da sala; nunca o conteúdo da sessão |
| A página do projeto `depthrel.github.io` (GitHub Pages) | `updates.json` ao iniciar (a versão mais recente; a verificação pode ser desligada nas definições); `config.json` quando um anfitrião abre uma sessão com o servidor de encontro escolhido automaticamente | o seu endereço IP, como em qualquer página web; aplica-se a declaração de privacidade do GitHub |
| GitHub e `gstreamer.freedesktop.org` | no Windows, só quando instala um componente em falta com o botão **Instalar** | o seu endereço IP |
| Os servidores STUN públicos da Google (`stun.l.google.com`) e da Cloudflare (`stun.cloudflare.com`) | só ao configurar um túnel AmneziaWG: dizem ao computador o seu endereço exterior | o seu endereço IP e a sua porta; aplicam-se as suas próprias políticas de privacidade |
| O seu router (UPnP, NAT-PMP) | quando o anfitrião pede ao router que abra uma porta | nada para além da sua rede local |

Uma sessão também funciona sem o servidor de encontro: escolha a ligação direta ou o seu próprio servidor na janela
do convite («Servidor de encontro» no [guia do utilizador](UserGuide.md)).

## A página do projeto

A página do projeto `depthrel.github.io/Golether` conta as suas visitas com o Umami Cloud. O Umami não usa cookies e,
segundo a sua própria descrição, não guarda endereços IP: regista o endereço da página (sem a parte depois de `#` e
`?`), a página de onde veio, o navegador, o sistema, o tipo de dispositivo, o tamanho do ecrã, o idioma e o país, que
deduz do seu endereço. Também se conta o clique num botão de transferência: que pacote (Windows, .deb, arquivo, macOS)
e de que lugar da página. Um navegador que pede para não ser seguido (Do Not Track) não é contado. A página de convite
(`/join/`) não tem contador nenhum, e a própria aplicação não envia nada ao Umami.

## O servidor de encontro público

O servidor de encontro do projeto apenas apresenta os dois lados um ao outro; depois falam diretamente e o servidor
deixa de participar. Mantém a lista das salas abertas em memória enquanto estão abertas. O seu registo de eventos
(ligações, salas, falhas, com endereços de rede) também vive só em memória, guarda um número limitado dos últimos
eventos e só é visível para o administrador na própria máquina do servidor. O registo do sistema do servidor anota
quando um anfitrião ocupa ou deixa uma sala, com a impressão digital curta da sua chave e o seu endereço de rede;
essas linhas ficam enquanto o sistema do servidor guardar o seu registo. Nada é vendido nem passado a ninguém.

Qualquer pessoa pode ter o seu próprio servidor de encontro: [Servidor de encontro](RendezvousServer.md).

## Perguntas

As perguntas sobre os seus dados, os problemas e as ideias dirigem-se ao responsável do projeto:
project.golether@gmail.com. O botão **Comunicar um problema** (na janela «Acerca do Golether» e na janela de um erro)
abre no seu programa de correio uma carta com a versão do Golether, o sistema e o idioma já preenchidos. Nada é
enviado antes de o próprio utilizador enviar a carta; ela chega ao responsável através do Gmail.
