#!/usr/bin/env python3
"""Test-metrics gate (TSK-04, definition of done in docs/04-task-breakdown.md §2).

Fails (exit 1) when:
  * no test results are found (a broken test run must never look green),
  * any test failed, errored, timed out or was aborted,
  * any test was skipped / not executed (no silently disabled tests),
  * line coverage of a gated assembly is below the threshold.

A gated assembly with no coverable lines yet (for example an empty layer early in the project) is
reported as "n/a", not as a failure. Writes a Markdown summary to $GITHUB_STEP_SUMMARY when it is set.
"""
import argparse
import glob
import os
import sys
import xml.etree.ElementTree as ET

TRX_NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
FAILING_COUNTERS = ("failed", "error", "timeout", "aborted")


def read_trx(path):
    root = ET.parse(path).getroot()
    counters = root.find("t:ResultSummary/t:Counters", TRX_NS)
    if counters is None:
        raise ValueError(f"{path}: no ResultSummary/Counters element")
    storage = root.find("t:TestDefinitions/t:UnitTest", TRX_NS)
    suite = os.path.splitext(os.path.basename(storage.get("storage")))[0] if storage is not None else os.path.basename(path)
    value = lambda name: int(counters.get(name, "0"))
    return {
        "suite": suite,
        "total": value("total"),
        "passed": value("passed"),
        "failed": sum(value(name) for name in FAILING_COUNTERS),
        "skipped": value("notExecuted") + value("inconclusive"),
    }


def read_coverage(path, assemblies):
    """Returns {assembly: (covered_lines, coverable_lines)} from a Cobertura report."""
    result = {name: (0, 0) for name in assemblies}
    if not path or not os.path.exists(path):
        return result
    root = ET.parse(path).getroot()
    for package in root.iter("package"):
        name = package.get("name")
        if name not in result:
            continue
        # Cobertura lists each line twice: under <method> and under <class>. Count only class-level lines.
        lines = [line for cls in package.iter("class") for line in cls.findall("lines/line")]
        covered = sum(1 for line in lines if int(line.get("hits", "0")) > 0)
        previous_covered, previous_total = result[name]
        result[name] = (previous_covered + covered, previous_total + len(lines))
    return result


def main():
    # Portable output: Windows consoles default to cp1252, which can't print the status symbols.
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--trx-dir", required=True)
    parser.add_argument("--coverage", help="Merged Cobertura XML (e.g. from ReportGenerator)")
    parser.add_argument("--min-line-coverage", type=float, default=80.0)
    parser.add_argument("--gated-assemblies", nargs="*", default=[])
    args = parser.parse_args()

    problems = []
    lines = ["## Test-metrics gate", "", "| Suite | Total | Passed | Failed | Skipped |", "|---|---|---|---|---|"]

    trx_files = sorted(glob.glob(os.path.join(args.trx_dir, "**", "*.trx"), recursive=True))
    if not trx_files:
        problems.append(f"no .trx test results found under {args.trx_dir}")

    totals = {"total": 0, "passed": 0, "failed": 0, "skipped": 0}
    for path in trx_files:
        result = read_trx(path)
        for key in totals:
            totals[key] += result[key]
        lines.append(f"| {result['suite']} | {result['total']} | {result['passed']} | {result['failed']} | {result['skipped']} |")
    lines.append(f"| **Total** | **{totals['total']}** | **{totals['passed']}** | **{totals['failed']}** | **{totals['skipped']}** |")

    if trx_files and totals["total"] == 0:
        problems.append("test results contain 0 tests")
    if totals["failed"] > 0:
        problems.append(f"{totals['failed']} test(s) failed")
    if totals["skipped"] > 0:
        problems.append(f"{totals['skipped']} test(s) skipped or not executed (skipped must be 0)")

    lines += ["", f"**Coverage gate:** line coverage ≥ {args.min_line_coverage:g}% for gated assemblies.", "",
              "| Assembly | Covered / coverable lines | Line coverage | Result |", "|---|---|---|---|"]
    for name, (covered, coverable) in read_coverage(args.coverage, args.gated_assemblies).items():
        if coverable == 0:
            lines.append(f"| {name} | 0 / 0 | n/a | ➖ no code yet |")
            continue
        percent = 100.0 * covered / coverable
        ok = percent >= args.min_line_coverage
        lines.append(f"| {name} | {covered} / {coverable} | {percent:.1f}% | {'✅' if ok else '❌'} |")
        if not ok:
            problems.append(f"{name} line coverage {percent:.1f}% < {args.min_line_coverage:g}%")

    lines += ["", "**Result:** " + ("✅ passed" if not problems else "❌ failed: " + "; ".join(problems))]
    report = "\n".join(lines)
    print(report)

    # GitHub annotations: shown on the PR and readable through the public API, so a red gate is
    # diagnosable without opening the job log.
    if os.environ.get("GITHUB_ACTIONS") == "true":
        for problem in problems:
            print(f"::error title=Test-metrics gate::{problem}")

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write(report + "\n")

    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
