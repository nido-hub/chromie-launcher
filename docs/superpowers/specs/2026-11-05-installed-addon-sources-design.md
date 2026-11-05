# Sources des addons installés — 0.4.3

Objectif approuvé : comparer les addons installés au catalogue, ajouter les sources manquantes vérifiées, gérer les packs multi-dossiers et produire la version 0.4.3.

Approches : de simples liens ne permettent pas les installations/mises à jour ; l'approche retenue étend les sources déclaratives et les mécanismes existants, sans nouveau gestionnaire.

Contraintes : ne modifier ni Interface/AddOns ni WTF ; conserver les protections, les révisions figées, empreintes, dépendances et refus des rétrogradations. Ne pas inventer de provenance. Documenter les ressources sans TOC, dossiers client protégés et variantes locales non reproductibles.

Périmètre : manifeste des sources, générateur/catalogue, découverte des mises à jour, tests, mentions de provenance, version et notes de release. Compiler/tester et publier par le workflow signé existant si l'accès GitHub le permet. Aucun changement graphique ni installation d'addon dans le jeu.
