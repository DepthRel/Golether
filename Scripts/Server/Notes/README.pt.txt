Servidor de encontro do Golether {version} ({runtime})

O guia completo do administrador (Windows, Linux, macOS): ADMIN-GUIDE.pt.md junto a este ficheiro.

[win] Em resumo:
[unix] Em resumo (os comandos são executados a partir da raiz da pasta do servidor):
[win]   - Serviço do Windows: o instalador GoletherServer-<versão>-setup.exe ou os comandos da secção 4 do guia.
[win]   - Sem serviço: execute golether-server.exe na raiz da pasta do servidor (Ctrl+C para-o).
[win]   - Golether Server Monitor (golether-server-monitor.exe junto a golether-server.exe): o estado do servidor, o registo de eventos, iniciar e parar o serviço, exportar o registo, as definições.
[win]   - Definições: config\appsettings.json; reinicie o serviço depois de as alterar.
[win]   - Pastas: bin - o programa e as suas bibliotecas, config - as definições, docs - a documentação, monitor - o monitor.
[linux]   - Serviço systemd: sudo ./service/install-service.sh (instala em /opt/golether-server e ativa-o no arranque; uma atualização é o mesmo comando, as definições mantêm-se).
[linux]   - Remoção: sudo ./service/install-service.sh uninstall
[linux]   - Registo: journalctl -u golether-server -f
[macos]   - Serviço launchd: sudo ./service/install-service.sh (instala em /usr/local/golether-server e inicia-o no arranque; uma atualização é o mesmo comando, as definições mantêm-se).
[macos]   - Remoção: sudo ./service/install-service.sh uninstall
[macos]   - Registo: tail -f /Library/Logs/golether-server.log
[macos]   - Se o macOS não deixar abrir o programa: na pasta do servidor execute xattr -dr com.apple.quarantine . e codesign --force --sign - golether-server bin/golether-server
[unix]   - Sem serviço: ./golether-server (Ctrl+C para-o).
[unix]   - Golether Server Monitor (./golether-server-monitor, precisa de um ambiente de trabalho): o estado, o registo de eventos, iniciar e parar, as definições.
[unix]   - Definições: config/appsettings.json; reinicie o serviço depois de as alterar.
[unix]   - Pastas: bin - o programa e as suas bibliotecas, config - as definições, docs - a documentação, service - a instalação do serviço, monitor - o monitor.
[win]   - Abra TCP e UDP 47900 para o exterior. Verificação nesta máquina: http://127.0.0.1:47980/health
[unix]   - Abra TCP e UDP 47900 para o exterior. Verificação nesta máquina: curl http://127.0.0.1:47980/health
  - Dê aos anfitriões o endereço público do servidor e a porta, por exemplo 203.0.113.7:47900.
