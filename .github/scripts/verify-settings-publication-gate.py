#!/usr/bin/env python3
"""Parse the xUnit v3 publication results and emit a small shell-safe record summary."""

from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from typing import Dict, List, Optional, Set, Tuple

TEST_TYPE = "CCP.Core.Settings.Tests.SettingsPublicationTests"
EXPECTED_EXCEPTION = "Xunit.Sdk.EqualException"
MISSED_TEMP_MESSAGES = {
    "publication did not leave its flushed temp file behind within 2s",
    "SaveImmediate did not leave its flushed temp file behind while the settings reader was held.",
}
CONTRACTS = {
    "release": ("ObservedPublicationFailureThatReleasesTheReaderRecoversAndPublishes", "fr", "ja"),
    "hold_read": ("PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile", "6", "1"),
    "hold_read_delete": ("PersistentHeldReaderLeavesPreviousSnapshotAndCleansItsTempFile", "6", "1"),
    "later": ("LaterSaveWinsWhenAnEarlierPublicationWaitsForTheReader", "de", "ja"),
}


def out(key: str, value: object) -> None:
    print(key + "=" + " ".join(str(value).split()))


def local_name(tag: object) -> str:
    return tag.rsplit("}", 1)[-1] if isinstance(tag, str) else ""


def identity(test: ET.Element) -> str:
    method = test.get("method") or "?"
    name = test.get("name") or ""
    return method + ("(" + name.split("(", 1)[1] if "(" in name else "")


def element_text(element: ET.Element) -> str:
    return "".join(element.itertext())


def normalize_assertion_value(raw: str) -> str:
    """Remove only xUnit's two documented outer-quote forms; otherwise keep the value."""
    value = raw.strip()
    if len(value) >= 4 and value.startswith('\\"') and value.endswith('\\"'):
        return value[2:-2]
    if len(value) >= 2 and value.startswith('"') and value.endswith('"'):
        return value[1:-1]
    return value


def unique_assertion_value(message: str, label: str) -> Optional[str]:
    pattern = re.compile(rf"^[ \t]*{re.escape(label)}:[ \t]*(.*?)[ \t]*$")
    values = [normalize_assertion_value(match.group(1)) for line in message.splitlines() if (match := pattern.fullmatch(line))]
    return values[0] if len(values) == 1 else None


def has_equal_header(message: str) -> bool:
    lines = message.splitlines()
    return bool(lines and lines[0].strip().startswith("Assert.Equal() Failure:"))


def has_own_method_frame(stack: str, method: str) -> bool:
    pattern = re.compile(
        rf"^at {re.escape(TEST_TYPE + '.' + method)}\([^)]*\)(?: in .+)?$"
    )
    return any(pattern.fullmatch(line.strip()) for line in stack.splitlines())


def inspect_record(test: ET.Element, contract: Optional[Tuple[str, str, str]]) -> Dict[str, object]:
    result = test.get("result", "")
    failures = [node for node in test.iter() if local_name(node.tag) == "failure"]
    direct_failures = [node for node in list(test) if local_name(node.tag) == "failure"]
    details: dict[str, object] = {
        "shape": 1,
        "signature": 0,
        "missed_temp": 0,
        "summary": "",
        "issue": "",
    }

    if result in ("Pass", "Skip"):
        if failures:
            details["shape"] = 0
            details["issue"] = f"{result} record has failure descendant(s)"
        return details

    if result != "Fail":
        if failures:
            details["shape"] = 0
            details["issue"] = "unknown-result record has failure descendant(s)"
        return details

    issue = ""
    if len(failures) != 1 or len(direct_failures) != 1:
        issue = "Fail record must have exactly one direct failure node"
    failure = direct_failures[0] if len(direct_failures) == 1 else None
    messages = [node for node in list(failure) if local_name(node.tag) == "message"] if failure is not None else []
    stacks = [node for node in list(failure) if local_name(node.tag) == "stack-trace"] if failure is not None else []
    if not issue and (len(messages) != 1 or len(stacks) != 1):
        issue = "Fail record must have one direct message and one direct stack-trace"

    exception = failure.get("exception-type", "") if failure is not None else ""
    message = element_text(messages[0]) if len(messages) == 1 else ""
    stack = element_text(stacks[0]) if len(stacks) == 1 else ""
    if not issue and not exception:
        issue = "Fail record failure node has no exception-type"
    if not issue and (not message.strip() or not stack.strip()):
        issue = "Fail record failure node has an empty message or stack-trace"

    expected = unique_assertion_value(message, "Expected")
    actual = unique_assertion_value(message, "Actual")
    own_stack = bool(contract and has_own_method_frame(stack, contract[0]))
    if contract:
        details["missed_temp"] = int(message in MISSED_TEMP_MESSAGES)
        expected_exception, expected_value, actual_value = EXPECTED_EXCEPTION, contract[1], contract[2]
        details["signature"] = int(
            not issue
            and exception == expected_exception
            and has_equal_header(message)
            and expected == expected_value
            and actual == actual_value
            and own_stack
        )
    details["summary"] = (
        f"exception={exception or '<missing>'}; expected={expected if expected is not None else '<missing>'}; "
        f"actual={actual if actual is not None else '<missing>'}; own_stack={int(own_stack)}"
    )
    if issue:
        details["shape"] = 0
        details["issue"] = issue
    return details


def parse(path: str, release_method: str, slot_args: List[str]) -> None:
    slots = dict(argument.split(":", 1) for argument in slot_args)
    try:
        root = ET.parse(path).getroot()
    except Exception as error:  # malformed/truncated XML is not evidence
        out("ok", 0)
        out("error", repr(error)[:300])
        return

    if root.tag not in ("assemblies", "assembly"):
        out("ok", 0)
        out("error", f"unexpected root element <{root.tag}>")
        return
    assemblies = [root] if root.tag == "assembly" else root.findall("assembly")
    if len(assemblies) != 1:
        out("ok", 0)
        out("error", f"expected exactly one <assembly>, found {len(assemblies)}")
        return

    assembly = assemblies[0]
    out("ok", 1)
    for key in ("total", "passed", "failed", "skipped", "errors", "environment"):
        out(key, assembly.get(key, ""))

    tests = assembly.findall(".//test")
    seen = {"Pass": 0, "Fail": 0, "Skip": 0, "unknown": 0}
    for test in tests:
        result = test.get("result", "")
        seen[result if result in seen else "unknown"] += 1
    out("n_tests", len(tests))
    out("n_pass", seen["Pass"])
    out("n_fail", seen["Fail"])
    out("n_skip", seen["Skip"])
    out("n_unknown", seen["unknown"])
    out("skip_names", ",".join(sorted((test.get("method") or "?") for test in tests if test.get("result") == "Skip")))

    by_identity: Dict[str, List[ET.Element]] = {}
    for test in tests:
        by_identity.setdefault(identity(test), []).append(test)
    expected = {value: slot for slot, value in slots.items()}
    unmatched: List[str] = []
    matched: Set[str] = set()
    invalid: List[str] = []

    for key, records in sorted(by_identity.items()):
        slot = expected.get(key)
        if slot is None:
            unmatched.append(f"unexpected record '{key}'")
            continue
        if len(records) != 1:
            unmatched.append(f"'{key}' reported {len(records)} times")
            continue
        test = records[0]
        matched.add(slot)
        if test.get("type") != TEST_TYPE or test.get("name") != TEST_TYPE + "." + key:
            invalid.append(f"{key}: identity attributes do not match the contracted test type/name")
        details = inspect_record(test, CONTRACTS.get(slot))
        if details["issue"]:
            invalid.append(f"{key}: {details['issue']}")
        out("slot_" + slot, test.get("result", ""))
        out("failure_shape_" + slot, details["shape"])
        out("failure_signature_" + slot, details["signature"])
        out("failure_missed_temp_" + slot, details["missed_temp"])
        out("failure_summary_" + slot, details["summary"])

    # Inspect records that were not identity-matched too: Pass/Skip failure descendants and
    # nested/duplicate failure nodes are invalid evidence even before identity reconciliation.
    for key, records in sorted(by_identity.items()):
        if expected.get(key) is not None and len(records) == 1:
            continue
        for test in records:
            details = inspect_record(test, None)
            if details["issue"]:
                invalid.append(f"{key}: {details['issue']}")

    out("unmatched", "; ".join(unmatched))
    out("missing", ", ".join(sorted(slots[key] + " (" + key + ")" for key in slots if key not in matched)))
    out("invalid", "; ".join(invalid))

    release_records = [test for test in tests if test.get("method") == release_method]
    out("n_release", len(release_records))
    if len(release_records) == 1:
        details = inspect_record(release_records[0], CONTRACTS["release"])
        out("release_result", release_records[0].get("result", ""))
        out("release_shape", details["shape"])
        out("release_signature", details["signature"])
        out("release_missed_temp", details["missed_temp"])
        out("release_summary", details["summary"])
    else:
        out("release_result", "")
        out("release_shape", 0)
        out("release_signature", 0)
        out("release_missed_temp", 0)
        out("release_summary", "")


if __name__ == "__main__":
    parse(sys.argv[1], sys.argv[2], sys.argv[3:])
