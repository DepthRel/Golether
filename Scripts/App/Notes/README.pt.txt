Golether {version} ({runtime})

[win] Iniciar: Golether.exe nesta pasta (pode criar um atalho ou afixá-lo na barra de tarefas).
[unix] Iniciar: ./Golether.sh
[linux] Menu de aplicações e ligações de convite: execute uma vez ./install-desktop-entry.sh.
Todos os dados (chaves, base de dados, túneis, componentes) ficam na pasta data junto à aplicação. Mova e apague a pasta do Golether sempre inteira.
[win] Vídeo, câmaras e voz: os componentes estão incluídos. Se faltar algum, o cartão “Componentes” do ecrã inicial instala-o.
[win] Túneis AmneziaWG: não é preciso instalar o AmneziaWG para Windows. Na primeira vez que a janela “Túneis AWG” abre, o Golether transfere o seu próprio motor (cerca de 4 MB, é necessária Internet). O Windows pede permissão de administrador quando um túnel é ligado.
[win] O Windows mostra um aviso do SmartScreen enquanto a compilação não estiver assinada.
[linux] Vídeo: instale o pacote libmpv da sua distribuição (libmpv2 / mpv-libs). Câmaras e voz: gstreamer1.0-plugins-base, -good, -bad e gstreamer1.0-nice. Túneis: amneziawg-tools e o módulo amneziawg (ou amneziawg-go).
[macos] A compilação não está assinada pela Apple: no primeiro arranque abra “Definições do Sistema → Privacidade e segurança → Abrir na mesma” ou execute: xattr -dr com.apple.quarantine Golether.app
[macos] Vídeo: brew install mpv. Câmaras e voz: brew install gstreamer.
Guia do utilizador: https://github.com/DepthRel/Golether/blob/main/Docs/Pt/UserGuide.md
