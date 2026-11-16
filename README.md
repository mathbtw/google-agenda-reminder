# Agenda Reminder pour Windows

Petite app de la zone de notification qui synchronise **Google Agenda** (évènements + anniversaires) et **Google Tasks**, et affiche ses propres rappels :

- **notifications maison** (pas les notifications Windows) : en bas à droite, elles ne volent pas le focus et le mode Concentration de Windows ne les bloque pas ;
- **rappel plein écran épuré** pour ne rien rater (snooze +5 / +10 / +30 min, Entrée/Échap pour fermer) ;
- **blacklist d'applications** : si une app listée est au premier plan, le rappel est reporté, réduit en toast ou ignoré ;
- **détection automatique** des jeux plein écran, du mode présentation et des appels (`SHQueryUserNotificationState`) ;
- cache local : les rappels marchent même hors ligne.

## Heure des tâches Google

L'API Google Tasks ne renvoie que la **date** d'échéance. L'heure choisie dans Google n'est pas transmise, c'est une [limite connue de l'API](https://issuetracker.google.com/issues/170143566). L'app résout donc l'heure dans cet ordre :

1. l'heure fournie par l'API, si Google la renvoie un jour (déjà géré) ;
2. l'heure lue sur le site Google Agenda par le **module web optionnel** (voir plus bas) ;
3. **une heure écrite dans le titre ou les notes de la tâche** :
   | Écrit dans la tâche              | Rappel |
   |----------------------------------|--------|
   | `⏰ 14h30` / `@14:30` / `rappel 14h30` | 14:30 |
   | `Appeler maman à 18h`            | 18:00  |
   | `heure: 7h15`                    | 07:15  |
   | `Réunion 10:45`                  | 10:45  |
   | `Faire 2h de sport` (durée)      | ignoré, heure par défaut |
4. sinon, l'heure par défaut `DefaultTaskTime` (09:00).

Ça marche aussi quand la tâche est créée depuis le téléphone : il suffit d'ajouter `⏰ 14h30` dans les notes.

### Module web : récupérer l'heure réelle des tâches (expérimental, désactivé par défaut)

Ce module lit l'heure que tu as choisie dans Google, sans rien écrire dans la tâche. L'app ouvre Google Agenda dans un navigateur intégré invisible (Microsoft Edge WebView2, déjà présent sur Windows 10/11). Elle affiche la vue « jour » de chaque date qui a des tâches, et y lit l'heure affichée à côté de chaque tâche.

**Activation :** menu → *Heure des tâches (module web)* → *Activer*, puis *Se connecter au site Google Agenda…* (une seule fois).

- Les tâches doivent être visibles dans Google Agenda (agenda « Tâches » coché dans la liste de gauche).
- Lecture toutes les 30 min (`WebTaskTimesRefreshMinutes`). Le navigateur n'est lancé que le temps de la lecture.
- L'app est **prudente** : si l'heure est ambiguë (deux tâches au même titre le même jour, deux heures différentes…), elle ne la prend pas et revient aux règles 3 puis 4.
- En cas d'échec (Google change son site, session expirée…), les rappels continuent avec l'heure écrite ou l'heure par défaut. L'état est affiché dans le menu.

⚠️ À savoir :
- **Ce n'est pas une API officielle.** Ça peut cesser de marcher si Google modifie son site.
- La session Google du module (cookies) est enregistrée dans `%APPDATA%\AgendaReminder\webview`. Elle donne accès à **tout** le compte, et pas seulement en lecture comme l'API. Pour la révoquer : *Se déconnecter* sur le site, ou supprimer ce dossier.
- Google peut refuser la connexion dans un navigateur intégré (« Ce navigateur n'est peut-être pas sécurisé »). Dans ce cas, le module ne pourra pas fonctionner : il reste désactivé et l'app marche comme avant.

## Installation

### 1. Créer les identifiants Google (une seule fois, ~5 min)

1. Sur <https://console.cloud.google.com/>, créer un projet.
2. **API et services → Bibliothèque** : activer **Google Calendar API** et **Google Tasks API**.
3. Ouvrir **Google Auth Platform** (menu ☰ → *API et services* → *Écran de consentement OAuth*). Si on vous le demande, cliquer sur **Commencer** : nom de l'app, votre e-mail, audience **Externe**.
4. **Audience** : cliquer sur **Publier l'application** (passer en *Production*).
   En mode *Test*, Google fait expirer la connexion **tous les 7 jours**. En production, il n'y a pas besoin de faire valider l'app pour un usage perso : Google affiche juste « Application non vérifiée » à la première connexion (*Paramètres avancés → Accéder à…*).
5. **Clients → + Créer un client** → type **Application de bureau** → **Créer**.
6. Dans la fenêtre qui s'affiche, cliquer sur **Télécharger le JSON**.
   ⚠️ Google ne montre le code secret **qu'une seule fois**, à la création. Si la fenêtre est fermée : *Clients* → cliquer sur le client → **Ajouter un code secret** (ou supprimer le client et en recréer un), puis télécharger le JSON.

### 2. Lancer l'app

1. Télécharger `AgendaReminder.exe` depuis l'onglet **Actions** du dépôt (artefact `AgendaReminder-win-x64`), ou compiler (voir plus bas).
2. Lancer l'exe : le dossier `%APPDATA%\AgendaReminder` s'ouvre.
3. Y copier le JSON téléchargé en le renommant **`client_secret.json`**.
4. Clic droit sur l'icône calendrier de la zone de notification → **Synchroniser maintenant**. Le navigateur s'ouvre pour autoriser l'accès (lecture seule).
5. Optionnel : cocher **Lancer au démarrage de Windows**.

Le menu **Tester** permet de voir une notification, le plein écran, et le nom exact du processus au premier plan (pratique pour remplir la blacklist).

## Paramètres

`%APPDATA%\AgendaReminder\settings.json` est rechargé à chaud dès qu'il est enregistré (menu → *Paramètres*). Voir [`settings.example.json`](settings.example.json).

| Clé | Rôle |
|-----|------|
| `Display` | `FullScreen`, `Toast` ou `None` pour `Event`, `Birthday`, `Task` |
| `Blacklist` | `[{ "Process": "valorant.exe", "Action": "Postpone" }]`. Actions : `Postpone` (attend que l'app ne soit plus au premier plan), `ToastOnly` (petit toast au lieu du plein écran), `Silent` (rien) |
| `AutoDetectFullscreenApps` / `AutoDetectAction` | Traite les jeux plein écran, présentations et appels comme une app blacklistée |
| `MaxPostponeMinutes` | Au-delà, un rappel reporté s'affiche quand même en toast |
| `DefaultEventReminderMinutes` | Utilisé si l'évènement n'a pas de rappel « notification » dans Google (sinon ceux de Google sont repris) |
| `AlsoRemindAtEventStart` | Rappel supplémentaire à l'heure pile |
| `DefaultTaskTime`, `TaskReminderMinutes` | Heure par défaut des tâches, et rappels avant l'heure résolue |
| `EnableWebTaskTimes`, `WebTaskTimesRefreshMinutes` | Module web (heure réelle des tâches), désactivé par défaut |
| `AllDayReminderTime`, `BirthdayReminderTime` | Heure du rappel des évènements « journée entière » et des anniversaires |
| `MissedReminderGraceMinutes` | Un rappel manqué (PC éteint ou en veille) reste affiché s'il a moins de N min |
| `IgnoredCalendarIds` | Agendas à ignorer. Par défaut, ce sont les agendas cochés dans Google qui sont suivis |

À savoir : aucune fenêtre ne peut s'afficher par-dessus un jeu en **plein écran exclusif**. C'est pour ça que l'action `Postpone` est conseillée pour les jeux.

## Développement

- `src/AgendaReminder.Core` : modèles, synchro Google, résolution de l'heure des tâches, planification, règles de blacklist (multiplateforme, testé).
- `src/AgendaReminder.App` : app WPF (.NET 8) : zone de notification, toasts, plein écran, détection de l'app au premier plan.
- `tests/AgendaReminder.Core.Tests` : tests xUnit.

```bash
dotnet test tests/AgendaReminder.Core.Tests
```

```bash
dotnet publish src/AgendaReminder.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Fichiers dans `%APPDATA%\AgendaReminder` : `settings.json`, `state.json` (rappels déjà vus, snoozes), `cache.json`, `token/` (jeton OAuth), `web_task_times.json` et `webview/` (module web), `app.log`.
