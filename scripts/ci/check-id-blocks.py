#!/usr/bin/env python3
"""ID block registry check (ADR-0005, TSK-07).

Fails (exit 1) when config/id-blocks.json:
  * is not valid JSON or does not have the expected shape,
  * declares a code space other than 62^7,
  * has a block ID that is malformed (B-NNN) or repeated,
  * has a region index that is not a non-negative integer,
  * has a range that is empty or falls outside [0, 62^7),
  * has two blocks that overlap (in any region: generated codes must be unique across all regions).

Ranges are half-open: [rangeStart, rangeEnd).
"""
import argparse
import json
import re
import sys

CODE_SPACE_SIZE = 62 ** 7
BLOCK_ID = re.compile(r"^B-\d{3,}$")
REQUIRED = ("blockId", "regionIndex", "rangeStart", "rangeEnd")


def is_int(value):
    return isinstance(value, int) and not isinstance(value, bool)


def check(registry):
    """Returns a list of error messages; empty when the registry is valid."""
    if not isinstance(registry, dict) or not isinstance(registry.get("blocks"), list):
        return ["registry must be an object with a 'blocks' array"]

    errors = []
    if registry.get("codeSpaceSize") != CODE_SPACE_SIZE:
        errors.append(f"codeSpaceSize must be {CODE_SPACE_SIZE} (62^7)")

    seen = set()
    valid = []
    for index, block in enumerate(registry["blocks"]):
        where = f"blocks[{index}]"
        if not isinstance(block, dict):
            errors.append(f"{where}: must be an object")
            continue
        missing = [name for name in REQUIRED if name not in block]
        if missing:
            errors.append(f"{where}: missing {', '.join(missing)}")
            continue

        block_id = block["blockId"]
        if not isinstance(block_id, str) or not BLOCK_ID.match(block_id):
            errors.append(f"{where}: blockId {block_id!r} must look like B-001")
            continue
        where = block_id
        if block_id in seen:
            errors.append(f"{where}: duplicate blockId")
            continue
        seen.add(block_id)

        region, start, end = block["regionIndex"], block["rangeStart"], block["rangeEnd"]
        if not is_int(region) or region < 0:
            errors.append(f"{where}: regionIndex must be an integer >= 0")
            continue
        if not is_int(start) or not is_int(end):
            errors.append(f"{where}: rangeStart and rangeEnd must be integers")
            continue
        if start < 0 or end > CODE_SPACE_SIZE or start >= end:
            errors.append(f"{where}: range [{start}, {end}) must be non-empty and inside [0, {CODE_SPACE_SIZE})")
            continue
        valid.append((start, end, block_id))

    valid.sort()
    for (_, previous_end, previous_id), (start, _, block_id) in zip(valid, valid[1:]):
        if start < previous_end:
            errors.append(f"{block_id}: overlaps {previous_id}")

    return errors


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("registry", nargs="?", default="config/id-blocks.json")
    args = parser.parse_args(argv)

    try:
        with open(args.registry, encoding="utf-8") as handle:
            registry = json.load(handle)
    except (OSError, ValueError) as error:
        print(f"FAIL {args.registry}: {error}")
        return 1

    errors = check(registry)
    for error in errors:
        print(f"FAIL {error}")
    if errors:
        return 1

    print(f"OK {args.registry}: {len(registry['blocks'])} block(s), no overlaps")
    return 0


if __name__ == "__main__":
    sys.exit(main())
