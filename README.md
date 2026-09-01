# Homelab Dashboard

[![CI](https://github.com/pierre3150/homelab-dashboard/actions/workflows/ci.yml/badge.svg)](https://github.com/pierre3150/homelab-dashboard/actions/workflows/ci.yml)

Dashboard web léger pour surveiller un cluster **Proxmox VE** en temps réel : état des nodes, VMs et containers LXC, CPU/RAM/uptime. Pensé pour tourner *sur* le homelab qu'il surveille, pas sur un hébergeur externe.

## Stack technique

- **.NET 8** / ASP.NET Core Web API + fichiers statiques (pas de build JS)
- Client Proxmox VE via **API Token** (jamais le mot de passe root)
- **xUnit + Moq** pour les tests (client Proxmox mocké, aucun appel réseau réel en CI)
- **Docker** multi-stage, image publiée sur **GitHub Container Registry** (ghcr.io)
- **GitHub Actions** : build + tests à chaque PR, build & push de l'image à chaque merge sur `main`

## Pourquoi pas Render/Vercel

Ce dashboard interroge l'API Proxmox sur le réseau local (`192.168.x.x:8006`) — un hébergeur externe n'y aurait pas accès. L'image est donc buildée en CI puis **tirée et lancée directement sur le homelab**.

## Déploiement sur le homelab

### 1. Créer un token API Proxmox (pas le mot de passe root)

Proxmox → Datacenter → Permissions → API Tokens → Add. Décoche "Privilege Separation" si tu veux hériter des droits du user, ou assigne un rôle `PVEAuditor` en lecture seule (suffisant pour ce dashboard).

### 2. Déployer

**Via Dokploy :**
- Nouvelle application → Docker Image → `ghcr.io/pierre3150/homelab-dashboard:latest`
- Port interne `8080`
- Variables d'environnement : voir `docker-compose.yml` à la racine du repo

**Via `docker compose` directement dans une LXC/VM Docker :**
```bash
curl -O https://raw.githubusercontent.com/pierre3150/homelab-dashboard/main/docker-compose.yml
# éditer les variables PROXMOX__* et DASHBOARD__APIKEY
docker compose up -d
```

Le dashboard sera accessible sur `http://<ip-du-host>:8088`.

### 3. Mise à jour

L'image `:latest` est republiée à chaque merge sur `main`. Pour mettre à jour :
```bash
docker compose pull && docker compose up -d
```
(Ou active le webhook Dokploy correspondant pour du vrai continuous deployment local.)

## Développement local

```bash
dotnet restore
dotnet run --project src/HomelabDashboard
```

## Tests

```bash
dotnet test
```

## Sécurité

- Le token Proxmox est scopable et révocable indépendamment du compte root.
- Le dashboard exige un header `X-Api-Key` sur toutes les routes `/api/*` (le `/health` reste ouvert pour les probes Docker/Dokploy).
- Le certificat auto-signé de Proxmox est accepté explicitement (`ServerCertificateCustomValidationCallback`) car c'est un usage LAN interne — ne pas réutiliser ce pattern pour un appel exposé publiquement.
