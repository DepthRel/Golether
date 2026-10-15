Serveur de rendez-vous Golether {version} ({runtime})

Le guide complet de l'administrateur (Windows, Linux, macOS) : ADMIN-GUIDE.fr.md à côté de ce fichier.

[win] En bref :
[unix] En bref (les commandes se lancent depuis la racine du dossier du serveur) :
[win]   - Service Windows : le programme d'installation GoletherServer-<version>-setup.exe ou les commandes de la section 4 du guide.
[win]   - Sans service : lancez golether-server.exe à la racine du dossier du serveur (Ctrl+C l'arrête).
[win]   - Golether Server Monitor (golether-server-monitor.exe à côté de golether-server.exe) : l'état du serveur, le journal des événements, le démarrage et l'arrêt du service, l'export du journal, les réglages.
[win]   - Réglages : config\appsettings.json ; redémarrez le service après les avoir modifiés.
[win]   - Dossiers : bin - le programme et ses bibliothèques, config - les réglages, docs - la documentation, monitor - le moniteur.
[linux]   - Service systemd : sudo ./service/install-service.sh (installe dans /opt/golether-server et l'active au démarrage ; une mise à jour se fait avec la même commande, les réglages sont conservés).
[linux]   - Suppression : sudo ./service/install-service.sh uninstall
[linux]   - Journal : journalctl -u golether-server -f
[macos]   - Service launchd : sudo ./service/install-service.sh (installe dans /usr/local/golether-server et le lance au démarrage ; une mise à jour se fait avec la même commande, les réglages sont conservés).
[macos]   - Suppression : sudo ./service/install-service.sh uninstall
[macos]   - Journal : tail -f /Library/Logs/golether-server.log
[macos]   - Si macOS refuse d'ouvrir le programme : dans le dossier du serveur, lancez xattr -dr com.apple.quarantine . et codesign --force --sign - golether-server bin/golether-server
[unix]   - Sans service : ./golether-server (Ctrl+C l'arrête).
[unix]   - Golether Server Monitor (./golether-server-monitor, nécessite un bureau) : l'état, le journal des événements, le démarrage et l'arrêt, les réglages.
[unix]   - Réglages : config/appsettings.json ; redémarrez le service après les avoir modifiés.
[unix]   - Dossiers : bin - le programme et ses bibliothèques, config - les réglages, docs - la documentation, service - l'installation du service, monitor - le moniteur.
[win]   - Ouvrez TCP et UDP 47900 vers l'extérieur. Vérification sur cette machine : http://127.0.0.1:47980/health
[unix]   - Ouvrez TCP et UDP 47900 vers l'extérieur. Vérification sur cette machine : curl http://127.0.0.1:47980/health
  - Donnez aux hôtes l'adresse publique du serveur et le port, par exemple 203.0.113.7:47900.
