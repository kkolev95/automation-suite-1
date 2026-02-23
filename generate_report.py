"""
generate_report.py
Reads a Visual Studio TRX file and produces a self-contained HTML test report.
"""

import xml.etree.ElementTree as ET
from collections import OrderedDict
from datetime import datetime, timezone, timedelta
import re, html, os

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
TRX_PATH   = "/home/kolev95/examtest1/TestIT.ApiTests/results.trx"
HTML_PATH  = "/home/kolev95/examtest1/TestIT.ApiTests/test-report.html"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
NS = {"ns": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

def parse_duration(dur_str: str) -> float:
    """Convert 'HH:MM:SS.fffffff' to total seconds (float)."""
    parts = dur_str.split(":")
    h, m = int(parts[0]), int(parts[1])
    s = float(parts[2])
    return h * 3600 + m * 60 + s

def fmt_dur(seconds: float) -> str:
    """Format seconds to M:SS.ss or H:MM:SS.ss as appropriate."""
    if seconds >= 3600:
        h = int(seconds // 3600)
        remainder = seconds - h * 3600
        m = int(remainder // 60)
        s = remainder - m * 60
        return f"{h}h {m:02d}m {s:05.2f}s"
    elif seconds >= 60:
        m = int(seconds // 60)
        s = seconds - m * 60
        return f"{m}m {s:05.2f}s"
    else:
        return f"{seconds:.2f}s"

def parse_iso_datetime(s: str) -> datetime:
    """Parse an ISO-8601 datetime that may have a +HH:MM tz offset."""
    # Python 3.6 doesn't handle the colon in tz; strip it if present.
    if re.search(r'[+-]\d{2}:\d{2}$', s):
        s = s[:-3] + s[-2:]          # +02:00 -> +0200
    return datetime.strptime(s, "%Y-%m-%dT%H:%M:%S.%f%z")

# ---------------------------------------------------------------------------
# Parse TRX
# ---------------------------------------------------------------------------
tree = ET.parse(TRX_PATH)
root = tree.getroot()

# --- run-level times ---
times_el  = root.find("ns:Times", NS)
run_start = parse_iso_datetime(times_el.get("start"))
run_finish = parse_iso_datetime(times_el.get("finish"))
run_duration_sec = (run_finish - run_start).total_seconds()

# --- counters (official summary) ---
counters = root.find(".//ns:Counters", NS)
total_tests = int(counters.get("total", "0"))
passed      = int(counters.get("passed", "0"))
failed      = int(counters.get("failed", "0"))
errors      = int(counters.get("error", "0"))
skipped     = int(counters.get("notExecuted", "0"))

# --- individual results ---
class Test:
    __slots__ = ("full_name", "class_name", "method_name", "outcome", "duration_sec")
    def __init__(self, full_name, outcome, duration_sec):
        self.full_name    = full_name
        self.outcome      = outcome
        self.duration_sec = duration_sec
        # split into class + method at the last dot
        idx = full_name.rfind(".")
        if idx != -1:
            self.class_name  = full_name[:idx]
            self.method_name = full_name[idx+1:]
        else:
            self.class_name  = ""
            self.method_name = full_name

tests = []
for r in root.findall(".//ns:UnitTestResult", NS):
    tests.append(Test(
        full_name    = r.get("testName", ""),
        outcome      = r.get("outcome", "Unknown"),
        duration_sec = parse_duration(r.get("duration", "00:00:00.0000000"))
    ))

# --- group by class, preserving encounter order ---
classes: OrderedDict[str, list] = OrderedDict()
for t in tests:
    classes.setdefault(t.class_name, []).append(t)

# --- overall stats ---
pass_rate = (passed / total_tests * 100) if total_tests else 0.0
all_passed = (failed + errors) == 0

slowest = max(tests, key=lambda t: t.duration_sec) if tests else None
fastest = min(tests, key=lambda t: t.duration_sec) if tests else None
avg_dur  = sum(t.duration_sec for t in tests) / len(tests) if tests else 0.0

# ---------------------------------------------------------------------------
# HTML generation
# ---------------------------------------------------------------------------

# Short class label: strip common prefix "TestIT.ApiTests.Tests."
CLASS_PREFIX = "TestIT.ApiTests.Tests."

def short_class(name: str) -> str:
    return name[len(CLASS_PREFIX):] if name.startswith(CLASS_PREFIX) else name

# Colour palette
C_BG          = "#f4f6f8"
C_WHITE       = "#ffffff"
C_TEXT        = "#2c3e50"
C_TEXT_LIGHT  = "#7f8c8d"
C_BORDER      = "#dce1e6"
C_GREEN_BG    = "#27ae60"
C_GREEN_LIGHT = "#eafaf1"
C_GREEN_TEXT  = "#1e8449"
C_RED_BG      = "#e74c3c"
C_RED_LIGHT   = "#fdedec"
C_RED_TEXT    = "#c0392b"
C_ACCENT      = "#2980b9"       # blue accent for links / highlights

summary_bg  = C_GREEN_BG if all_passed else C_RED_BG
badge_failed_bg = C_RED_LIGHT
badge_passed_bg = C_GREEN_LIGHT

# --- display date/time in local offset kept from TRX ---
run_start_local = run_start.strftime("%Y-%m-%d %H:%M:%S %Z").replace("UTC", "").strip()
# If strftime gave an empty tz name, render the offset manually
if run_start.utcoffset() is not None:
    off = run_start.utcoffset()
    total_secs = int(off.total_seconds())
    sign = "+" if total_secs >= 0 else "-"
    total_secs = abs(total_secs)
    oh, om = divmod(total_secs // 60, 60)
    tz_str = f"UTC{sign}{oh:02d}:{om:02d}"
    run_start_local = run_start.strftime("%Y-%m-%d %H:%M:%S") + f" ({tz_str})"

# ---------------------------------------------------------------------------
# Build HTML string
# ---------------------------------------------------------------------------

def outcome_badge(outcome: str) -> str:
    if outcome == "Passed":
        return (f'<span style="display:inline-block;padding:2px 10px;border-radius:12px;'
                f'background:{C_GREEN_LIGHT};color:{C_GREEN_TEXT};font-weight:600;font-size:13px;">'
                f'Passed</span>')
    else:
        return (f'<span style="display:inline-block;padding:2px 10px;border-radius:12px;'
                f'background:{C_RED_LIGHT};color:{C_RED_TEXT};font-weight:600;font-size:13px;">'
                f'{html.escape(outcome)}</span>')


def class_table(class_name: str, test_list: list) -> str:
    short    = html.escape(short_class(class_name))
    count    = len(test_list)
    total_d  = sum(t.duration_sec for t in test_list)
    has_fail = any(t.outcome != "Passed" for t in test_list)
    header_color = C_RED_TEXT if has_fail else C_GREEN_TEXT
    header_bg    = C_RED_LIGHT if has_fail else C_GREEN_LIGHT

    rows = ""
    for i, t in enumerate(sorted(test_list, key=lambda x: x.method_name)):
        row_bg = C_WHITE if i % 2 == 0 else "#f9fafb"
        rows += (
            f'<tr style="background:{row_bg};">'
            f'<td style="padding:8px 12px;border-bottom:1px solid {C_BORDER};color:{C_TEXT};font-size:13px;">'
            f'{html.escape(t.method_name)}</td>'
            f'<td style="padding:8px 12px;border-bottom:1px solid {C_BORDER};text-align:center;">'
            f'{outcome_badge(t.outcome)}</td>'
            f'<td style="padding:8px 12px;border-bottom:1px solid {C_BORDER};text-align:right;'
            f'color:{C_TEXT_LIGHT};font-size:13px;font-family:monospace;">'
            f'{t.duration_sec:.2f}s</td>'
            f'</tr>\n'
        )

    return (
        f'<details style="margin-bottom:16px;border:1px solid {C_BORDER};border-radius:8px;'
        f'overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.06);">\n'
        f'<summary style="display:flex;align-items:center;justify-content:space-between;'
        f'padding:12px 16px;background:{header_bg};cursor:pointer;user-select:none;'
        f'list-style:none;-webkit-list-style:none;">\n'
        f'  <span style="font-weight:700;color:{header_color};font-size:15px;">'
        f'&#9654; {short}</span>\n'
        f'  <span style="font-size:13px;color:{C_TEXT_LIGHT};font-weight:500;">'
        f'{count} test{"s" if count != 1 else ""} &middot; {fmt_dur(total_d)}</span>\n'
        f'</summary>\n'
        f'<table style="width:100%;border-collapse:collapse;">\n'
        f'<thead><tr style="background:{C_BORDER};">\n'
        f'  <th style="padding:8px 12px;text-align:left;font-size:12px;color:{C_TEXT_LIGHT};'
        f'font-weight:600;border-bottom:2px solid {C_BORDER};">Test Name</th>\n'
        f'  <th style="padding:8px 12px;text-align:center;font-size:12px;color:{C_TEXT_LIGHT};'
        f'font-weight:600;border-bottom:2px solid {C_BORDER};">Outcome</th>\n'
        f'  <th style="padding:8px 12px;text-align:right;font-size:12px;color:{C_TEXT_LIGHT};'
        f'font-weight:600;border-bottom:2px solid {C_BORDER};">Duration</th>\n'
        f'</tr></thead>\n'
        f'<tbody>\n{rows}</tbody>\n'
        f'</table>\n'
        f'</details>\n'
    )


# ---- stat card helper ----
def stat_card(label, value, sub="", accent_color=C_ACCENT):
    return (
        f'<div style="background:{C_WHITE};border:1px solid {C_BORDER};border-radius:10px;'
        f'padding:18px 20px;flex:1 1 140px;max-width:240px;text-align:center;'
        f'box-shadow:0 1px 3px rgba(0,0,0,0.06);">\n'
        f'  <div style="font-size:11px;text-transform:uppercase;letter-spacing:1px;'
        f'color:{C_TEXT_LIGHT};margin-bottom:6px;">{label}</div>\n'
        f'  <div style="font-size:26px;font-weight:700;color:{accent_color};">{value}</div>\n'
        + (f'  <div style="font-size:12px;color:{C_TEXT_LIGHT};margin-top:4px;">{sub}</div>\n' if sub else "")
        + f'</div>\n'
    )


# ---- main HTML ----
html_doc = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>TestIT API Tests &#8212; Test Report</title>
<style>
  /* reset details/summary arrow across browsers */
  details > summary::-webkit-details-marker {{ display:none; }}
  details > summary {{ list-style:none; }}
  details[open] > summary > span:first-child {{ }}
</style>
</head>
<body style="margin:0;padding:0;background:{C_BG};font-family:'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;color:{C_TEXT};">

<!-- ============================================================
     HEADER / TITLE
     ============================================================ -->
<div style="background:{C_WHITE};border-bottom:1px solid {C_BORDER};padding:28px 0 20px;">
  <div style="max-width:960px;margin:0 auto;padding:0 24px;">
    <h1 style="margin:0;font-size:24px;font-weight:700;color:{C_TEXT};">
      TestIT API Tests &#8212; Test Report
    </h1>
    <p style="margin:6px 0 0;font-size:14px;color:{C_TEXT_LIGHT};">
      Generated from <code style="background:{C_BG};padding:1px 6px;border-radius:4px;font-size:13px;">results.trx</code>
    </p>
  </div>
</div>

<!-- ============================================================
     SUMMARY BAR  (colour-coded)
     ============================================================ -->
<div style="max-width:960px;margin:24px auto 0;padding:0 24px;">
  <div style="background:{summary_bg};border-radius:12px;padding:22px 28px;
              box-shadow:0 2px 8px rgba(0,0,0,0.15);color:#ffffff;">
    <!-- top row: big headline -->
    <div style="display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px;">
      <div>
        <div style="font-size:28px;font-weight:700;line-height:1.2;">
          {"&#10004; All {0} Tests Passed".format(total_tests) if all_passed
           else "&#10008; {0} of {1} Tests Failed".format(failed + errors, total_tests)}
        </div>
        <div style="font-size:14px;opacity:0.88;margin-top:4px;">
          Run on {run_start_local}
        </div>
      </div>
      <div style="font-size:48px;font-weight:700;line-height:1;">
        {pass_rate:.1f}<span style="font-size:20px;font-weight:400;opacity:0.8;">%</span>
      </div>
    </div>

    <!-- bottom row: metric pills -->
    <div style="display:flex;gap:24px;flex-wrap:wrap;margin-top:18px;">
      <div style="background:rgba(255,255,255,0.18);border-radius:8px;padding:8px 18px;min-width:100px;">
        <div style="font-size:11px;text-transform:uppercase;letter-spacing:0.8px;opacity:0.75;">Total</div>
        <div style="font-size:22px;font-weight:700;">{total_tests}</div>
      </div>
      <div style="background:rgba(255,255,255,0.18);border-radius:8px;padding:8px 18px;min-width:100px;">
        <div style="font-size:11px;text-transform:uppercase;letter-spacing:0.8px;opacity:0.75;">Passed</div>
        <div style="font-size:22px;font-weight:700;">{passed}</div>
      </div>
      <div style="background:rgba(255,255,255,0.18);border-radius:8px;padding:8px 18px;min-width:100px;">
        <div style="font-size:11px;text-transform:uppercase;letter-spacing:0.8px;opacity:0.75;">Failed</div>
        <div style="font-size:22px;font-weight:700;">{failed + errors}</div>
      </div>
      <div style="background:rgba(255,255,255,0.18);border-radius:8px;padding:8px 18px;min-width:100px;">
        <div style="font-size:11px;text-transform:uppercase;letter-spacing:0.8px;opacity:0.75;">Duration</div>
        <div style="font-size:22px;font-weight:700;">{fmt_dur(run_duration_sec)}</div>
      </div>
    </div>
  </div>
</div>

<!-- ============================================================
     PER-CLASS BREAKDOWN
     ============================================================ -->
<div style="max-width:960px;margin:32px auto 0;padding:0 24px;">
  <h2 style="font-size:18px;font-weight:600;color:{C_TEXT};margin:0 0 16px;">
    Per-Class Breakdown
  </h2>
  {"".join(class_table(cls, tsts) for cls, tsts in classes.items())}
</div>

<!-- ============================================================
     OVERALL STATS
     ============================================================ -->
<div style="max-width:960px;margin:32px auto 40px;padding:0 24px;">
  <h2 style="font-size:18px;font-weight:600;color:{C_TEXT};margin:0 0 16px;">
    Overall Stats
  </h2>
  <div style="display:flex;gap:16px;flex-wrap:wrap;">
    {stat_card("Slowest Test",
               fmt_dur(slowest.duration_sec) if slowest else "—",
               html.escape(slowest.method_name) if slowest else "",
               C_RED_TEXT)}
    {stat_card("Fastest Test",
               fmt_dur(fastest.duration_sec) if fastest else "—",
               html.escape(fastest.method_name) if fastest else "",
               C_GREEN_TEXT)}
    {stat_card("Avg Duration",
               fmt_dur(avg_dur),
               f"across {len(tests)} tests",
               C_ACCENT)}
  </div>
</div>

<!-- ============================================================
     FOOTER
     ============================================================ -->
<div style="border-top:1px solid {C_BORDER};padding:16px 0;text-align:center;">
  <span style="font-size:12px;color:{C_TEXT_LIGHT};">
    Report auto-generated &middot; source: results.trx
  </span>
</div>

</body>
</html>
"""

# ---------------------------------------------------------------------------
# Write
# ---------------------------------------------------------------------------
with open(HTML_PATH, "w", encoding="utf-8") as fh:
    fh.write(html_doc)

print(f"Report written to: {HTML_PATH}")
