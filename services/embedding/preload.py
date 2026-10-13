"""Setup only: fetch pinned assets or copy the controller's verified offline cache."""
import argparse
import hashlib
import json
import shutil
import urllib.request
from pathlib import Path

REVISION = "614241f622f53c4eeff9890bdc4f31cfecc418b3"
parser = argparse.ArgumentParser()
parser.add_argument("--cache", type=Path, help="Offline source directory; omit to download at setup time")
args = parser.parse_args()
root = Path(__file__).resolve().parent
manifest = json.loads((root / "model-manifest.json").read_text(encoding="utf-8-sig"))
target = root / "model"
target.mkdir(exist_ok=True)
for name, digest in manifest.items():
    destination = target / name
    temporary = target / (name + ".download")
    try:
        if args.cache:
            shutil.copyfile(args.cache / name, temporary)
        else:
            url = f"https://huggingface.co/intfloat/multilingual-e5-small/resolve/{REVISION}/{name}"
            with urllib.request.urlopen(url, timeout=120) as response, temporary.open("wb") as output:
                shutil.copyfileobj(response, output)
        with temporary.open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest() != digest:
                raise RuntimeError("MODEL_ASSET_IDENTITY: " + name)
        temporary.replace(destination)
    finally:
        temporary.unlink(missing_ok=True)
print("Pinned model assets prepared; no inference performed.")
