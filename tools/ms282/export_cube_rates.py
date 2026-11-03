#!/usr/bin/env python3
"""Snapshot 282.5 cube rate inputs without confusing disabled TSVs with active rates."""
import argparse
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]


def export(source):
    hashes = {}

    def read(rel):
        raw = (source / rel).read_bytes()
        hashes[rel] = hashlib.sha256(raw).hexdigest()
        return raw.decode("utf-8-sig")

    def rows(name):
        text = read("data/resources/tms282/enhance/" + name + ".tsv")
        lines = [line.split("\t") for line in text.splitlines() if line.strip() and not line.startswith("#")]
        return [dict(zip(lines[0], row, strict=True)) for row in lines[1:]]

    constants = read("source/src/main/java/net/swordie/ms/constants/ItemConstants.java")
    potential = read("source/src/main/java/net/swordie/ms/constants/PotentialConstants.java")
    families = {}
    for name, body in re.findall(r"public static List<Integer> (\w+) = List.of\((.*?)\);", potential, re.S):
        body = re.sub(r"//[^\n]*", "", body)
        families[name] = [int(x.strip()) for x in body.split(",") if x.strip()]
    for name in ("CubeRateTable", "EnhanceTables"):
        read("source/src/main/java/net/swordie/ms/loaders/enhance/" + name + ".java")
    rates = {name: [int(x.strip()) for x in values.split(",")]
             for name, values in re.findall(r"public static final int\[\] (\w*TIER_UP_RATES?) = new int\[\]\{([^}]+)\}", constants)}
    if len(rates) != 21 or any(len(v) != 3 or any(n < 0 or n > 1000 for n in v) for v in rates.values()):
        raise ValueError("Unexpected source tier-up arrays; recheck source")

    tables = {name: rows(name) for name in ("cube-settings", "cube-grade", "cube-option-level", "cube-options", "cube-option-override")}
    # Validate weights but retain ordering and original spelling for source reproducibility.
    for name in ("cube-options", "cube-option-override"):
        for row in tables[name]:
            weights = [tuple(map(int, pair.split(":"))) for pair in row["weights"].split(",") if pair.strip()]
            if not weights or any(k <= 0 or w < 0 for k, w in weights) or sum(w for _, w in weights) <= 0:
                raise ValueError("Invalid weights")
    return {"schemaVersion": 1, "sourceSha256": hashes, "tierUpUnit": 1000,
            "builtinTierUpRates": rates, "optionFamilies": families, "tables": tables}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT.parent.parent / "TMS282.5")
    parser.add_argument("--out", type=Path, default=ROOT / "Assets/Resources/Enhancement/CubeRates.json")
    args = parser.parse_args()
    data = export(args.source)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n")
    print("Exported", len(data["builtinTierUpRates"]), "rate arrays; tables:",
          {k: len(v) for k, v in data["tables"].items()})


if __name__ == "__main__":
    main()
