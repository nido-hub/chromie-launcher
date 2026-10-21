# Chromie Launcher

Application desktop Windows pour ChromieCraft / WoW **3.3.5a**, disponible en **anglais, français et italien** (anglais par défaut, quelle que soit la langue de Windows). Projet communautaire indépendant, non affilié à ChromieCraft ou CurseForge.

## Utiliser le launcher

Ouvrir **`dist\win-x64\ChromieLauncher.exe`**, ou double-cliquer sur **`Lancer ChromieLauncher.cmd`**. Le dossier du jeu est recherché dans les dossiers parents ; sinon le sélectionner dans **Settings**. Le launcher doit rester dans son propre dossier, pas directement à côté de `Wow.exe`.

La release `0.3.0` est **compilée en Native AOT pour Windows x64**. Elle peut être lancée sans installer .NET. Télécharger le ZIP depuis les [releases GitHub](https://github.com/nido-hub/chromie-launcher/releases/latest) et conserver les DLL natives du dossier à côté de l’exécutable. Pour une compilation locale, utiliser les scripts ci-dessous.

- **My addons** : détection des dossiers et métadonnées TOC, versions inconnues conservées comme telles, modules Blizzard masqués. Ouvrir ou retirer un addon.
- **Library** : recherche, catégories, packs multi-dossiers, dépendances obligatoires, installation avec confirmation et dépôt d’origine visible.
- **Client patches** : installation du Patch Y non-HD ou HD dans `Data`, téléchargement MEGA intégré, validation du type de client et sauvegarde avant remplacement.
- **Downloads** : historique de session et accès aux sauvegardes. Annulation du téléchargement avant le remplacement des fichiers.
- **Settings / Paramètres / Impostazioni** : langue de l’application, chemin du jeu, flux de mises à jour signé, vérification au démarrage. Choisir **English**, **Français** ou **Italiano**, puis enregistrer : l’interface change sans redémarrage et le choix est conservé pour les prochains lancements. La langue peut être enregistrée avant de configurer un dossier de jeu.
- **Play ChromieCraft** : lancement de `Wow.exe` avec le bon répertoire de travail.

## Identité visuelle

L’interface reprend les codes du [site ChromieCraft](https://chromiecraft.com/en/) : fond illustré sombre, logo et emblème, navigation horizontale, titres en **Marcellus SC**, texte en **Roboto Condensed**, touches dorées et bleu glacier, boutons encadrés. Le contraste des panneaux reste adapté à la gestion des addons.

Les images et les polices sont embarquées : **aucun accès au site ni téléchargement de ressources graphiques n’est nécessaire à l’exécution**. Les deux polices sont fournies avec leur licence SIL OFL. Les visuels et marques appartiennent à leurs ayants droit ; leur réutilisation publique nécessite de vérifier les autorisations. Les sources et mentions sont dans `src/ChromieLauncher.Desktop/Assets/NOTICE.md`. Le launcher conserve sa mention de projet indépendant, non affilié.

Pour régénérer ces ressources uniquement en développement : `python tools/fetch_design_assets.py` (Python 3.11+, Pillow et fontTools). La compilation .NET utilise directement les fichiers déjà présents dans le dépôt et ne nécessite pas Python.

L’emblème ChromieCraft est également embarqué dans l’exécutable Windows (`ApplicationIcon`) et appliqué aux fenêtres Avalonia, y compris les confirmations. Le fichier `.ico` contient neuf tailles de 16 à 256 pixels pour la barre des tâches et les écrans haute densité. Si un ancien raccourci épinglé conserve l’icône générique mise en cache par Windows, le désépingler puis épingler le nouvel exécutable. L’application ne purge pas le cache d’icônes système.

Les menus, confirmations et messages de progression suivent la langue choisie. Les diagnostics techniques externes peuvent rester dans leur langue d’origine. Les noms et métadonnées des addons restent ceux de leurs sources et fichiers TOC ; le launcher ne traduit ni ne modifie le code des addons ni la langue du jeu.

## Provenance du catalogue — attention

La [page officielle ChromieCraft](https://chromiecraft.com/en/downloads/) renvoie vers [WoWGaming Addon Archive](https://wowgaming.github.io/addon-archive/). **L’archive indique elle-même être historique et déconseille son utilisation.** Le catalogue ne doit pas être présenté comme une sélection d’addons actuellement maintenus ou individuellement recommandés par ChromieCraft.

Le catalogue contient **133 packs, 247 dossiers et 7 473 fichiers** provenant de trois dépôts figés :

- **Archive WoWGaming** : révision `f23627cf6cc3a353a96cb9240940ad33532a7c40`, 131 packs et 245 dossiers. Seuls les TOC déclarant une interface WoW 3.x <= 30300 et les packs dont les dépendances requises sont disponibles sont proposés. Les scripts exécutables annexes sont exclus.
- **[Chromie Wishlist](https://github.com/nido-hub/chromie-wishlist)** **1.2.0** : révision `0f9c0a09c1bce81248cd96b46398de1c662473f7`, dossier `ChromieWishlist`. Dépendance requise : AtlasLoot, installée si elle n’est pas déjà présente.
- **[LootRolls / LootSummary](https://github.com/nido-hub/loot-rolls)** **1.1.0** : révision `48c0215a7bf50bd46468ce5b5849bf1ba84abeb4`, dossier `LootSummary` conformément au fichier TOC. Commande en jeu : `/ls`.

Les deux addons communautaires déclarent l’interface **30300** et sont disponibles dans la catégorie **ChromieCraft**. Ils ne font pas partie de l’archive historique WoWGaming. Seuls leurs fichiers d’exécution, médias et documentation sont téléchargés, pas leurs tests ni leurs métadonnées Git. Chaque source possède sa propre révision et ses propres empreintes Git. Cette vérification **ne constitue pas un test en jeu**. Les dépendances optionnelles ne sont pas installées automatiquement.

Il n’y a **aucune mise à jour silencieuse des addons manuels**, ni de comparaison inventée entre leurs numéros de version et ceux du catalogue. Le bouton **Replace…** avertit du risque de rétrogradation. Une dépendance déjà présente intégralement n’est pas remplacée implicitement. Un addon géré est à jour **par rapport à sa révision figée**, pas par rapport à tous les dépôts existants sur Internet. Le catalogue change avec une nouvelle version du launcher ; il n’est pas encore synchronisé automatiquement avec les branches distantes.

Les fichiers sont téléchargés à la révision exacte, avec vérification de leur taille et de leur empreinte Git. Aucun code d’addon n’est exécuté par le launcher. Pas de redistribution massive des sources ; les droits des auteurs restent applicables.

## Patch Y — clients non-HD et HD

L’onglet **Client patches** propose deux fichiers fournis par l’utilisateur :

| Variante | Source MEGA | Taille | Destination |
|---|---|---|---|
| Client **non-HD** | [Patch Y standard](https://mega.nz/file/nmBkjBzZ#dN1_3LnBzpF1cSfIqtMTB4im--VmG6ocRBxYFgpVg24) | 27 780 537 octets (26,49 Mio) | `<jeu>\Data\patch-y.mpq` |
| Client **HD** | [Patch Y HD](https://mega.nz/file/GqQHVD6Z#8SgtiuJfIvyEkO2MbUPBK7e0tYWabJMyz1Jft6jyH_w) | 28 645 259 octets (27,32 Mio) | `<jeu>\Data\patch-y.mpq` |

Le fichier HD distant porte le nom `patch-y - andre hd - new spells.mpq` : il est installé sous le nom canonique **`patch-y.mpq`**. Les variantes sont **mutuellement exclusives**, pas cumulables. Le launcher n’essaie pas de déduire le type HD/non-HD de tes autres fichiers : il faut choisir la bonne variante et cocher la confirmation correspondante. Un mauvais choix peut provoquer des erreurs en jeu.

- **Fermer WoW avant l’installation.** Une seconde vérification est effectuée juste avant le remplacement.
- Aucun compte ni logiciel MEGA n’est requis : téléchargement HTTPS, déchiffrement local AES-CTR et vérification du MAC MEGA, puis contrôle du SHA-256 figé et de la signature MPQ. Les clés restent locales et ne sont pas envoyées à l’API MEGA.
- SHA-256 non-HD : `d42f902107669f76b04b40e6c92d2869f18d52df00de6f7cf064cd749fe4cc17`.
- SHA-256 HD : `7535d02ee3bf6d3722f3bd970fc4b24fce41c3018ddca1f2732cb7563202a39d`.
- Un fichier MEGA modifié, incomplet, non authentifié ou soumis à une limite de transfert n’est pas installé. Une erreur ou une annulation avant application laisse le fichier existant intact.
- L’ancien `Data\patch-y.mpq` est conservé dans `<jeu>\Interface\.chromie-launcher\patch-transactions\<opération>\backup.mpq`. Pour revenir en arrière : fermer le jeu et le launcher, conserver le patch actuel ailleurs puis remettre `backup.mpq` dans `Data` sous le nom `patch-y.mpq`.
- Les transactions interrompues sont récupérées avant la prochaine installation explicite ; les fichiers déplacés lors de la récupération sont eux aussi conservés, jamais supprimés silencieusement.
- Aucun autre MPQ, fichier `WTF` ou addon n’est modifié. L’état affiché repose sur le SHA-256 du fichier déjà présent, pas sur une supposition à partir de son nom.

Ces patches ne font pas partie du logiciel distribué et ne sont pas des mises à jour officielles du launcher. La vérification d’intégrité ne constitue pas un test en jeu ni une garantie de compatibilité avec tous les clients HD.

Vérification en développement, sans installer dans un client :

```powershell
dotnet run --project tools/ChromieLauncher.Release -- verify-patches
```

Les deux téléchargements sont placés dans un cache temporaire isolé ; aucun fichier du jeu n’est remplacé.

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
3. Publier le contenu de `dist\win-x64` à la racine d’un ZIP. Pour la version 0.3.0 :

   ```powershell
   ./tools/publish.ps1 -Native -Version 0.3.0
   Compress-Archive -Path dist\win-x64\* -DestinationPath ChromieLauncher-0.3.0-win-x64.zip
   dotnet run --project tools/ChromieLauncher.Release -- sign ChromieLauncher-0.3.0-win-x64.zip https://example.org/ChromieLauncher-0.3.0-win-x64.zip 0.3.0 C:\MesCles\chromie.private.pem update.json "Notes de version"
   ```

4. Héberger le ZIP et `update.json` sur HTTPS (par exemple GitHub Releases), puis renseigner l’URL de `update.json` dans les paramètres. Utiliser une URL stable pour le manifeste (`releases/latest/download/update.json` sur GitHub).
5. Tester le parcours complet avec deux versions signées dans un dossier de test, avant une distribution publique. Aucun déploiement externe n’est effectué par ce projet.

La signature du manifeste est distincte d’Authenticode. Un certificat Windows sera nécessaire pour une distribution signée du binaire et pour réduire les avertissements SmartScreen.

## Développement et tests

- `src/ChromieLauncher.Core` : catalogue, TOC, stockage, transactions et mise à jour signée. Sérialisation JSON générée, compatible AOT.
- `src/ChromieLauncher.Desktop` : Avalonia, thème sombre, interface anglais/français/italien sans bindings par réflexion.
- `tests/ChromieLauncher.Tests` : tests isolés dans des dossiers temporaires, téléchargements simulés, aucune écriture dans les vrais addons.
- `tools/ChromieLauncher.Release` : génération des clés et signature des manifestes.
- `tools/build_catalog.py` : génération reproductible des métadonnées de l’archive et des sources définies dans `tools/community-addons.json` (Python 3.11+, Internet requis). `python tools/build_catalog.py --verify-community` vérifie en ligne les 12 fichiers communautaires et leurs empreintes sans modifier le jeu.

Test visuel non destructif :

```powershell
dist\win-x64\ChromieLauncher.exe --smoke-test artifacts\desktop.png
```

L’application s’ouvre, détecte les addons et le Patch Y en lecture seule, vérifie la langue mémorisée et l’icône de fenêtre, teste les cinq écrans, la confirmation HD/non-HD, la recherche des deux addons communautaires et la taille compacte dans les trois langues, capture son interface puis se ferme. Le test ne modifie pas la langue enregistrée. Aucun addon ou patch n’est installé automatiquement au démarrage.
