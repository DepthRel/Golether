# Golether: guia do utilizador

## O que é preciso

- Windows 10 ou mais recente, Linux ou macOS 14+.
- O pacote para Windows já contém os componentes de vídeo e câmaras. Se faltar algum, prima **Instalar** no cartão
  “Componentes” do ecrã inicial: a aplicação transfere-o do site oficial, verifica a soma de controlo e instala-o. Não são
  precisos direitos de administrador. No Linux e no macOS, o cartão mostra um comando pronto para o terminal: copie-o com
  o botão **Copiar**.
- Um vídeo pode ser aberto de imediato: “Abrir com → Golether” cria uma sessão e mostra esse ficheiro.
- Os túneis precisam do AmneziaWG. No Windows não é preciso instalar nada à parte: na primeira vez que abre a janela dos
  túneis, o Golether transfere o seu próprio motor (cerca de 4 MB). No Linux e no macOS precisa de `amneziawg-tools`
  juntamente com o módulo `amneziawg` / `amneziawg-go`. O próprio Golether nunca precisa de ser executado como
  administrador: o sistema pede permissão quando um túnel é ligado.

## Idioma da interface

O Golether fala inglês, alemão, francês, espanhol, português e russo. No primeiro arranque, o idioma segue o sistema
operativo: um sistema num destes idiomas recebe-o, seja qual for a região (alemão da Suíça, francês do Canadá,
português do Brasil, etc.), e qualquer outro idioma recebe o inglês. A escolha é guardada, e os arranques seguintes
usam-na em vez do idioma do sistema.

Para mudar o idioma, abra as **Definições** (o botão no canto superior direito), secção **Definições gerais**: a lista
mostra a bandeira e o nome de cada idioma. Depois de **Aplicar**, todos os textos mudam de imediato, sem reiniciar, e a
escolha é guardada. As mensagens de erro usam o mesmo idioma. Os motivos de uma recusa enviados pelo anfitrião aparecem
no seu idioma, mesmo que o anfitrião use outro.

As palavras do código de verificação também aparecem no seu idioma. Cada idioma nomeia as mesmas 64 coisas, por isso,
quando o anfitrião usa outro idioma, as palavras dele são traduções das suas: *coruja · farol · vento · 47* num ecrã é
*owl · lighthouse · wind · 47* no outro. Compare o significado das palavras e o número.

## Mostrar um filme (é o anfitrião)

1. Escreva o seu nome e o nome da sessão e prima **Criar uma sessão**. A porta do seu computador (47800 por
   predefinição) e o pedido ao router estão em **Avançado**: altere-os apenas se a porta estiver ocupada ou tiver sido
   reencaminhada à mão.
2. Prima **Mostrar** → **Ficheiro** e escolha um vídeo. O ficheiro fica consigo: os participantes recebem-no parte a parte
   enquanto veem e guardam-no apenas na memória.
3. Prima **Convidar**, copie a **ligação** e envie-a aos seus convidados como preferir. Um clique nela abre o Golether
   no computador do convidado e pergunta se quer entrar. A ligação é válida durante 15 minutos. Se a ligação não abrir a
   aplicação num convidado, envie a **chave** a partir da mesma janela: é colada à mão no Golether e é válida durante
   15 minutos ou uma hora.
   Um convite serve para todo o grupo: publique a ligação na conversa de grupo, e todos podem entrar com ela até
   expirar. Mesmo assim, é você quem deixa entrar cada convidado. A validade só limita o momento da entrada: um convidado
   que entrou fica na sessão depois de o convite expirar.
   Com “Abrir a porta no router” ativado, o Golether pede ao router que abra a porta (UPnP ou NAT-PMP), adiciona o
   endereço público ao convite e indica o resultado na lista de eventos. Se o router não o conseguir fazer ou se o
   endereço do seu fornecedor não for público, adicione um endereço público com uma porta reencaminhada à mão, ou o
   endereço dentro de um túnel AWG (janela do convite, **Avançado**).
   O mais simples é o servidor de encontro. O seletor **Modo de ligação** do ecrã inicial está em **Automaticamente** por
   predefinição: o Golether encontra sozinho o servidor do projeto, ele vai no convite, e os convidados de outras redes
   ligam-se sem portas abertas de nenhum dos lados; veja [mais abaixo](#servidor-de-encontro).
4. Quando um participante se liga, aparece uma janela com o código de verificação. Leia o código em voz alta. Se o
   participante vir o mesmo código, prima **Os códigos coincidem, deixar entrar**.
5. Reproduzir, pausar e saltar funcionam para todos os participantes.
6. As câmaras e a voz arrancam sozinhas quando entra numa sessão: o quadro de cada participante, à direita, mostra a
   câmara dele, e as vozes dos outros são misturadas num único fluxo. A câmara e o microfone escolhem-se nas
   **Definições**, secção **Câmara e microfone** (caso contrário, usam-se as predefinições do sistema). Uma câmara ou um
   microfone desligado é realmente libertado.

## Entrar (é participante)

1. Clique na ligação do convite: o navegador oferece-se para abrir o Golether, e o Golether pergunta se quer entrar na
   sessão (mostrando o nome dela e o do anfitrião). Se já estiver noutra sessão, pergunta se quer mudar. Em vez de
   clicar, pode colar a chave ou a própria ligação no campo **Convite** e premir **Ligar**.
2. Aparece um código de verificação. Compare-o de viva voz com o anfitrião. Se os códigos forem diferentes, prima
   **Terminar**: a ligação pode ter sido adulterada.
3. Se já tiver o mesmo ficheiro, prima **A minha cópia do ficheiro** e escolha-o: a reprodução será feita a partir do seu
   disco.

## Controlos

| Ação | Botão | Tecla |
|---|---|---|
| Reproduzir / pausar para todos | ▶ / ❚❚ | Espaço |
| Recuar / avançar 10 s | ◀◀ / ▶▶ | ← / → |
| Saltar | o controlo deslizante (aplica-se ao soltar) | — |
| Ecrã inteiro | ⛶ | F11, Esc |
| Reproduzir / pausar com um clique | um clique no vídeo | — |
| Ecrã inteiro com um clique | um duplo clique na área do vídeo (funciona também antes de escolher um filme) | — |
| O seu próprio volume | o ícone do altifalante à direita: ao passar por cima aparece uma escala de 0–100 %, um clique silencia e repõe, a roda muda-o | — |
| Microfone e câmara | “Microfone ligado” / “Câmara ligada” por baixo dos quadros dos participantes | — |
| Som e legendas | o ícone “CC” da barra do leitor: cada um tem as suas próprias faixas | — |
| Reação para todos | o ícone da carinha na barra do leitor | — |
| Desenhar sobre o vídeo | o ícone da caneta na barra do leitor | — |
| Conversa e eventos | os separadores **CONVERSA** e **EVENTOS** em baixo à direita; um clique no separador aberto ou na seta oculta o painel | — |
| Enviar uma mensagem | o campo em baixo no painel da conversa | Enter |
| Volume de outro participante | clique com o botão direito no quadro dele → controlo deslizante | — |
| A câmara de um participante numa janela própria | clique com o botão direito no quadro dele → **Abrir numa janela separada** | — |

Numa janela estreita, os botões “Definições”, “Convidar” e “Terminar” recolhem-se no botão **⋯** no canto superior
direito.

O volume e o silêncio só atuam do seu lado: os outros não notam nada. Uma escala em 0 silencia o som, e outro clique no
altifalante repõe o volume anterior.

### Ecrã inteiro

O cursor sobre o vídeo desaparece após três segundos parado e volta ao primeiro movimento, em qualquer modo de janela.
Sobre os quadros dos participantes e a conversa, está sempre visível.

Em ecrã inteiro, a barra de estado em baixo fica oculta e o vídeo ocupa toda a altura. A barra superior e a barra do
leitor ficam por cima do vídeo e recolhem-se três segundos depois de o cursor sair delas; a imagem não muda de tamanho.
Leve o cursor ao limite superior ou inferior do ecrã e as barras regressam e ficam enquanto o cursor estiver sobre elas.
Nada se oculta enquanto houver um menu de uma barra aberto, se estiver a arrastar o controlo deslizante ou a caneta
estiver ativa.

Se quiser as barras sempre visíveis, ative **Mostrar sempre a linha de tempo** nas **Definições**, secção
**Apresentação**: em ecrã inteiro, as duas barras ficam no seu lugar e o vídeo ajusta-se entre elas.

### Largura do painel de participantes e ordem dos quadros

Nas **Definições**, secção **Apresentação**, ative **Disposição ajustável**: aparece um limite fino entre o vídeo e os
participantes. Arraste-o para alargar o painel dos participantes; os quadros das câmaras crescem com ele. Um duplo
clique no limite repõe a largura original.

Com três participantes ou menos, os quadros formam uma coluna. Com mais participantes e um painel esticado para mais do
triplo da largura original, os quadros formam duas colunas, para caber mais gente no ecrã.

Com a mesma definição, os quadros podem ser trocados: arraste um para cima de outro. A ordem só é visível para si e dura
até a aplicação ser fechada; os novos participantes vão para o fim. Desativar a definição repõe a largura e a ordem
originais e retira o limite.

### A câmara de um participante numa janela separada

Clique com o botão direito no quadro de um participante → **Abrir numa janela separada**. A janela mostra apenas essa
câmara; pode aumentá-la ou movê-la para outro ecrã. Fechar a janela não afeta nada; quando o participante sai, ela
fecha-se sozinha.

### As suas próprias faixas de áudio e legendas

O ícone **CC** da barra do leitor abre a lista de faixas de áudio e legendas do filme. A escolha só atua do seu lado: um
vê a versão original, outro a dobragem, um terceiro lê legendas. O idioma escolhido é guardado e aplicado ao filme
seguinte que tenha uma faixa assim. **Legendas a partir de um ficheiro…** adiciona um ficheiro `.srt` ou `.ass` do seu
computador, **Som a partir de um ficheiro…** uma faixa de áudio separada (a sua própria dobragem, por exemplo). Só você
vê e ouve qualquer um deles.

### Conversa e reações

Há dois separadores em baixo à direita: **CONVERSA** e **EVENTOS**. Um clique no separador aberto (ou na seta à direita)
oculta o painel por completo e deixa o espaço para os quadros dos participantes. Enquanto a conversa está oculta, o
separador dela mostra um contador de mensagens novas. As mensagens viajam pelo mesmo canal protegido que o resto da
sessão e não ficam guardadas em lado nenhum: quando a sessão fecha, desaparecem.

Enquanto a conversa não está à vista, aparece também um «i» laranja num círculo no canto do separador dela, e
enquanto todo o painel está oculto, uma faixa laranja curta percorre devagar o contorno do botão da seta. O
separador **EVENTOS** nunca chama a atenção. Uma mensagem de outro participante é anunciada por uma breve pancada de
claquete de cinema; as suas próprias mensagens e as reações não fazem som. O som desliga-se nas **Definições**,
secção **Sons**.

O ícone da carinha na barra do leitor envia uma reação: durante alguns segundos, ela flutua sobre o vídeo para todos os
participantes, com o nome de quem a enviou. As reações estão disponíveis desde que entra numa sessão, antes de escolher
um filme. A camada das reações deixa passar os cliques: a pausa com um clique no vídeo continua a funcionar.

### A caneta: rodear algo diretamente no vídeo

O ícone da **caneta** na barra do leitor ativa o desenho. O botão fica premido e o cursor sobre o vídeo transforma-se
numa caneta. Mantenha premido o botão esquerdo do rato e mova-o: a linha segue o cursor com exatidão, e todos os
participantes a veem no mesmo momento. Quando solta o botão, a linha fica dois segundos e desvanece-se. Cada
participante tem a sua própria cor, a mesma do seu quadro, para se saber quem está a desenhar. Outro clique no ícone
solta o botão e o cursor volta ao normal.

As linhas não ficam guardadas em lado nenhum e estão presas à imagem, não à janela: se a sua janela for mais larga e a
da outra pessoa mais estreita, o sítio rodeado continua a ser a mesma parte da imagem. Enquanto a caneta está
desligada, os cliques no vídeo funcionam como de costume: pausa e ecrã inteiro.

### Mostrar as fotos e os vídeos de uma pasta (anfitrião)

**Mostrar** → **Pasta de imagens** (Ctrl+Shift+O) abre uma janela que mostra o que há numa pasta: as suas subpastas,
as suas imagens com miniaturas e os seus vídeos. Abra a pasta de que precisa e prima **Mostrar**. Por baixo, a janela
conta o que vai sair (imagens, vídeos, o tamanho das imagens) e o que fica de fora: ficheiros RAW de câmaras e imagens
com mais de 64 MB. Acompanham-na dois interruptores:

- **Com as subpastas**: os ficheiros de cada subpasta seguem-se aos da própria pasta, pasta a pasta, e a faixa indica
  o nome de cada subpasta no seu primeiro elemento.
- **Não mostrar o local da fotografia, a câmara nem os comentários das imagens** (ligado por omissão): as cópias que
  os participantes recebem perdem o local, a câmara, o autor, os comentários e as imagens extra que alguns telemóveis
  juntam depois de uma foto. Os seus próprios ficheiros não mudam. A orientação mantém-se, por isso a foto continua
  direita.

Na janela, **Anterior** e **Seguinte** funcionam como num explorador de ficheiros, também com Alt+← e Alt+→,
Retrocesso e os botões laterais do rato.

São mostrados no máximo 2000 ficheiros de cada vez. Uma única imagem escolhida com **Mostrar** → **Ficheiro** é
mostrada sozinha: nada mais da sua pasta sai, e os seus metadados ficam consigo.

Enquanto uma pasta é mostrada, há por baixo do ecrã uma faixa de miniaturas. Um clique num elemento põe-no no ecrã de
todos, como um salto no filme: qualquer participante o pode fazer, o anfitrião decide. ← e → passam ao elemento
anterior e ao seguinte; o botão de quatro quadrados abre todos os elementos numa grelha só no seu ecrã (Esc fecha-a).
Por baixo de cada elemento estão as marcas de quem o está a ver. A roda do rato desloca a faixa para o lado.

- **As imagens** são desenhadas pelo próprio Golether. A roda do rato ou as teclas + e − ampliam uma imagem à volta do
  ponteiro, arrastar move a imagem ampliada, 0 volta a mostrá-la inteira. Isso muda só o seu ecrã, e os traços da
  caneta ficam no seu lugar da imagem. As fotos HEIC, AVIF e TIFF são convertidas em JPEG no computador do anfitrião
  pelo componente de vídeo, por isso os participantes não precisam de nada para elas.
- **Abrir só para mim** (clique direito num elemento) mostra uma imagem só no seu ecrã. Uma linha em cima diz o que os
  outros veem e oferece **Voltar para todos** e **Mostrar a todos**; os outros veem a marca “À PARTE” no seu
  mosaico. Entretanto a caneta fica desligada.
- **Os vídeos** da pasta são reproduzidos como um filme: posto no ecrã, um vídeo passa a ser o filme de todos, com a
  sua linha do tempo, o seu som e as suas legendas, e a faixa mantém-se. A sua miniatura é uma imagem do vídeo.

O filme fica em pausa quando aparece uma imagem no ecrã e espera por baixo das imagens; os botões de som e de legendas
ficam inativos enquanto é mostrada uma imagem. Um vídeo da pasta posto no ecrã substitui o filme. **Fechar as
imagens** (anfitrião) termina a apresentação para todos, e o filme volta ao ponto onde foi interrompido. Escolher um
filme com **Mostrar** → **Ficheiro** também a termina.

### Esperar que todos estejam prontos

Quando prime reproduzir, o Golether verifica primeiro que todos têm o ficheiro aberto e o início carregado. Enquanto
alguém não estiver pronto, a barra do leitor mostra de quem se está à espera (“À espera de: Marina…”) e o botão passa a
“Cancelar o início”. O anfitrião não tem de esperar: **Começar sem eles** inicia o filme de imediato, e quem estiver
atrasado recupera sozinho. Se um participante continuar sem estar pronto ao fim de 45 segundos, a sessão começa na mesma.

### Continuar onde parou

O Golether lembra-se de onde parou em cada filme (neste computador, na pasta `data`). Quando o anfitrião volta a abrir o
mesmo ficheiro, aparece por cima da linha de tempo uma barra com a posição onde parou e o botão **Continuar para
todos**. Um filme visto até ao fim é esquecido.

### O que a linha de tempo mostra

O controlo deslizante só se move quando a imagem se move de facto: durante a contagem decrescente antes do início e
enquanto o leitor salta ou espera por dados, fica parado.

O preenchimento claro na linha de tempo é o que já está carregado e para onde se pode saltar sem esperar. Para o
anfitrião e para quem vê a sua própria cópia do ficheiro, toda a linha de tempo está iluminada. Os círculos com letras
são os participantes: pode ver onde está cada um.

### A ligação caiu

Se a ligação ao anfitrião se perder, o Golether volta a ligar sozinho: até oito tentativas com pausas cada vez maiores.
Não é preciso um convite novo: ao entrar recebe um bilhete válido para esta sessão. O anfitrião confirma o regresso na
mesma, como numa entrada normal. Enquanto as tentativas decorrem, uma barra no topo mostra o número da tentativa e o
botão **Sair**.

### Anfitrião: desligar o microfone ou a câmara de um participante

Clique com o botão direito no quadro de um participante e escolha **Desligar o microfone** ou **Desligar a câmara**. O
anfitrião só pode desligar: só o participante pode voltar a ligar o dispositivo, e o Golether diz-lho. Os quadros
mostram quem tem o microfone ou a câmara desligados. O seu próprio quadro não tem menu: para si, use os botões por baixo
dos quadros.

### Anfitrião: remover um participante

O mesmo menu (clique com o botão direito no quadro de um participante) tem **Remover da sessão**. O Golether pede
confirmação, e depois o participante sai de imediato da sessão e vê uma mensagem a dizer que o anfitrião o removeu. Não
é um bloqueio: pode voltar, mas só com um convite novo seu; nem os convites anteriores nem a religação automática o
deixam entrar. Os convites anteriores continuam a funcionar para os seus outros convidados: é a pessoa que é removida,
não a ligação. Abra **Convidar**, prima **Novo convite** e envie a nova ligação a essa pessoa em privado. A mesma regra
aplica-se a alguém que recusou na janela do código de verificação.

### Início, fim do filme e a barra de espaço

Quando o filme termina, para na última imagem para todos e o botão de reproduzir passa a **Ver outra vez** (uma seta
circular): premi-lo ou a barra de espaço começa o filme do início para todos.

Os botões do leitor não ficam com o foco do teclado, por isso a barra de espaço significa sempre reproduzir/pausar,
mesmo depois de clicar noutros botões.

Depois de premir reproduzir há uma curta contagem decrescente: assim todos os participantes começam juntos, mesmo com um
ping elevado.

## O que os quadros mostram

- **Moldura verde**: o participante está a falar neste momento (e você também, quando fala). Com o microfone desligado
  não há moldura.
- **Ping**: o tempo de resposta até ao anfitrião. Verde abaixo de 400 ms, amarelo até 1,5 s, vermelho acima.
- **Desvio**: quanto o participante está adiantado (+) ou atrasado (−) em relação à posição comum. Um desvio pequeno é
  corrigido com uma mudança impercetível de velocidade, um grande com um salto.
- **Buffer**: quantos segundos de vídeo já estão carregados à frente. Se alguém ficar muito para trás e esperar por
  dados, o anfitrião pausa todos automaticamente e continua quando todos estiverem prontos.

Quando várias pessoas estão a ver, os participantes passam diretamente uns aos outros as partes já carregadas do filme,
assim o anfitrião não tem de as dar a cada um. Cada parte é verificada com a soma de controlo do anfitrião. Quanto foi
recebido e dado aparece na barra de estado no fundo da janela.

## Se algo correu mal

**⋯ → Relatório de diagnóstico…** guarda um ficheiro de texto em `data/logs` (o botão **⋯** aparece no canto superior
direito quando a janela é tão estreita que os botões da barra superior já não cabem): a versão, o estado dos
componentes, informações sobre a sessão (sem o nome do filme) e as últimas linhas do registo. As chaves, os tokens e as
palavras-passe são retirados do relatório, por isso o ficheiro pode ser enviado a quem o estiver a ajudar.

Com qualquer largura da janela, o mesmo relatório guarda-se em **Definições → Acerca do Golether → Relatório de
diagnóstico…**. Ao lado, **Comunicar um problema** abre no seu programa de correio uma carta aos programadores
(project.golether@gmail.com) com a versão e o sistema já preenchidos; a janela de qualquer erro oferece o mesmo. Anexe
o relatório à carta: nada é enviado antes de a enviar.

O relatório tem uma linha para cada ligação com um participante: se está verificada, se o canal de dados está aberto e
quantas imagens saíram e entraram (`vídeo 120↑/0↓`). Mostram de imediato onde a ligação falha: a ligação não se
estabeleceu, estabeleceu-se vazia, ou a câmara não dá nada.

### Ninguém se consegue ligar: a firewall

O Windows trata uma nova ligação de rede, sobretudo um adaptador de túnel, como rede pública e rejeita as ligações de
entrada. Os participantes não conseguem então entrar, mesmo que os computadores se vejam perfeitamente. O Golether
verifica isto ao começar uma sessão e, se não houver regra, mostra no topo a barra **“A firewall não deixa entrar os
participantes”** com o botão **Permitir**. O botão pede uma vez a confirmação de um administrador e adiciona uma regra
para o programa; não aparece nenhum serviço permanente, e nunca é preciso repetir.

O mesmo à mão: “Firewall do Windows Defender” → “Permitir uma aplicação através da firewall” → encontre o Golether e
marque tanto a rede privada como a pública.

## Atualizações

Ao arrancar, o Golether pergunta à [página do projeto](https://depthrel.github.io/Golether/) se saiu uma versão nova. Se
sim, aparece no topo a barra “Está disponível uma versão nova” com o botão **Abrir a página de transferência**: o seu
navegador abre a página com as ligações para o seu sistema. Nada é transferido nem instalado sozinho: é você quem
decide. A caixa **Ignorar esta versão**, marcada antes de fechar a barra, desliga o aviso dessa versão; o Golether
avisá-lo-á da seguinte. Uma atualização não toca nos dados pessoais.

Nas **Definições**, secção **Atualizações**, pode desligar a verificação ou indicar a sua própria origem (o endereço de
um ficheiro com a lista de versões, `https://…`); só é preciso para compilações próprias.

## Servidor de encontro

Se você e os seus participantes têm endereços “cinzentos” e não é possível reencaminhar uma porta, o servidor de
encontro ajuda: um pequeno programa em qualquer máquina com IP público (sua ou de um amigo). Ele só apresenta os vossos
computadores um ao outro; a ligação entre eles é aberta diretamente. O filme, as câmaras e as vozes não passam pelo
servidor, e ele não os consegue ver.

1. Alguém instala o servidor segundo o
   [guia do administrador](../../Sources/Apps/Golether.Web.Server/ADMIN-GUIDE.pt.md) (Windows, Linux ou macOS; o mesmo
   ficheiro está em cada pacote do servidor).
2. O anfitrião escolhe o **modo de ligação** no ecrã inicial e cria uma sessão:
   - **Automaticamente** (predefinição): o Golether fica a saber o endereço do servidor do projeto a partir da página
     do projeto. Se a página não estiver acessível, usa-se o endereço da última vez; se não houver nenhum, a sessão é
     criada sem servidor e a lista de eventos indica-o;
   - **Diretamente**: sem servidor; os convidados ligam-se apenas aos endereços do seu computador;
   - **O meu próprio servidor**: aparece um campo para o endereço do servidor, por exemplo `203.0.113.7:47900`.

   Quando o servidor responde, a lista de eventos mostra “O servidor de encontro está pronto”.
3. Um participante não tem nada a configurar: o endereço do servidor vem no convite. Se os endereços diretos não
   responderem em 4 segundos, o Golether encontra-se com o anfitrião através do servidor por si só.

Não funciona quando pelo menos um dos lados está atrás de um NAT simétrico (comum em operadores móveis e em redes de
escritório): o Golether informa então que os routers não deixaram passar a ligação direta, e resta um túnel AWG ou uma
porta reencaminhada.

## Túnel AmneziaWG

Um túnel liga-se antes de se ligar à sessão: **Definições → Túneis AmneziaWG → Configurar túneis**. Durante uma sessão,
este botão não está disponível.

Um túnel é necessário quando uma ligação direta não passa ou está bloqueada. Não é preciso um servidor próprio, e no
Windows **não é preciso instalar o AmneziaWG à parte**: o Golether transfere o seu próprio motor e liga o túnel na sua
própria instância. Se já usa o AmneziaWG com os seus próprios endereços, não é afetado: são adaptadores de rede
diferentes.

Na primeira vez que a janela dos túneis abre, o motor é transferido com o botão **Instalar** nessa mesma janela (cerca
de 4 MB, sem direitos de administrador). Até estar lá, os botões “Ligar o túnel” não estão disponíveis.

**Anfitrião:**
1. Na janela dos túneis: **Sou o anfitrião → Criar uma oferta**, envie o texto ao participante.
2. Cole a resposta do participante e prima **Aceitar a resposta**. Comparem o código de viva voz.
3. **Ligar o túnel** (o Windows mostra um pedido de permissão, o macOS e o Linux pedem a palavra-passe) ou **Exportar
   .conf** para o AmneziaVPN.
4. Adicione ao convite o endereço do anfitrião dentro do túnel, por exemplo `10.77.41.1:47800` (janela do convite,
   **Avançado**).

**Participante:**
1. Na janela dos túneis: **Sou participante**, cole a oferta, **Aceitar a oferta**.
2. Envie a resposta ao anfitrião, comparem o código, **Ligar o túnel**.

**Um túnel vive enquanto o Golether estiver aberto.** Ao sair, é desligado automaticamente; o sistema pede uma vez a
permissão de um administrador. Se recusar, o túnel fica, e o Golether oferece-se para o remover no arranque seguinte.
Para o remover antes, use **Desligar o túnel** na mesma janela. Os túneis que não foram ligados pelo Golether não são
tocados.

O Golether encontra o endereço público sozinho: ao criar uma oferta e ao aceitar uma resposta, pergunta a servidores
STUN públicos como a sua porta é vista de fora e coloca o endereço no pacote. Depois, ambos os lados “batem à porta” um
do outro para que os routers deixem passar a resposta. Por isso, reencaminhar uma porta à mão costuma ser desnecessário.

Não funciona quando o router dá a cada destino a sua própria porta (NAT simétrico) ou o fornecedor o mantém atrás de um
endereço partilhado (CGNAT), nos dois lados ao mesmo tempo. Nesse caso, um dos lados precisa de um IP público ou de uma
porta reencaminhada. Os ficheiros `.conf` contêm chaves: não os publique.

## Onde ficam os dados

O Golether é autónomo: tudo o que guarda (a chave do dispositivo, a base de dados de contactos e túneis, as
configurações `.conf`, os componentes transferidos, as definições) está na pasta `data` junto ao programa. Um arquivo
descompactado não escreve nada no perfil do utilizador nem no registo; o instalador do Windows só acrescenta ao registo a
entrada de desinstalação e o tratamento das ligações de convite. Por isso:

- descompacte o Golether onde tiver permissão de escrita: em “Documentos”, no ambiente de trabalho, numa pen USB. Uma
  cópia descompactada em `Program Files` não arranca e pede-lhe para mover a pasta;
- o instalador do Windows pergunta para quem instalar. **Só para mim** (predefinição) instala em
  `%LOCALAPPDATA%\Programs\Golether`, não precisa de direitos de administrador e guarda os dados em `data` dentro da
  instalação. **Para todos** instala em `C:\Program Files\Golether`, precisa de direitos de administrador uma vez durante
  a instalação e guarda os dados de cada utilizador no seu perfil: `%LOCALAPPDATA%\Golether`. O instalador também ensina
  o sistema a abrir as ligações de convite com o Golether. Num arquivo descompactado, as ligações não se abrem sozinhas:
  use a chave (no Linux, `install-desktop-entry.sh` ativa as ligações);
- o pacote para Ubuntu e Debian (`.deb`, instalado com `sudo apt install ./Golether-<version>-linux-x64.deb`) traz
  consigo os componentes de vídeo e câmaras, põe o Golether no menu, faz o sistema abrir as ligações de convite e
  guarda os dados de cada utilizador no seu perfil: `~/.local/share/Golether`. Remove-se com
  `sudo apt remove golether`;
- mova, copie e apague a pasta `Golether` inteira: todos os dados viajam com ela;
- para atualizar uma cópia descompactada, descompacte a nova versão e mova para ela a pasta `data` da antiga;
- se os dados estavam antes em `%LOCALAPPDATA%\Golether` (compilações antigas), são movidos para `data` automaticamente
  no primeiro arranque;
- no Windows, a chave do dispositivo e os segredos dos túneis são ainda cifrados para a sua conta do Windows. Noutro
  computador ou com outro utilizador, não podem ser lidos: o programa diz isso e, depois de apagar a pasta
  `data\identity`, cria uma chave nova;
- enquanto uma pasta é mostrada, `data\gallery` guarda os ficheiros JPEG que o anfitrião faz das suas fotos HEIC,
  AVIF e TIFF e de imagens dos seus vídeos. É esvaziada quando a apresentação termina, ou no arranque seguinte se o
  programa foi fechado durante ela;
- `data\identity\device.key` é a chave do dispositivo. Se for apagada, a impressão digital do dispositivo muda e os seus
  contactos têm de voltar a comparar o código.

## Perguntas frequentes

- **“Não foi possível chegar ao anfitrião”.** Verifique se a porta está aberta na firewall do anfitrião, ou use um
  túnel.
- **“O convite é inválido ou expirou”.** A validade do convite terminou (uma ligação vive 15 minutos, uma chave até uma
  hora): peça um novo ao anfitrião.
- **“O anfitrião removeu-o” ou “O anfitrião recusou o pedido”.** Os convites anteriores já não o deixam entrar; pode
  entrar com um convite que o anfitrião crie depois disso.
- **O macOS não abre a aplicação.** A compilação não está assinada pela Apple: “Definições do Sistema → Privacidade e
  segurança → Abrir na mesma”.
- **Outro participante tem uma ligação fraca.** O quadro dele recebe a marca “eco”: a sua câmara chega-lhe com uma
  resolução mais baixa (320×180) até a ligação recuperar. O som não piora.
- **Sem câmaras.** Verifique se o componente “Câmaras e voz” está instalado (ecrã inicial) e se o Windows permite que as
  aplicações usem a câmara e o microfone (“Definições → Privacidade”). Um quadro preto costuma significar uma tampa da
  objetiva fechada.
- **A outra pessoa ouve-se a si própria.** O cancelamento de eco funciona para o som que o Golether reproduz; uns
  auscultadores eliminam o problema por completo.
