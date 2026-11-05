import hashlib
import json
import unittest
from unittest.mock import patch

import build_catalog as build


class CommunityCatalogTests(unittest.TestCase):
    def test_multi_folder_source_replaces_archive_pack_and_resolves_package_dependencies(self):
        source = {"Name": "Grid2", "Folder": "Grid2", "Folders": ["Grid2", "Grid2Options"],
                  "Prefix": "addons/", "Repository": "bkader/Grid2-WoTLK", "Commit": "a" * 40,
                  "Description": "Raid frames", "PathMappings": {"Grid2/wrong-name/": "Grid2/required-name/"}}
        content = {
            "addons/Grid2/Grid2.toc": b"## Interface: 30300\n## Version: r736\n-- DBM-style comment\nCore.lua",
            "addons/Grid2/core.lua": b"-- runtime",
            "addons/Grid2/wrong-name/library.lua": b"-- dependency",
            "addons/Grid2Options/Grid2Options.toc": b"## Interface: 30300\n## Dependencies: Grid2\nOptions.lua",
            "addons/Grid2Options/Options.lua": b"-- options",
            "tools/test.py": b"-- do not ship",
        }
        tree = {"truncated": False, "tree": [dict(path=p, type="blob", mode="100644", size=len(b),
                sha=hashlib.sha1(b"blob " + str(len(b)).encode() + b"\0" + b).hexdigest()) for p, b in content.items()]}
        tree["tree"][1]["mode"] = "100755"
        catalog = {"Packages": [{"Id": "Grid2", "Folders": ["Grid2"], "Dependencies": []}],
                   "Files": [{"Path": "Grid2/Old.lua"}]}
        def download(url):
            if "/git/trees/" in url:
                return json.dumps(tree).encode()
            return content[url.split("a" * 40 + "/")[1]]
        with patch.object(build.pathlib.Path, "read_text", return_value=json.dumps([source])), patch.object(build, "download", side_effect=download):
            build.add_community_sources(catalog)
        package, = catalog["Packages"]
        self.assertEqual(package["Folders"], ["Grid2", "Grid2Options"])
        self.assertEqual(package["Dependencies"], [])
        self.assertEqual(package["Version"], "r736")
        self.assertEqual({f["Path"] for f in catalog["Files"]},
                         {p.removeprefix("addons/").replace("wrong-name/", "required-name/") for p in content if p.startswith("addons/")})


if __name__ == "__main__":
    unittest.main()
