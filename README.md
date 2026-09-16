# Homelab Dashboard

[![CI](https://github.com/pierre3150/homelab-dashboard/actions/workflows/ci.yml/badge.svg)](https://github.com/pierre3150/homelab-dashboard/actions/workflows/ci.yml)

Dashboard web pour surveiller un cluster **Proxmox VE** en temps réel : état des nodes, VMs et containers LXC, CPU/RAM/uptime. Pensé pour tourner *sur* le homelab qu'il surveille, exposé publiquement derrière nginx, avec une authentification sérieuse (sessions, 2FA, anti-bot, fail2ban).

## Stack technique

- **.NET 8** / ASP.NET Core Web API + fichiers statiques (pas de build JS)
- **EF Core + SQLite** pour les comptes utilisateurs et le journal de connexions
- Client Proxmox VE via **API Token** (jamais le mot de passe root)
- **xUnit + Moq** pour les tests (client Proxmox et audit de connexion mockés, aucun appel réseau réel en CI)
- **Docker** multi-stage, image publiée sur **GitHub Container Registry** (ghcr.io)
- **GitHub Actions** : build + tests à chaque PR, build & push de l'image à chaque merge sur `main`

## Pourquoi pas Render/Vercel

Ce dashboard interroge l'API Proxmox sur le réseau local (`192.168.x.x:8006`) — un hébergeur externe n'y aurait pas accès. L'image est donc buildée en CI puis **tirée et lancée directement sur le homelab**, exposée via nginx.

## Sécurité — vue d'ensemble

| Mécanisme | Où | Détail |
|---|---|---|
| Sessions | Cookie HttpOnly + Secure + SameSite=Strict | Toutes les routes protégées par défaut (filtre global), sauf `/health` et les endpoints de login |
| 2FA | TOTP (Google Authenticator) | Secret chiffré au repos, QR code de setup, obligatoire une fois activé |
| Anti-IDOR | `/api/auth/me` etc. | Aucun identifiant utilisateur accepté depuis le client — toujours dérivé du cookie authentifié |
| Anti-bot | Honeypot (zéro config) + hCaptcha optionnel | Le honeypot rejette avant même de toucher la base |
| Rate limiting applicatif | `/api/auth/login*` | 8 requêtes/min/IP, defense in depth |
| Rate limiting nginx | `/api/auth/login` | 5 requêtes/min/IP (voir `deploy/nginx/`) |
| Ban IP | fail2ban | **3 tentatives échouées → 10 minutes de ban** (voir `deploy/fail2ban/`) |
| Journal de connexions | DB + fichier dédié | IP, OS, navigateur, succès/échec, pour chaque tentative |

## Déploiement complet sur le homelab

### 1. Token API Proxmox (pas le mot de passe root)

Proxmox → Datacenter → Permissions → API Tokens → Add. Rôle `PVEAuditor` (lecture seule) suffit pour ce dashboard.

### 2. Lancer le conteneur

```bash
mkdir -p /opt/homelab-dashboard && cd /opt/homelab-dashboard
curl -O https://raw.githubusercontent.com/pierre3150/homelab-dashboard/main/docker-compose.yml
mkdir -p data/db data/keys logs
```

Édite `docker-compose.yml` :
- `ADMIN_USERNAME` / `ADMIN_PASSWORD` : ton compte, créé automatiquement au premier démarrage (uniquement si aucun utilisateur n'existe déjà — pas d'UI de création de compte, dashboard personnel)
- `PROXMOX__*` : host, token ID, token secret
- `CAPTCHA__HCAPTCHASECRETKEY` : laisse vide pour l'instant (voir section hCaptcha plus bas)

```bash
docker compose up -d
```

Le conteneur écoute uniquement sur `127.0.0.1:8088` — il n'est **jamais** exposé directement, nginx est le seul point d'entrée public.

### 3. nginx (reverse proxy + TLS)

```bash
sudo cp deploy/nginx/proxy-common.conf /etc/nginx/snippets/
sudo cp deploy/nginx/homelab-dashboard.conf /etc/nginx/sites-available/
# edite le fichier: remplace DOMAINE.exemple.fr par ton vrai domaine
sudo ln -s /etc/nginx/sites-available/homelab-dashboard.conf /etc/nginx/sites-enabled/
sudo certbot --nginx -d DOMAINE.exemple.fr
sudo nginx -t && sudo systemctl reload nginx
```

**Important** : `proxy-common.conf` transmet `X-Forwarded-For` et `X-Forwarded-Proto` — sans ça, l'app ne voit jamais la vraie IP du client (le journal de connexions et fail2ban se tromperaient sur qui se connecte) et le cookie de session ne se pose jamais (il exige HTTPS, détecté via ce header).

### 4. fail2ban (3 tentatives, 10 min de ban)

```bash
sudo cp deploy/fail2ban/filter.d/homelab-dashboard.conf /etc/fail2ban/filter.d/
sudo cp deploy/fail2ban/jail.local /etc/fail2ban/jail.d/homelab-dashboard.local
# adapte logpath dans ce fichier si ton dossier /opt/homelab-dashboard/logs differe
sudo systemctl restart fail2ban
sudo fail2ban-client status homelab-dashboard   # verifie que la jail est active
```

Pour changer la durée du ban, modifie uniquement `bantime` dans `jail.local`.

### 5. Première connexion et activation de la 2FA

1. Va sur `https://DOMAINE.exemple.fr`, connecte-toi avec `ADMIN_USERNAME`/`ADMIN_PASSWORD`
2. Clique sur **2FA** en haut à droite
3. Scanne le QR code avec Google Authenticator
4. Entre le code à 6 chiffres généré pour confirmer — la 2FA est maintenant obligatoire à chaque connexion

### 6. hCaptcha (optionnel)

Le honeypot suffit contre la plupart des bots basiques. Pour une protection plus forte : crée un compte gratuit sur [hcaptcha.com](https://www.hcaptcha.com/), récupère ta clé secrète, mets-la dans `CAPTCHA__HCAPTCHASECRETKEY`, redémarre le conteneur. Il faudra aussi ajouter le widget hCaptcha côté frontend (`src/HomelabDashboard/wwwroot/index.html`) avec ta clé de site publique — pas fait par défaut pour ne pas dépendre d'une clé que tu n'as pas encore.

### 7. Mise à jour

```bash
cd /opt/homelab-dashboard
docker compose pull && docker compose up -d
```

### 8. Console web SSH pour chaque CT

Le bouton "Console" sur chaque CT LXC ouvre un vrai terminal shell, relayé en SSH par l'app - pas via l'API console de Proxmox (bug connu, non lié a l'auth par token API : voir https://forum.proxmox.com/threads/how-to-tell-vncwebsocket-to-reply-in-text-mode-suitable-for-xterm-js.160547/ et https://forum.proxmox.com/threads/the-api-does-not-verify-termproxy-tickets-created-by-api-token-users.91733/).

**Sur chaque CT que tu veux pouvoir gérer**, crée un utilisateur dédié `dashboard` (pas root - accès limité si la clé fuit un jour), sans mot de passe, uniquement accessible par clé :

```bash
# Depuis le shell de l'hôte Proxmox, pour chaque CTID actif :
pct exec <CTID> -- bash -c '
  id -u dashboard &>/dev/null || useradd -m -s /bin/bash dashboard
  mkdir -p /home/dashboard/.ssh
  echo "TA_CLE_PUBLIQUE_ICI" >> /home/dashboard/.ssh/authorized_keys
  chown -R dashboard:dashboard /home/dashboard/.ssh
  chmod 700 /home/dashboard/.ssh
  chmod 600 /home/dashboard/.ssh/authorized_keys
'
```

Assure-toi que `openssh-server` tourne sur le CT (`systemctl is-active ssh`; sinon `apt install -y openssh-server && systemctl enable --now ssh`).

**Sur l'hôte où tourne le dashboard**, place la clé privée dédiée (jamais ta clé perso) :
```bash
mkdir -p /opt/homelab-dashboard/ssh
# copie dashboard_console_key ici
chmod 600 /opt/homelab-dashboard/ssh/dashboard_console_key
```

Puis dans `docker-compose.yml`, mets à jour `SSH_HOSTS_MAP` avec le mapping VMID → IP de tes vrais CT (pas d'auto-découverte - les IP DHCP/agent invité ne sont pas fiables selon les templates).



```bash
dotnet restore
dotnet run --project src/HomelabDashboard
```

Sans `ADMIN_USERNAME`/`ADMIN_PASSWORD` définis, l'app démarre (utile pour `/health`) mais personne ne peut se connecter — c'est le comportement de repli volontaire d'un système d'auth qui ne doit jamais s'ouvrir silencieusement.

## Tests

```bash
dotnet test
```

Couvre : hashing de mot de passe, génération/vérification TOTP, chiffrement des secrets au repos, parsing du User-Agent, service d'audit, et un test d'intégration qui prouve explicitement le pattern anti-IDOR (`/api/auth/me?userId=999` ne renvoie jamais les données d'un autre utilisateur).

## Notes de sécurité complémentaires

- Le token Proxmox est scopable et révocable indépendamment du compte root.
- Le certificat auto-signé de Proxmox est accepté explicitement (`ServerCertificateCustomValidationCallback`) car c'est un usage LAN interne — ne pas réutiliser ce pattern pour un appel exposé publiquement.
- Le trousseau de chiffrement (`data/keys/`) protège les secrets TOTP au repos. Ne le perds pas sans en avoir conscience : ça invalide la 2FA de tous les comptes (il faudrait la reconfigurer).
- Pas de création de compte en libre-service : nouveau compte = accès direct à la base SQLite (`data/db/dashboard.db`) pour l'instant. Normal pour un dashboard personnel à un seul utilisateur.
