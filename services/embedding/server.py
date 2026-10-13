"""Offline CPU E5 service. No request content or exception details are logged."""
import hashlib
import json
import os
import socket
import time
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"
os.environ["TOKENIZERS_PARALLELISM"] = "false"
import torch
from transformers import AutoModel, AutoTokenizer

MODEL_ID = "intfloat/multilingual-e5-small"
REVISION = "614241f622f53c4eeff9890bdc4f31cfecc418b3"
MODEL_VERSION = f"{MODEL_ID}@{REVISION}"
DIMENSIONS = 384
MAX_BODY = 112 * 1024 * 1024
MAX_CHARS = 16 * 1024 * 1024
MODEL_DIR = Path(os.environ.get("MODEL_DIR", "/app/model"))
manifest = json.loads(Path(__file__).with_name("model-manifest.json").read_text(encoding="utf-8-sig"))
for name, digest in manifest.items():
    with (MODEL_DIR / name).open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != digest:
            raise RuntimeError("MODEL_ASSET_IDENTITY")
torch.set_num_threads(max(1, min(4, int(os.environ.get("CPU_THREADS", "2")))))
torch.set_num_interop_threads(1)
tokenizer = AutoTokenizer.from_pretrained(MODEL_DIR, local_files_only=True, trust_remote_code=False, use_fast=True)
model = AutoModel.from_pretrained(MODEL_DIR, local_files_only=True, trust_remote_code=False, use_safetensors=True).to("cpu").eval()
if model.config.hidden_size != DIMENSIONS or not tokenizer.is_fast:
    raise RuntimeError("MODEL_CONFIGURATION")


class ContractError(Exception):
    pass


def require(condition):
    if not condition:
        raise ContractError()


def deadline_check(deadline):
    if time.monotonic() > deadline:
        raise TimeoutError()


def token_count(text):
    return len(tokenizer(text, add_special_tokens=True, truncation=False)["input_ids"])


def embed(data, deadline):
    require(data.get("modelVersion") == MODEL_VERSION)
    kind = data.get("kind")
    texts = data.get("texts")
    require(kind in ("query", "passage") and isinstance(texts, list) and 1 <= len(texts) <= 16)
    require(kind != "query" or len(texts) == 1)
    require(all(isinstance(t, str) and t.strip() and len(t) <= (1000 if kind == "query" else 32768) for t in texts))
    prefixed = [f"{kind}: {t}" for t in texts]
    # No truncation: indexing must repair chunking rather than lose document text.
    require(all(token_count(t) <= 512 for t in prefixed))
    deadline_check(deadline)
    batch = tokenizer(prefixed, max_length=512, padding=True, truncation=False, return_tensors="pt")
    with torch.inference_mode():
        hidden = model(**batch).last_hidden_state
        mask = batch["attention_mask"].unsqueeze(-1).bool()
        pooled = hidden.masked_fill(~mask, 0.0).sum(dim=1) / mask.sum(dim=1)
        vectors = torch.nn.functional.normalize(pooled, p=2, dim=1)
        require(bool(torch.isfinite(vectors).all()) and vectors.shape[1] == DIMENSIONS)
        require(bool((vectors.norm(dim=1) - 1).abs().lt(0.001).all()))
    deadline_check(deadline)
    return {"modelVersion": MODEL_VERSION, "dimensions": DIMENSIONS, "vectors": vectors.tolist()}


def chunk(data, deadline):
    require(data.get("modelVersion") == MODEL_VERSION and data.get("prefix") == "passage: ")
    require(data.get("targetTokens") == 300 and data.get("overlapTokens") == 50 and data.get("maxInputTokens") == 512)
    max_chunks = data.get("maxChunks")
    sections = data.get("sections")
    require(type(max_chunks) is int and 1 <= max_chunks <= 2000 and isinstance(sections, list) and len(sections) <= 1000)
    total = 0
    for section in sections:
        require(isinstance(section, dict) and isinstance(section.get("text"), str))
        location = section.get("location")
        require(location is None or isinstance(location, str) and len(location) <= 500)
        total += len(section["text"])
        require(total <= MAX_CHARS)
    chunks = []
    for section in sections:
        text = section["text"]
        start = 0
        while start < len(text):
            deadline_check(deadline)
            if not text[start:start + 8192].strip():
                # Whitespace carries no semantic tokens; skip in bounded windows.
                start += min(8192, len(text) - start)
                continue
            if len(chunks) == max_chunks:
                return result(chunks, True)
            # Bounded tokenizer allocation even for a 16M-character section.
            window = text[start:start + 8192]
            offsets = tokenizer(window, add_special_tokens=False, truncation=False, return_offsets_mapping=True)["offset_mapping"]
            require(bool(offsets))
            target_body = 300 - token_count("passage: ")
            end = offsets[min(target_body, len(offsets)) - 1][1]
            require(end > 0)
            piece = window[:end]
            count = token_count("passage: " + piece)
            # Prefix/re-tokenization can alter boundary tokens. Recheck actual complete input.
            while count > 300:
                end -= 1
                require(end > 0)
                piece = window[:end]
                count = token_count("passage: " + piece)
                deadline_check(deadline)
            require(count <= 512 and piece.strip())
            chunks.append({"ordinal": len(chunks), "text": piece, "tokenCount": count, "location": section.get("location")})
            if start + end >= len(text):
                break
            piece_offsets = tokenizer(piece, add_special_tokens=False, return_offsets_mapping=True)["offset_mapping"]
            overlap_start = piece_offsets[-50][0] if len(piece_offsets) > 50 else end
            # Repeated offsets (multi-token Unicode codepoints) must still make progress.
            start += overlap_start if 0 < overlap_start < end else end
    return result(chunks, False)


def result(chunks, partial):
    return {"modelVersion": MODEL_VERSION, "chunkerVersion": "e5-offset-v1-300-50", "chunks": chunks,
            "isPartial": partial, "reason": "CHUNK_LIMIT" if partial else None}


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    def log_message(self, *_args):
        pass

    def setup(self):
        super().setup()
        self.connection.settimeout(10)
        # setup runs before BaseHTTPRequestHandler reads the request line/headers.
        # One absolute budget covers slow headers, body, inference and response.
        self.deadline = time.monotonic() + 110
        self.watchdog = threading.Timer(115, lambda: os._exit(70))
        self.watchdog.daemon = True
        self.watchdog.start()

    def finish(self):
        try:
            super().finish()
        finally:
            # BaseRequestHandler invokes finish even when parsing/handling fails;
            # this also covers GET, malformed headers and disconnected clients.
            self.watchdog.cancel()

    def reply(self, status, data):
        body = json.dumps(data, ensure_ascii=False, allow_nan=False, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Connection", "close")
        self.end_headers()
        self.wfile.write(body)
        self.close_connection = True

    def do_GET(self):
        self.reply(200 if self.path == "/health" else 404, {"modelVersion": MODEL_VERSION, "dimensions": DIMENSIONS})

    def read_body(self, deadline):
        # HttpClient JsonContent normally uses chunked transfer encoding.
        body = bytearray()
        encoding = self.headers.get("Transfer-Encoding", "").lower()
        length = self.headers.get("Content-Length")
        require(not (encoding and length))
        if encoding:
            require(encoding == "chunked")
            while True:
                deadline_check(deadline)
                line = self.rfile.readline(128)
                require(line.endswith(b"\r\n") and len(line) < 128)
                size = int(line.strip(), 16)
                require(0 <= size <= MAX_BODY - len(body))
                if size == 0:
                    require(self.read_exact(2, deadline) == b"\r\n")
                    break
                part = self.read_exact(size, deadline)
                require(self.read_exact(2, deadline) == b"\r\n")
                body.extend(part)
        else:
            require(length is not None)
            size = int(length)
            require(0 <= size <= MAX_BODY)
            body = self.read_exact(size, deadline)
        deadline_check(deadline)
        return json.loads(body)

    def read_exact(self, size, deadline):
        body = bytearray()
        while len(body) < size:
            deadline_check(deadline)
            self.connection.settimeout(min(10, max(0.1, deadline - time.monotonic())))
            part = self.rfile.read1(min(65536, size - len(body)))
            require(bool(part))
            body.extend(part)
        return body

    def do_POST(self):
        deadline = self.deadline
        try:
            deadline_check(deadline)
            require(self.path in ("/embed", "/chunk"))
            data = self.read_body(deadline)
            require(isinstance(data, dict))
            output = embed(data, deadline) if self.path == "/embed" else chunk(data, deadline)
            self.reply(200, output)
        except (ContractError, ValueError, TypeError, KeyError):
            self.reply(422, {"error": "EMBEDDING_CONTRACT"})
        except (TimeoutError, socket.timeout):
            self.reply(503, {"error": "EMBEDDING_TIMEOUT"})
        except Exception:
            self.reply(503, {"error": "EMBEDDING_UNAVAILABLE"})


class Server(HTTPServer):
    request_queue_size = 4
    def handle_error(self, *_args):
        pass  # Never emit request/traceback to logs.


if __name__ == "__main__":
    # One process/one inference at a time prevents unbounded queued tensors/requests.
    Server(("0.0.0.0", 8001), Handler).serve_forever()
