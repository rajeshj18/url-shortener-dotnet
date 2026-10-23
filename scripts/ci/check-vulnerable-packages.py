#!/usr/bin/env python3
"""Dependency (SCA) gate (TSK-04, ADR-0011).

Reads the JSON from:
    dotnet list <solution> package --vulnerable --include-transitive --format json
and fails (exit 1) if any direct or transitive package has a known vulnerability.
"""
import json
import sys


def main():
    # Portable output: Windows consoles default to cp1252, which can't print the status symbols.
    sys.stdout.reconfigure(encoding="utf-8")
    if len(sys.argv) != 2:
        print("usage: check-vulnerable-packages.py <dotnet-list-output.json>", file=sys.stderr)
        return 2

    with open(sys.argv[1], encoding="utf-8-sig") as handle:
        report = json.load(handle)

    findings = []
    for project in report.get("projects", []):
        for framework in project.get("frameworks", []):
            for kind in ("topLevelPackages", "transitivePackages"):
                for package in framework.get(kind, []):
                    for vulnerability in package.get("vulnerabilities", []):
                        findings.append((project.get("path", "?"), package.get("id"), package.get("resolvedVersion"),
                                         vulnerability.get("severity"), vulnerability.get("advisoryurl")))

    if not findings:
        print("✅ No known-vulnerable packages (direct or transitive).")
        return 0

    print(f"❌ {len(findings)} known-vulnerable package reference(s):")
    for project, package, version, severity, url in findings:
        print(f"  - {package} {version} [{severity}] in {project}: {url}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
