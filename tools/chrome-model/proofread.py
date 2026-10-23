"""Chrome's proofreader model without Chrome (BlueEdit #104), for BlueEdit's ChromeModelChecker.

Loads a LiteRT-LM model file once (the one Chrome downloads: "Tinymodel Proofreader") and proofreads lines of JSON on
stdin, one per line: {"text": "..."} -> {"corrected": "..."} (or {"error": "..."}). The prompt is the text and the
model's turn mark, " <ctrl1>", as the template in the model file has it. Chrome picks its answer at random among the
likeliest (two runs of the same set differed in 6 of 59 sentences); the likeliest answer alone found 14 of 24 errors on
the British academic set against Chrome's 21, top-k 3 at temperature 0.5 with a fixed seed 17 (2 false alarms either
way) — the default here, so the same text always gets the same answer. Needs litert-lm (pip install litert-lm).

    python proofread.py <model file>
"""
import json
import os
import sys

import litert_lm


def sampler():
    """top-k 3, temperature 0.5, seed 1; BLUEEDIT_CHROME_SAMPLER = "top_k,temperature,seed" for measuring others,
    "greedy" for the most likely answer every time."""
    spec = os.environ.get("BLUEEDIT_CHROME_SAMPLER", "3,0.5,1")
    if spec == "greedy":
        return None
    k, t, seed = spec.split(",")
    return litert_lm.SamplerConfig(top_k=int(k), temperature=float(t), seed=int(seed))


def proofread(engine, text):
    out = ""
    with engine.create_session(apply_prompt_template=False, sampler_config=sampler(), max_output_tokens=len(text) + 64) as session:
        session.run_prefill([text + " <ctrl1>"])
        for r in session.run_decode_async():
            out += r.texts[0]
            # the model ends its answer itself; this only guards against one that runs on
            if len(out) > len(text) * 2 + 40:
                break
    return out.split("<", 1)[0].strip()


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stdin.reconfigure(encoding="utf-8")
    with litert_lm.Engine(sys.argv[1]) as engine:
        print(json.dumps({"ready": True}), flush=True)
        for line in sys.stdin:
            if not line.strip():
                continue
            try:
                text = json.loads(line)["text"]
                print(json.dumps({"corrected": proofread(engine, text)}, ensure_ascii=False), flush=True)
            except Exception as e:  # one bad line must not end the others
                print(json.dumps({"error": str(e)}), flush=True)


if __name__ == "__main__":
    main()
