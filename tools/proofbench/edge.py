"""The English sentences of bench.py through Edge's built-in Proofreader (on-device, English only), added to results.json.

Edge Canary with hidden switches, driven by cdp.ps1 (headless, its own profile; the model is downloaded on first use):
    python tools\proofbench\edge.py <cdp.ps1> <profile dir> [<msedge.exe>]
"""
import json, os, subprocess, sys
sys.path.insert(0, os.path.dirname(__file__))
import bench

cdp, profile = sys.argv[1], sys.argv[2]
edge = sys.argv[3] if len(sys.argv) > 3 else os.path.join(os.environ["LOCALAPPDATA"], "Microsoft", "Edge SxS", "Application", "msedge.exe")
tests = [t for t in bench.TESTS if t[0] == "en"]

script = """(async () => {
  let step = 'availability';
  try {
    step += ' ' + await Proofreader.availability({ expectedInputLanguages: ['en'] });
    const session = await Proofreader.create({ expectedInputLanguages: ['en'] });
    const out = [];
    for (const text of %s) {
      step = text;
      const s = performance.now();
      let r = null;
      for (let attempt = 0; attempt < 3 && !r; attempt++)
        try { r = await session.proofread(text); } catch (e) { if (attempt == 2) throw e; }
      out.push({ text, ms: Math.round(performance.now() - s), corrected: r.correctedInput,
        errors: (r.corrections || []).map(c => ({ original: text.slice(c.startIndex, c.endIndex), replacement: c.correction })) });
    }
    return JSON.stringify(out);
  } catch (e) { return 'ERROR at ' + step + ': ' + e; }
})()""" % json.dumps([t[2] for t in tests])

js = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_edge.js")
open(js, "w", encoding="utf-8").write(script)
ps = (f"& '{cdp}' -Profile '{profile}' -Edge '{edge}' -Url 'https://example.com/' -Scripts (Get-Content -Raw '{js}') "
      "-Extra @('--enable-features=AIProofreadingAPI', '--enable-blink-features=AIProofreaderAPI')")
out = subprocess.run(["pwsh", "-NoProfile", "-Command", ps], capture_output=True, text=True, encoding="utf-8").stdout.strip()
os.remove(js)
if not out.startswith("["):
    sys.exit("Edge: " + out[-2000:])

rows = []
for (lang, kind, text, expected), r in zip(tests, json.loads(out)):
    rows.append({"lang": lang, "kind": kind, "text": text, "corrected": r["corrected"], "errors": r["errors"], "ms": r["ms"],
                 "result": bench.score(text, expected, r["corrected"])})
results = json.load(open(bench.OUT, encoding="utf-8"))
results["Edge Proofreader"] = {"rows": rows}
json.dump(results, open(bench.OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(bench.summary({"Edge Proofreader": results["Edge Proofreader"]}))
