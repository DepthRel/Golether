# Arranque do emulador Android

Como preparar um computador com Windows para executar o cliente Android do Golether num emulador. O Android Studio não
é necessário. Os scripts deixam o dispositivo a funcionar por si mesmos: o teclado, os botões de navegação e a aceleração
gráfica funcionam logo após a instalação.

## Início rápido

São precisos o Windows 10 ou 11 (64 bits), a virtualização ativada nas definições do BIOS/UEFI e cerca de 10 GB de espaço
livre. Execute os scripts de `Scripts/Android` por esta ordem; basta um duplo clique:

| Passo | Script | Direitos de administrador | O que faz |
|---|---|---|---|
| 1 | `enable-windows-features.bat` | sim, pede-os | Ativa a Plataforma do Hipervisor do Windows. Depois, **reinicie o Windows** |
| 2 | `install-android-tools.bat` | sim, pede-os | Instala o workload .NET Android, o JDK, o Android SDK e o emulador: cerca de 6 GB, as transferências são verificadas por SHA-256 |
| 3 | `create-emulator.bat` | não | Cria o dispositivo `Golether-API36`, inicia-o e coloca um atalho no ambiente de trabalho |

Depois, o emulador inicia-se com o atalho **Android Emulator (Golether-API36)** ou com `start-emulator.bat`. Arranca do zero de cada vez: um instantâneo para o arranque rápido ocuparia 4 GB, reescritos em cada saída. Depois
de uma saída falhada, o emulador pergunta se deve enviar um relatório de falha e espera a resposta com o dispositivo
desligado; feche essa janela.

Na linha de comandos, o dispositivo desliga-se de forma limpa com `adb shell reboot -p`; `adb emu kill` conta como uma saída falhada. O dispositivo só tem câmara traseira: para experimentar a troca entre duas câmaras, inicie o emulador uma vez com `-camera-front emulated`.

Os passos 2 e 3 podem ser repetidos: o que está instalado fica, e um dispositivo existente mantém os seus dados.

## O que vai para onde

| O quê | Onde |
|---|---|
| Workload .NET Android | A pasta do .NET, `C:\Program Files\dotnet\packs`: um workload fica sempre aí |
| Microsoft OpenJDK 21 | `C:\Program Files\AndroidTools\jdk-21` |
| Android SDK, emulador, imagem do sistema | `C:\Program Files\AndroidTools\sdk` |
| Dispositivos virtuais | `%USERPROFILE%\.android\avd`: o emulador escreve aí enquanto funciona, por isso não podem estar em Program Files |

`JAVA_HOME` e `ANDROID_HOME` são definidas para todos os utilizadores. Reinicie os terminais e o Visual Studio para as
ver.

Duas variáveis mudam as pastas: `ANDROID_TOOLS_ROOT` para as ferramentas e `ANDROID_AVD_HOME` para os dispositivos. Com
`ANDROID_TOOLS_ROOT` não são precisos direitos de administrador (se o workload estiver instalado), mas `JAVA_HOME` e
`ANDROID_HOME` passam a ser definidas por si.

## As definições do dispositivo

O `avdmanager` cria um dispositivo pouco prático, por isso o `create-emulator.bat` altera-o:

| Definição | Por omissão | Definida pelo script | Porquê |
|---|---|---|---|
| `hw.keyboard` | `no` | `yes` | Caso contrário, o teclado do computador não escreve nada |
| `hw.gpu.enabled` | `no` | `yes` | Caso contrário, é o processador que desenha a imagem: o sistema fica lento e o vídeo não tem OpenGL ES |
| `hw.gpu.mode` | `auto` | `auto` | A placa gráfica quando possível; num computador sem ela, desenha o processador |
| `hw.ramSize` | `2G` | `4G` | 2 GB é pouco para o Android 16 com Google APIs |
| Navegação | gestos | três botões | Com gestos não há botões no ecrã, e os gestos são pouco práticos com o rato |

As primeiras definições são linhas do `config.ini` do dispositivo. A navegação define-se no sistema em execução e fica
lá:

```powershell
adb shell cmd overlay enable-exclusive --category com.android.internal.systemui.navbar.threebutton
```

## Trabalhar com o emulador

- Com o emulador inicia-se uma janela de consola minimizada. Fechá-la fecha o emulador.
- A partir do emulador, o computador está em `10.0.2.2`.
- Enquanto o teclado físico está ativo, o teclado no ecrã não aparece: é normal.
- O emulador não mostra a verdadeira descodificação de vídeo por hardware, nem o aquecimento, nem a velocidade de um
  telemóvel: para isso é preciso um telemóvel real ligado por USB, com a depuração USB ativada.
- O `sdkmanager` e o `adb` não estão no `PATH`. Estão em `C:\Program Files\AndroidTools\sdk`, em
  `cmdline-tools\latest\bin` e `platform-tools`. O `sdkmanager` escreve em Program Files, por isso precisa de um terminal
  executado como administrador.

## Compilar e executar o cliente

Os projetos Android não fazem parte da compilação de `Golether.slnx`: ela tem de funcionar sem o workload Android.
Compilam-se à parte e precisam dos componentes nativos em `Native/android-*` (ver `Native/README.md`):

```powershell
dotnet build Sources/Apps/Golether.Mobile.Android -t:Install
```

Com `-p:GoletherDeviceChecks=true`, o pacote inclui ainda verificações do que só se pode provar num dispositivo: dados,
chaves, TLS, libmpv, GStreamer. Iniciam-se com um extra da atividade e respondem no logcat com a etiqueta
`GOLETHER-CHECK`:

```powershell
adb shell am start -n depthrel.golether/depthrel.golether.MainActivity --ez golether.checks true --ez golether.conference true
adb logcat -s GOLETHER-CHECK
```

## Problemas

| O que vê | O que significa |
|---|---|
| `sdkmanager` não é reconhecido | Não está no `PATH`: use o caminho completo ou `install-android-tools.bat` |
| Acesso negado enquanto o `sdkmanager` instala | O terminal não foi executado como administrador |
| `WARNING: The SDK Manager CLI tool (sdkmanager) is deprecated` | Um aviso, não um erro |
| `Error: Could not load devices from ...\devices.xml` do `avdmanager` | Inofensivo: a imagem não tem esse ficheiro, o dispositivo é criado |
| O teclado não escreve nada ou o sistema fica lento | O dispositivo foi criado à mão: execute `create-emulator.bat`, mantém os dados e corrige as definições |
| O emulador está muito lento, o passo 1 foi ignorado | Execute `enable-windows-features.bat` e reinicie o Windows. Verifique com `emulator -accel-check` |
| `enable-windows-features.bat` falha | A virtualização está desativada nas definições do BIOS/UEFI |
