# Couverture des addons installés — 0.4.3

Inventaire en lecture seule du 5 novembre 2026 : **88 dossiers hors Blizzard**, dont **83 rattachés au catalogue**, regroupés en 33 packs. Les deux dossiers DBM-AQ20/DBM-AQ40 sont sans TOC local ; leur source existe mais ils ne sont pas détectés comme addons installés. Parmi les 84 dossiers avec TOC hors APIDocumentation, **81 sont couverts et 3 restent manuels**. Aucun fichier du jeu n’a été modifié.

Le catalogue complet contient 150 packs, 278 dossiers, 10 657 fichiers et 19 dépôts distincts. Les 20 entrées hors archive sont figées dans [`../tools/community-addons.json`](../tools/community-addons.json) ; leurs fichiers et empreintes sont dans le catalogue embarqué. Les sources historiques ne sont pas présentées comme maintenues ni identiques aux modifications locales.

## Sources ajoutées ou actualisées

| Addon / pack local | Dossiers | Source GitHub |
|---|---:|---|
| ChromieWishlist (actualisé en 1.2.2) | 1 | [nido-hub/chromie-wishlist](https://github.com/nido-hub/chromie-wishlist) |
| LootSummary (déjà présent) | 1 | [nido-hub/loot-rolls](https://github.com/nido-hub/loot-rolls) |
| ChromieCrafting | 1 | [nido-hub/chromie-crafting](https://github.com/nido-hub/chromie-crafting) |
| ChromiePlanner | 1 | [nido-hub/chromie-planner](https://github.com/nido-hub/chromie-planner) |
| WeakAuras, Archive, ModelPaths, Options, StopMotion, Templates | 6 | [NoM0Re/WeakAuras-WotLK](https://github.com/NoM0Re/WeakAuras-WotLK) |
| Grid2, Grid2Options, Grid2AoeHeals | 3 | [bkader/Grid2-WoTLK](https://github.com/bkader/Grid2-WoTLK) |
| Kui_Nameplates, Kui_Nameplates_Auras | 2 | [bkader/KuiNameplates-WoTLK](https://github.com/bkader/KuiNameplates-WoTLK) |
| MRT | 1 | [ExoJdi/Method-Raid-Tools-3.3.5a](https://github.com/ExoJdi/Method-Raid-Tools-3.3.5a) |
| DBM (remplace l’ancienne source du catalogue) | 34 | [5Buttons/DBM-CC](https://github.com/5Buttons/DBM-CC) |
| AwesomeCVar, Flash | 2 | [srazdokunebil/awesome_wotlk](https://github.com/srazdokunebil/awesome_wotlk), uniquement `addons/` |
| PlateCastBar | 1 | [Macumbafeh/PlateCastBar_awesome_wotlk](https://github.com/Macumbafeh/PlateCastBar_awesome_wotlk) |
| Bistooltip | 1 | [disruption01/BiS-Tooltip_335a_backport](https://github.com/disruption01/BiS-Tooltip_335a_backport) |
| Auctionator | 1 | [jejkas/Auctionator](https://github.com/jejkas/Auctionator), version historique 2.6.3 |
| Cheese | 1 | [hiroshi-richi/Cheese-Fixed](https://github.com/hiroshi-richi/Cheese-Fixed) |
| MinimapButtonFrame, MinimapButtonFrame_SkinPack | 2 | [Xurkon/MinimapButtonFrame](https://github.com/Xurkon/MinimapButtonFrame), deux entrées indépendantes |
| Binder | 1 | [btrvarun/wotlk-addons](https://github.com/btrvarun/wotlk-addons), uniquement `Binder/` |
| NiceDamage | 1 | [Evolvee/EvolvePWPUI-335a](https://github.com/Evolvee/EvolvePWPUI-335a), uniquement `Interface/AddOns/NiceDamage/` |
| MySlot | 1 | [Tarkusx/Myslot3.3.5a](https://github.com/Tarkusx/Myslot3.3.5a), version historique 3.3 |

Toutes ces sources utilisent leur branche par défaut pour les vérifications ultérieures. Les révisions embarquées sont des commits exacts, pas des URLs de branches flottantes. Les fichiers racine, sous-dossiers et packs gardent leurs noms de dossiers. Les tests et métadonnées Git sont exclus. Aucun patch client, DLL ou exécutable de ces dépôts n’est installé.

**Adaptation de chemin MBF :** le dépôt nomme une bibliothèque `libs/libdatabroker-1-1/`, alors que son XML charge `libs/LibDataBroker-1.1/`. Le manifeste déclare ce renommage du dossier de destination ; les octets, chemins de téléchargement et empreintes Git restent inchangés. La revue des 122 XML des sources a identifié cette bibliothèque obligatoire et un fichier de traduction `Locale/Locale-esMX.lua` absent. Ce dernier défaut mineur de traduction reste documenté, sans inventer de fichier localisé.

## Déjà présents dans l’archive conservée

Source commune : [wowgaming/addon-archive](https://github.com/wowgaming/addon-archive), révision `f23627cf6cc3a353a96cb9240940ad33532a7c40`.

`_NPCScan` ; `AtlasLoot` et ses cinq modules ; `AtlasLootFu` ; `DataStore` et `DataStore_Inventory` ; `Decursive` ; `GatherMate` et `GatherMate_Data` ; `GearScoreLite` ; `MoveAnything` ; `Postal` ; `Prat-3.0`, `Prat-3.0_HighCPUUsageModules`, `Prat-3.0_Libraries` ; `Skada` ; `WIM` ; `WoWEquip`.

Cela confirme leur présence par dossier, pas l’identité de leurs variantes locales (par exemple Skada Revisited). Ces sources n’ont pas été changées vers une autre variante arbitraire.

## Exceptions explicites

| Dossier | Source / raison de ne pas l’installer |
|---|---|
| `APIDocumentation` | Présent dans [WeakAuras-WotLK](https://github.com/NoM0Re/WeakAuras-WotLK/tree/master/APIDocumentation), mais dossier client explicitement protégé par le launcher ; protection conservée. |
| `boss_heads` | Images et fichier PSD, sans TOC : ressources, pas un addon autonome. |
| `RaidIDsChromie` | Addon local 3.7 ; aucun dépôt public correspondant retrouvé. L’archive locale ne fournit pas une source de mise à jour distante vérifiable. |
| `DBM-CountPack-HotS` | [Source officielle](https://github.com/DeadlyBossMods/DBM-CountPack-HotS) identifiée, mais son TOC actuel ne déclare pas 30300 ; le TOC local a été adapté. Pas de contournement de compatibilité. |
| `DBM-WorldEvents` | [Source DBM-CC](https://github.com/5Buttons/DBM-CC/tree/main/DBM-WorldEvents) identifiée, mais le TOC figé référence `WarmaneEvents/TowerDefense.lua`, absent du dépôt. Module exclu du nouveau pack au lieu d’offrir une installation incomplète. |

## Préservation des variantes locales

- **MySlot** local 3.3.3 contient des fonctions de profils et des tests absents de la source historique 3.3. Aucun remplacement automatique ; « Remplacer » retirerait ces modifications de code (les fichiers seraient sauvegardés).
- **NiceDamage** local déclare 40000 ; la variante du catalogue déclare 30300 et utilise d’autres polices/version. Pas de mise à jour automatique déduite de ces numéros.
- **AwesomeCVar** local 3.0 est plus récent que la source figée 2.5 : pas de rétrogradation automatique.
- **DBM** local est incomplet (AQ20/AQ40 sans TOC). « Tout mettre à jour » ne l’adopte pas. Une installation explicite du pack sauvegarde/remplace ses 34 dossiers et laisse `DBM-WorldEvents` et les autres dossiers hors pack intacts.
- Les révisions de l’ancien DBM d’archive ne sont pas comparables à celles du nouveau dépôt : une ancienne installation gérée peut nécessiter un remplacement explicite, jamais une migration silencieuse.
- Les versions inconnues, non numériques ou égales restent conservatrices, comme avant. Une source ajoutée ne garantit pas qu’un bouton « Mettre à jour » apparaisse pour chaque copie manuelle.

Les TOC et empreintes sont vérifiés sans exécuter de code d’addon. **Aucun test en jeu n’est effectué.**
