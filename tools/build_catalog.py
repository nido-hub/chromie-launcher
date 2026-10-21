"""Freeze the historical WoWGaming catalogue. Python 3.11+, standard library only.

Downloads metadata, never installs or runs addon code. Review generated changes.
"""
import concurrent.futures
import hashlib
import json
import pathlib
import re
import urllib.request
import urllib.parse
import sys

COMMIT = "f23627cf6cc3a353a96cb9240940ad33532a7c40"
ROOT = pathlib.Path(__file__).resolve().parents[1]
HEADERS = {"User-Agent": "ChromieLauncher-Catalog/0.1"}
FORBIDDEN_EXTENSIONS = {".exe", ".dll", ".bat", ".cmd", ".ps1", ".msi", ".com", ".scr", ".lnk", ".vbs"}


def download(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS), timeout=90) as response:
        return response.read()


def main():
    tree = json.loads(download(f"https://api.github.com/repos/wowgaming/addon-archive/git/trees/{COMMIT}?recursive=1"))
    if tree.get("truncated"):
        raise RuntimeError("GitHub tree is truncated")
    files = [f for f in tree["tree"] if f["type"] == "blob" and f.get("mode") == "100644"]
    tocs = {}
    for file in files:
        parts = file["path"].split("/")
        if len(parts) == 2 and parts[1].lower() == parts[0].lower() + ".toc":
            tocs[parts[0]] = file

    def read_toc(item):
        folder, file = item
        url_path = urllib.parse.quote(file["path"], safe="/")
        data = download(f"https://raw.githubusercontent.com/wowgaming/addon-archive/{COMMIT}/{url_path}")
        actual = hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()
        if actual != file["sha"]:
            raise RuntimeError("Git blob mismatch: " + folder)
        text = data.decode("utf-8-sig", errors="replace")
        return folder, dict((key.strip().lower(), value.strip()) for key, value in re.findall(r"^##\s*([^:\r\n]+):\s*(.*)$", text, re.M))

    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        metadata = dict(pool.map(read_toc, tocs.items()))
    compatible = {folder for folder, meta in metadata.items() if any(
        value.isdigit() and 30000 <= int(value) <= 30300
        for value in re.split(r"[,\s]+", meta.get("interface", "")))}
    families = ["DBM", "AtlasLoot", "Atlas", "Bagnon", "Dominos", "PitBull4", "Prat-3.0", "Skada", "Talented", "totalRP2", "TourGuide", "TidyPlates", "Altoholic", "DataStore", "Gatherer", "GatherMate", "Overachiever", "RaidRoll", "SilverDragon", "InlineAura", "MrTrader"]

    def group(folder):
        for family in families:
            if folder == family or folder.startswith(family + "_") or (family == "DBM" and folder.startswith("DBM-")):
                return family
        if folder.startswith("Titan"):
            return "Titan"
        return folder

    groups = {}
    for folder in sorted(compatible):
        groups.setdefault(group(folder), []).append(folder)
    categories = {
        "Combat & raids": ["DBM", "Recount", "Skada", "Omen", "GTFO", "Grid", "HealBot", "Healium", "Clique", "Decursive", "RaidBuffStatus", "MikScrollingBattleText", "PowerAuras", "Cooldowns", "Quartz"],
        "Quests & exploration": ["QuestHelper", "EveryQuest", "TomTomLite", "Carbonite", "Mapster", "Gatherer", "GatherMate", "TourGuide", "SilverDragon"],
        "Bags & economy": ["Bagnon", "BankStack", "Postal", "SellJunk", "Altoholic", "BeanCounter", "Informant", "Enchantrix", "AckisRecipeList", "MrTrader"],
        "Equipment": ["AtlasLoot", "GearScore", "GearScoreLite", "ClassLoot", "Outfitter", "EquipCompare", "TopFit"],
        "PvP": ["Gladius", "Proximo", "InterruptBar", "LoseControl"],
    }
    descriptions = {
        "DBM": "Boss alerts and raid/dungeon modules. Historical archive, not the current ChromieCraft-specific release.",
        "Bagnon": "Combines bags and bank storage into a unified interface.",
        "AtlasLoot": "Browse loot tables for dungeons, raids and professions.",
        "Recount": "Analyze damage, healing and combat statistics.",
        "Skada": "Modular damage, healing and threat meters.",
        "Bartender4": "Customize and move your action bars.",
        "QuestHelper": "Track quest objectives and plan your route.",
        "Postal": "Improve in-game mail management.",
        "Omen": "Monitor threat generated during combat.",
        "GTFO": "Sound alerts when your character stands in a dangerous area.",
    }
    packages = []
    for package_id, folders in groups.items():
        main_folder = package_id if package_id in folders else folders[0]
        meta = metadata[main_folder]
        dependencies = set()
        for folder in folders:
            for dependency in re.split(r"[,\s]+", metadata[folder].get("dependencies", metadata[folder].get("requireddeps", ""))):
                if dependency and group(dependency) != package_id and not dependency.startswith("Blizzard_"):
                    dependencies.add(group(dependency))
        category = next((key for key, values in categories.items() if package_id in values), "Interface")
        if package_id.startswith("Auc-") or package_id in ("Stubby", "BonusScanner"):
            category = "Bags & economy"
        name = re.sub(r"\|c[\da-fA-F]{8}|\|r", "", meta.get("title", package_id))
        packages.append({"Id": package_id, "Name": "Deadly Boss Mods" if package_id == "DBM" else name,
                         "Category": category, "Description": descriptions.get(package_id, meta.get("notes", "Addon from the historical WoWGaming archive.")),
                         "Version": meta.get("version", "Unknown"), "Folders": folders, "Dependencies": sorted(dependencies)})
    # Do not offer packages whose mandatory dependencies cannot be installed.
    while True:
        ids = {p["Id"] for p in packages}
        kept = [p for p in packages if all(dep in ids for dep in p["Dependencies"])]
        if len(kept) == len(packages):
            break
        packages = kept
    included = {folder for p in packages for folder in p["Folders"]}
    catalog = {"Commit": COMMIT, "Source": "https://wowgaming.github.io/addon-archive/", "Packages": sorted(packages, key=lambda p: p["Name"].casefold()),
               "Files": [{"Path": f["path"], "Sha": f["sha"], "Size": f["size"]} for f in files if f["path"].split("/")[0] in included and pathlib.PurePosixPath(f["path"]).suffix.lower() not in FORBIDDEN_EXTENSIONS]}
    add_community_sources(catalog)
    catalog["Packages"].sort(key=lambda p: p["Name"].casefold())
    output = ROOT / "src/ChromieLauncher.Core/Data/catalog.json"
    output.write_text(json.dumps(catalog, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Catalog: {len(catalog['Packages'])} packages, {sum(len(p['Folders']) for p in catalog['Packages'])} folders, {len(catalog['Files'])} files")
    print(f"Excluded folders (incompatible TOC or missing dependencies): {len(tocs) - len(included)}")


def add_community_sources(catalog):
    sources = json.loads((ROOT / "tools/community-addons.json").read_text(encoding="utf-8"))
    for source in sources:
        repository, commit, folder = source["Repository"], source["Commit"], source["Folder"]
        tree = json.loads(download(f"https://api.github.com/repos/{repository}/git/trees/{commit}?recursive=1"))
        if tree.get("truncated"):
            raise RuntimeError("Truncated community source tree: " + repository)
        files = [file for file in tree["tree"] if file["type"] == "blob" and file.get("mode") == "100644" and any(
            file["path"] == include or (include.endswith("/") and file["path"].startswith(include)) for include in source["Include"])]
        toc = next(file for file in files if file["path"] == folder + ".toc")
        data = download(f"https://raw.githubusercontent.com/{repository}/{commit}/{toc['path']}")
        if hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest() != toc["sha"]:
            raise RuntimeError("Community TOC hash mismatch")
        metadata = dict((key.strip().lower(), value.strip()) for key, value in re.findall(r"^##\s*([^:\r\n]+):\s*(.*)$", data.decode("utf-8-sig"), re.M))
        if metadata.get("interface") != "30300":
            raise RuntimeError("Community addon must declare interface 30300")
        dependencies = [value for value in re.split(r"[,\s]+", metadata.get("dependencies", metadata.get("requireddeps", ""))) if value]
        for dependency in dependencies:
            if not any(dependency in package["Folders"] for package in catalog["Packages"]):
                raise RuntimeError("Missing community dependency: " + dependency)
        catalog["Packages"].append({"Id": folder, "Name": source["Name"], "Category": "ChromieCraft", "Description": source["Description"],
                                    "Version": metadata["version"], "Folders": [folder], "Dependencies": dependencies,
                                    "Repository": repository, "Commit": commit})
        for file in files:
            if pathlib.PurePosixPath(file["path"]).suffix.lower() in FORBIDDEN_EXTENSIONS:
                raise RuntimeError("Executable found in community addon")
            catalog["Files"].append({"Path": folder + "/" + file["path"], "Sha": file["sha"], "Size": file["size"],
                                     "Repository": repository, "Commit": commit, "SourcePath": file["path"]})
        print(f"Community addon: {source['Name']} {metadata['version']} ({repository}@{commit[:8]}) — {len(files)} files")


def verify_community_files():
    catalog = json.loads((ROOT / "src/ChromieLauncher.Core/Data/catalog.json").read_text(encoding="utf-8"))
    files = [file for file in catalog["Files"] if file.get("Repository")]
    def verify(file):
        path = urllib.parse.quote(file["SourcePath"], safe="/")
        data = download(f"https://raw.githubusercontent.com/{file['Repository']}/{file['Commit']}/{path}")
        sha = hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()
        if len(data) != file["Size"] or sha != file["Sha"]:
            raise RuntimeError("Invalid community file: " + file["Path"])
        return len(data)
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        size = sum(pool.map(verify, files))
    print(f"Verified {len(files)} community files ({size} bytes) at their pinned revisions. No game files modified.")


if __name__ == "__main__":
    if sys.argv[1:] == ["--verify-community"]:
        verify_community_files()
    else:
        main()
