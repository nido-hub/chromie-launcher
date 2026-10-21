# Chromie Launcher

Application desktop Windows en français pour ChromieCraft / WoW **3.3.5a**. Projet communautaire indépendant, non affilié à ChromieCraft ou CurseForge.

## Utiliser la première version

Ouvrir **`dist\win-x64\ChromieLauncher.exe`**, ou double-cliquer sur **`Lancer ChromieLauncher.cmd`**. Le dossier du jeu est recherché dans les dossiers parents ; sinon le sélectionner dans **Paramètres**. Le launcher doit rester dans son propre dossier, pas directement à côté de `Wow.exe`.

La version `0.2.0` présente dans `dist\win-x64` a été **compilée en Native AOT pour Windows x64**. Elle peut être lancée sans installer .NET. Conserver les DLL natives du dossier à côté de l’exécutable.

- **Mes addons** : détection des dossiers et métadonnées TOC, versions inconnues conservées comme telles, modules Blizzard masqués. Ouvrir ou retirer un addon.
- **Bibliothèque** : recherche, catégories, packs multi-dossiers, dépendances obligatoires, installation avec confirmation.
- **Téléchargements** : historique de session et accès aux sauvegardes. Annulation du téléchargement avant le remplacement des fichiers.
- **Paramètres** : chemin du jeu, flux de mises à jour signé, vérification au démarrage.
- **Jouer** : lancement de `Wow.exe` avec le bon répertoire de travail.

## Identité visuelle

L’interface reprend les codes du [site ChromieCraft](https://chromiecraft.com/en/) : fond illustré sombre, logo et emblème, navigation horizontale, titres en **Marcellus SC**, texte en **Roboto Condensed**, touches dorées et bleu glacier, boutons encadrés. Le contraste des panneaux reste adapté à la gestion des addons.

Les images et les polices sont embarquées : **aucun accès au site ni téléchargement de ressources graphiques n’est nécessaire à l’exécution**. Les deux polices sont fournies avec leur licence SIL OFL. Les visuels et marques appartiennent à leurs ayants droit ; leur réutilisation publique nécessite de vérifier les autorisations. Les sources et mentions sont dans `src/ChromieLauncher.Desktop/Assets/NOTICE.md`. Le launcher conserve sa mention de projet indépendant, non affilié.

Pour régénérer ces ressources uniquement en développement : `python tools/fetch_design_assets.py` (Python 3.11+, Pillow et fontTools). La compilation .NET utilise directement les fichiers déjà présents dans le dépôt et ne nécessite pas Python.

## Provenance du catalogue — attention

La [page officielle ChromieCraft](https://chromiecraft.com/en/downloads/) renvoie vers [WoWGaming Addon Archive](https://wowgaming.github.io/addon-archive/). **L’archive indique elle-même être historique et déconseille son utilisation.** Le catalogue ne doit pas être présenté comme une sélection d’addons actuellement maintenus ou individuellement recommandés par ChromieCraft.

Cette version fige la révision `f23627cf6cc3a353a96cb9240940ad33532a7c40` : **131 packs, 245 dossiers, 7 461 fichiers**. Seuls les TOC déclarant une interface WoW 3.x <= 30300 et les packs dont les dépendances requises sont disponibles sont proposés. Les scripts exécutables annexes de l’archive sont exclus. Cette vérification **ne constitue pas un test en jeu**. Les dépendances optionnelles ne sont pas installées automatiquement.

Il n’y a **aucune mise à jour silencieuse des addons manuels**, ni de comparaison inventée entre leurs numéros de version et ceux de l’archive. Le bouton « Remplacer » avertit du risque de rétrogradation. Une dépendance déjà présente intégralement n’est pas remplacée implicitement. Un addon géré est à jour **par rapport à la révision du catalogue**, pas par rapport à tous les dépôts existants sur Internet. Le catalogue change avec une nouvelle version du launcher ; il n’est pas encore synchronisé automatiquement avec des sources maintenues.

Les fichiers sont téléchargés à la révision exacte, avec vérification de leur taille et de leur empreinte Git. Aucun code d’addon n’est exécuté par le launcher. Pas de redistribution massive des sources ; les droits des auteurs restent applicables.

## Protection des fichiers

- Fermer WoW avant une installation ou une désinstallation.
- Sauvegardes : `<jeu>\Interface\.chromie-launcher\transactions\<opération>\backup`.
- Les désinstallations déplacent les dossiers dans ces sauvegardes. **`WTF` n’est jamais supprimé ou modifié.**
- Récupération d’une transaction interrompue avant la prochaine modification explicite du jeu.
- État, cache et journaux : `%LOCALAPPDATA%\ChromieLauncher`. État séparé par dossier de jeu.
- Les jonctions/liens symboliques, chemins sortant des dossiers autorisés et fichiers exécutables d’addons sont refusés.
- La restauration manuelle consiste à fermer le jeu et le launcher puis remettre les dossiers du `backup` dans `Interface\AddOns`. Pour rétablir exactement le suivi de l’époque, le journal `journal.json` contient l’ancien état. L’interface de restauration automatique n’est pas encore proposée.

## Compiler

Prérequis : **SDK .NET 10**. Aucune installation du runtime n’est requise sur le PC utilisateur pour une publication autonome.

```powershell
dotnet build src/ChromieLauncher.Desktop/ChromieLauncher.Desktop.csproj
dotnet test tests/ChromieLauncher.Tests/ChromieLauncher.Tests.csproj
./tools/publish.ps1
./tools/package.ps1
```

Le script de publication lit la version dans `Directory.Build.props` sauf si `-Version` est fourni. `package.ps1` prépare le ZIP et son SHA-256 dans `artifacts`, avec les mentions des ressources graphiques et les licences des polices. Un tag `vMAJOR.MINOR.PATCH` correspondant à la version du projet déclenche le workflow `.github/workflows/release.yml` : tests, compilation Native AOT, contrôle desktop, puis publication de la release GitHub.

### Native AOT

```powershell
./tools/publish.ps1 -Native
```

Sur Windows, Native AOT nécessite **Visual Studio 2022 Build Tools / Desktop development with C++**, MSVC et le Windows SDK. Le workflow `.github/workflows/build.yml` compile en AOT sur un runner Windows équipé et exécute les tests. La publication autonome sans `-Native` sert de version de développement quand la chaîne C++ n’est pas disponible ; elle **n’est pas une compilation Native AOT**.

Avalonia reste une interface native desktop, sans WebView. La publication comporte plusieurs fichiers : l’exécutable et les dépendances natives de rendu doivent être distribués ensemble.

## Mises à jour intégrées

L’utilisateur ne lance ni n’installe aucun updater séparé : vérification dans l’application, téléchargement, puis bouton **Redémarrer pour mettre à jour**. Le même exécutable se copie temporairement et se lance avec `--apply-update` pour effectuer le remplacement après la fermeture du processus principal. Cette phase est interne et ne présente pas une seconde interface.

Le flux contient un manifeste **signé RSA / SHA-256**, avec version, URL HTTPS et empreinte SHA-256 du ZIP. La clé publique est intégrée à la compilation. Un manifeste non signé, une empreinte incorrecte ou une ancienne version sont refusés. Extraction avec contrôle des chemins, sauvegarde des fichiers remplacés et retour arrière sur erreur de remplacement. Les sauvegardes du logiciel restent dans `%LOCALAPPDATA%\ChromieLauncher\updates`. Un échec est détaillé dans `update-error.log`. Une coupure de courant pendant le remplacement peut nécessiter une restauration manuelle ; ce premier updater n’est pas un installateur transactionnel système.

**La première version ne contient volontairement ni clé d’éditeur factice ni fausse URL de production.** Pour rendre l’auto-update réellement distribuable :

1. Créer et conserver une clé privée **hors du dépôt** :

   ```powershell
   dotnet run --project tools/ChromieLauncher.Release -- keygen C:\MesCles\chromie.private.pem C:\MesCles\chromie.public.pem
   ```

2. Copier **uniquement la clé publique** dans `src/ChromieLauncher.Core/Data/update-public-key.pem`, puis compiler. Cette même clé doit être incluse dans toutes les releases. Ne jamais publier la clé privée.
3. Publier le contenu de `dist\win-x64` à la racine d’un ZIP. Pour la version 0.2.0 :

   ```powershell
   ./tools/publish.ps1 -Native -Version 0.2.0
   Compress-Archive -Path dist\win-x64\* -DestinationPath ChromieLauncher-0.2.0-win-x64.zip
   dotnet run --project tools/ChromieLauncher.Release -- sign ChromieLauncher-0.2.0-win-x64.zip https://example.org/ChromieLauncher-0.2.0-win-x64.zip 0.2.0 C:\MesCles\chromie.private.pem update.json "Notes de version"
   ```

4. Héberger le ZIP et `update.json` sur HTTPS (par exemple GitHub Releases), puis renseigner l’URL de `update.json` dans les paramètres. Utiliser une URL stable pour le manifeste (`releases/latest/download/update.json` sur GitHub).
5. Tester le parcours complet avec deux versions signées dans un dossier de test, avant une distribution publique. Aucun déploiement externe n’est effectué par ce projet.

La signature du manifeste est distincte d’Authenticode. Un certificat Windows sera nécessaire pour une distribution signée du binaire et pour réduire les avertissements SmartScreen.

## Développement et tests

- `src/ChromieLauncher.Core` : catalogue, TOC, stockage, transactions et mise à jour signée. Sérialisation JSON générée, compatible AOT.
- `src/ChromieLauncher.Desktop` : Avalonia, thème sombre, interface française sans bindings par réflexion.
- `tests/ChromieLauncher.Tests` : tests isolés dans des dossiers temporaires, téléchargements simulés, aucune écriture dans les vrais addons.
- `tools/ChromieLauncher.Release` : génération des clés et signature des manifestes.
- `tools/build_catalog.py` : génération reproductible des métadonnées depuis la révision figée de l’archive (Python 3.11+, Internet requis).

Test visuel non destructif :

```powershell
dist\win-x64\ChromieLauncher.exe --smoke-test artifacts\desktop.png
```

L’application s’ouvre, détecte les addons en lecture seule, capture son interface puis se ferme. Aucun addon n’est installé automatiquement au démarrage.
