# Compiler, installer et maintenir ma version de DS4Windows

Ce dépôt est mon fork de [schmaldeo/DS4Windows](https://github.com/schmaldeo/DS4Windows)
(version 3.9.9, la dernière : schmaldeo a arrêté le projet en mars 2026).

Toutes les commandes se lancent depuis la racine du dépôt : `C:\serveurs\DS4Windows`.

---

## 0. Prérequis

- **SDK .NET 8** (déjà installé sur mon PC). Vérifier avec :
  ```bash
  dotnet --list-sdks
  ```
  Il faut une ligne `8.0.xxx`. Sinon, télécharger le SDK .NET 8 sur https://dotnet.microsoft.com/download/dotnet/8.0
- **Git**.
- Pas besoin de Visual Studio (mais on peut ouvrir `DS4WindowsWPF.sln` avec si on veut).

---

## 1. Compiler pour tester (pendant le développement)

```bash
dotnet build DS4Windows/DS4WinWPF.csproj -c Release -p:Platform=x64
```

- Durée : environ 15 secondes.
- Résultat : `DS4Windows\bin\x64\Release\net8.0-windows\DS4Windows.exe`
- Les « warnings » (avertissements) sont normaux, seules les **erreurs** comptent.
- Le dossier `bin\` est ignoré par Git, il ne sera jamais commité.

> Fermer DS4Windows avant de recompiler, sinon les fichiers sont verrouillés.

---

## 2. Générer la version « installée » (celle que j'utilise au quotidien)

```bash
dotnet publish DS4Windows/DS4WinWPF.csproj -c Release -r win-x64 --self-contained false -p:Platform=x64 -o C:/Programmes/DS4Windows
```

- Crée (ou met à jour) le dossier `C:\Programmes\DS4Windows` (~12 Mo) avec `DS4Windows.exe`.
- C'est l'équivalent du zip officiel.
- **Fermer DS4Windows avant** (y compris l'icône près de l'horloge).
- Variante pour un PC sans .NET installé : remplacer `--self-contained false` par
  `--self-contained true` (le dossier fait alors ~150 Mo mais marche partout).

---

## 3. Première installation / passage depuis une autre version

1. Fermer l'ancien DS4Windows (et le retirer du démarrage de Windows s'il y était).
2. Lancer `C:\Programmes\DS4Windows\DS4Windows.exe`.
3. **Profils et réglages** :
   - S'ils étaient dans `%APPDATA%\DS4Windows`, ils sont retrouvés automatiquement.
   - Si l'ancienne version était en mode portable (fichiers à côté de l'ancien `.exe`),
     copier dans le nouveau dossier : `Profiles.xml`, `Auto Profiles.xml`, `Actions.xml`
     et le dossier `Profiles\`.
4. **Pilotes** : ViGEmBus (obligatoire) et HidHide (conseillé, évite les doubles entrées
   dans les jeux). S'ils manquent : onglet Paramètres → lien « Installer les pilotes »
   (« Controller/Driver Setup » en anglais).
   Des installeurs sont aussi dans le dossier `extras\` du dépôt.
5. **Démarrage auto** : onglet Paramètres → cocher « Lancer au démarrage ».
6. Faire un raccourci vers `C:\Programmes\DS4Windows\DS4Windows.exe` (bureau / menu Démarrer).

> La vérification de mises à jour interroge le dépôt de schmaldeo : comme il est arrêté
> en 3.9.9, l'appli dira toujours « à jour » et n'écrasera jamais ma version.

---

## 4. Le cycle de travail habituel

```bash
# 1. Modifier le code, puis tester :
dotnet build DS4Windows/DS4WinWPF.csproj -c Release -p:Platform=x64

# 2. Enregistrer mes modifications dans Git :
git add -A
git commit -m "Description de ma modification"
git push origin master

# 3. Mettre à jour ma version installée :
dotnet publish DS4Windows/DS4WinWPF.csproj -c Release -r win-x64 --self-contained false -p:Platform=x64 -o C:/Programmes/DS4Windows
```

> La branche principale s'appelle **`master`** (pas `main`).

---

## 5. Git : mon fork et le projet d'origine

Deux dépôts distants (« remotes ») sont configurés :

| Nom        | Adresse                                      | Rôle                                   |
|------------|----------------------------------------------|----------------------------------------|
| `origin`   | https://github.com/Baba0bab/DS4Windows       | Mon fork : je pousse mes modifs ici    |
| `upstream` | https://github.com/schmaldeo/DS4Windows.git  | Projet d'origine : je récupère ses modifs |

Vérifier avec `git remote -v`. Si `upstream` manque (ex. après un nouveau clone) :

```bash
git remote add upstream https://github.com/schmaldeo/DS4Windows.git
```

### Récupérer les nouveautés du projet d'origine

```bash
git fetch upstream
git merge upstream/master
git push origin master
```

- Si Git signale un **conflit** (on a modifié les mêmes lignes) : ouvrir les fichiers
  indiqués, garder la bonne version entre les marqueurs `<<<<<<<`, `=======`, `>>>>>>>`,
  puis :
  ```bash
  git add -A
  git commit
  ```
- Alternative sans terminal : bouton **« Sync fork »** sur la page GitHub de mon fork,
  puis `git pull` en local.

### Si quelqu'un reprend le projet un jour

Remplacer l'adresse d'`upstream` par celle du nouveau fork (qui doit être un fork de
schmaldeo, pour garder un historique commun) :

```bash
git remote set-url upstream https://github.com/AUTEUR/DS4Windows.git
```

### Conseils pour éviter les conflits

- Faire des modifications ciblées, ne pas reformater/déplacer du code inutilement.
- Fusionner régulièrement plutôt qu'une grosse fois tous les six mois.
- Utiliser `merge` (pas `rebase`) sur `master`, puisque la branche est poussée sur GitHub.

---

## 6. Dépannage rapide

| Problème | Solution |
|----------|----------|
| `error MSB3027` / fichier verrouillé | Fermer DS4Windows (icône près de l'horloge) et relancer |
| L'appli ne démarre pas | Installer le « .NET 8 Desktop Runtime » x64 |
| Manette détectée deux fois en jeu | Installer/configurer HidHide |
| Pas de manette virtuelle Xbox | Installer ViGEmBus (Paramètres → « Installer les pilotes », ou dossier `extras\`) |
| Repartir d'une compilation propre | Supprimer `DS4Windows\bin` et `DS4Windows\obj`, puis recompiler |
