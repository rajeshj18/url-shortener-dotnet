"""Unit tests for scripts/ci/check-id-blocks.py (TSK-07, AC7). Run: python3 -m unittest discover -s scripts/ci/tests"""
import importlib.util
import json
import os
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "..", "check-id-blocks.py")
REGISTRY = os.path.join(HERE, "..", "..", "..", "config", "id-blocks.json")

_spec = importlib.util.spec_from_file_location("check_id_blocks", SCRIPT)
check_id_blocks = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(check_id_blocks)

SPACE = 62 ** 7


def block(block_id="B-001", region=0, start=0, end=1000):
    return {"blockId": block_id, "regionIndex": region, "rangeStart": start, "rangeEnd": end}


def registry(*blocks):
    return {"codeSpaceSize": SPACE, "blocks": list(blocks)}


class CheckIdBlocksTests(unittest.TestCase):
    def assert_fails(self, value, fragment):
        errors = check_id_blocks.check(value)
        self.assertTrue(any(fragment in error for error in errors), errors)

    def test_real_registry_passes(self):
        self.assertEqual(check_id_blocks.main([REGISTRY]), 0)

    def test_adjacent_blocks_in_different_regions_pass(self):
        self.assertEqual(check_id_blocks.check(registry(block(), block("B-002", 1, 1000, 2000))), [])

    def test_whole_code_space_passes(self):
        self.assertEqual(check_id_blocks.check(registry(block(end=SPACE))), [])

    def test_overlap_fails(self):
        self.assert_fails(registry(block(), block("B-002", 1, 999, 2000)), "overlaps B-001")

    def test_overlap_is_found_regardless_of_order(self):
        self.assert_fails(registry(block("B-002", 1, 500, 600), block()), "overlaps")

    def test_range_beyond_code_space_fails(self):
        self.assert_fails(registry(block(end=SPACE + 1)), "inside")

    def test_negative_start_fails(self):
        self.assert_fails(registry(block(start=-1)), "inside")

    def test_empty_range_fails(self):
        self.assert_fails(registry(block(start=10, end=10)), "non-empty")

    def test_duplicate_block_id_fails(self):
        self.assert_fails(registry(block(), block(start=1000, end=2000)), "duplicate")

    def test_malformed_block_id_fails(self):
        self.assert_fails(registry(block("BLOCK-1")), "must look like")

    def test_missing_field_fails(self):
        incomplete = block()
        del incomplete["rangeEnd"]
        self.assert_fails(registry(incomplete), "missing rangeEnd")

    def test_negative_region_fails(self):
        self.assert_fails(registry(block(region=-1)), "regionIndex")

    def test_non_integer_range_fails(self):
        self.assert_fails(registry(block(end="1000")), "integers")

    def test_boolean_is_not_an_integer(self):
        self.assert_fails(registry(block(region=True)), "regionIndex")

    def test_wrong_code_space_fails(self):
        self.assert_fails({"codeSpaceSize": 1, "blocks": [block()]}, "codeSpaceSize")

    def test_wrong_shape_fails(self):
        self.assert_fails([block()], "'blocks' array")

    def test_invalid_json_file_fails(self):
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False, encoding="utf-8") as handle:
            handle.write("{ not json")
            path = handle.name
        try:
            self.assertEqual(check_id_blocks.main([path]), 1)
        finally:
            os.remove(path)

    def test_invalid_registry_file_fails(self):
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False, encoding="utf-8") as handle:
            json.dump(registry(block(), block("B-002", 1, 0, 10)), handle)
            path = handle.name
        try:
            self.assertEqual(check_id_blocks.main([path]), 1)
        finally:
            os.remove(path)


if __name__ == "__main__":
    unittest.main()
