# Corrections et améliorations — octobre 2026

Commit `37b9f789` sur `master` (base : DS4Windows 3.9.9 de schmaldeo), publié dans la version 4.0.0.

## 1. Connexion aux manettes (couche HID)

- **Fermeture des connexions** : depuis un remaniement fait en 2024 par l'ancien mainteneur, la connexion à la manette n'était jamais vraiment refermée (fuite de handles). En mode exclusif, la manette pouvait rester bloquée après une déconnexion. C'est corrigé.
- **Lectures avec délai d'attente** : quand le délai expirait (utilisé par les Joy-Con et la Switch Pro), la lecture restait en cours et pouvait écrire ensuite dans une zone mémoire déjà libérée. Elle est maintenant annulée proprement, et le résultat est bien signalé comme « délai dépassé » et non plus comme une erreur.
- **Optimisation des lectures** : l'ancien code créait un nouvel objet système à chaque rapport reçu (jusqu'à environ 1000 par seconde et par manette). Ces objets sont maintenant réutilisés. Mesuré sur une DualShock 4 en Bluetooth :

  | | Avant | Après |
  |---|---|---|
  | Mémoire allouée par lecture | 57 octets | 0 octet |
  | CPU par lecture | 78 µs | 26 µs |

- **Plus d'accès à une connexion déjà fermée** : les lectures de numéro de série, d'étalonnage et autres réglages vérifient d'abord que la connexion est encore ouverte.
- **Description de périphérique** : correction d'un plantage possible quand le nom renvoyé par Windows n'avait pas de caractère de fin.

## 2. Détection des manettes et branchement à chaud

- Un seul périphérique défectueux n'interrompt plus toute la recherche de manettes.
- Les périphériques ouverts pendant une recherche mais non retenus sont refermés, au lieu d'en fuir un à chaque branchement.
- **Branchement manqué** : si une manette était branchée pile au moment où la détection se terminait, elle pouvait ne jamais être vue. Corrigé.
- La détection survit maintenant à une erreur au lieu de s'arrêter définitivement.
- **Joy-Con en USB** : les boucles d'attente pendant l'initialisation pouvaient tourner à l'infini si on débranchait la manette, ce qui gelait toute la détection. Elles abandonnent maintenant au bout de 3 secondes.
- **Joy-Con et Switch Pro** : l'appli attend maintenant la bonne réponse à chaque commande envoyée. Avant, elle pouvait prendre un rapport sans rapport avec la commande, ce qui donnait parfois un mauvais étalonnage des sticks.
- Correction d'un dépassement de tableau au démarrage quand il y a plus de manettes que d'emplacements disponibles.
- L'arrêt d'une manette insiste jusqu'à ce que son thread de lecture se termine vraiment, sans risque de blocage sur lui-même.
- Le faux avertissement « disconnected due to read failure: 995 » n'apparaît plus lors d'un arrêt volontaire.

## 3. Traitement des entrées

- **Anti-rebond des boutons (debouncer)** réécrit :
  - plus de copie d'état ni de réflexion à chaque rapport (c'était un vrai coût CPU quand l'option est activée) ;
  - les rebonds sont maintenant filtrés à l'appui et au relâchement (avant, seulement à l'appui) ;
  - un changement du réglage s'applique immédiatement ;
  - correction d'une fuite mémoire : chaque manette déconnectée restait en mémoire pour toujours.

## 4. Stabilité (plantages évités)

- **Serveur UDP** : une adresse d'écoute invalide pouvait faire planter l'appli ou la bloquer définitivement au démarrage.
- **Actions spéciales** : une erreur pendant leur exécution (par exemple une manette débranchée au même moment) faisait planter toute l'appli.
- **Date de dernière vérification des mises à jour** : elle était relue avec le format de date du PC, donc jour et mois étaient inversés sur un Windows en français.

## 5. Mises à jour

- **Passage de .NET 8 à .NET 10** : le support de .NET 8 se termine le 10 novembre 2026. Il a fallu lever une ambiguïté de noms (`ContextMenu`/`MenuItem`) dans l'éditeur de profil.
- **Paquets NuGet** mis à jour : NLog 5.5.1, System.Management 10.0.13, TaskScheduler 2.12.2, WpfScreenHelper 2.1.1 et CsWin32 0.3.363. System.Memory est retiré, il est déjà inclus dans .NET.
- Les bibliothèques d'interface n'ont pas été modifiées.
- La compilation automatique sur GitHub (`release.yml`), le README et `COMPIL.md` sont passés en .NET 10.

## 6. Tests

- 4 tests échouaient déjà avant les changements : leurs résultats attendus dataient d'avant l'ajout de nouveaux réglages. Ils sont mis à jour, et l'un d'eux cachait le bug de date du point 4.
- 6 nouveaux tests couvrent le debouncer. Résultat : **16 tests sur 16 réussis**.
- **Test réel avec une DualShock 4 en Bluetooth, mode exclusif** :
  - connexion ;
  - entrées vérifiées jusqu'à la manette Xbox virtuelle (sticks, boutons, R2) ;
  - arrêt et redémarrage du service ;
  - reconnexion à chaud.

  La consommation est d'environ 0,7 % d'un cœur CPU en utilisation.

## Ce qui n'a pas été fait

- **Joy-Con, Switch Pro, DualSense et DS3 non testés** : seule une DualShock 4 était disponible.
- **Délai de 2 secondes avant la détection d'une manette branchée : non modifié**. Il avait été augmenté exprès pour la DualSense par l'ancien développeur, donc il n'a pas été touché.
