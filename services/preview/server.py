"""Trusted transport; only the bubblewrap child parses Office content with LibreOffice.

Requires unprivileged Linux user/network namespaces and seccomp. Failure is terminal for
this attempt: there is deliberately no subprocess fallback without isolation.
"""
import ctypes
import errno
import hashlib
import http.server
import os
import platform
import shutil
import signal
import stat
import subprocess
import tempfile
import threading
import time
import zipfile
from pathlib import Path
from xml.etree import ElementTree

MAX_INPUT = 100 * 1024 * 1024
MAX_OUTPUT = 32 * 1024 * 1024
VERSION = os.environ.get("CONVERTER_VERSION", "libreoffice-sandbox-v1")
SLOTS = threading.BoundedSemaphore(1)
ROOT = Path("/jobs")
MIMES = {
    "application/vnd.openxmlformats-officedocument.wordprocessingml.document": ("docx", "writer_pdf_Export"),
    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet": ("xlsx", "calc_pdf_Export"),
    "application/vnd.openxmlformats-officedocument.presentationml.presentation": ("pptx", "impress_pdf_Export"),
}
PROFILE = '''<?xml version="1.0" encoding="UTF-8"?>
<oor:items xmlns:oor="http://openoffice.org/2001/registry">
<item oor:path="/org.openoffice.Office.Common/Security/Scripting">
<prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop>
<prop oor:name="DisableMacrosExecution" oor:op="fuse"><value>true</value></prop>
</item>
<item oor:path="/org.openoffice.Office.Common/Load">
<prop oor:name="UpdateDocMode" oor:op="fuse"><value>0</value></prop>
</item>
<item oor:path="/org.openoffice.Office.Calc/Content/Update">
<prop oor:name="Link" oor:op="fuse"><value>2</value></prop>
</item>
<item oor:path="/org.openoffice.Office.Writer/Content/Update">
<prop oor:name="Link" oor:op="fuse"><value>2</value></prop>
</item>
</oor:items>'''


def seccomp_filter():
    # libseccomp resolves names for the running ABI and emits a kernel BPF program.
    # bwrap installs it after namespace/mount setup; all LO descendants inherit it.
    if platform.system() != "Linux" or platform.machine() not in ("x86_64", "aarch64"):
        raise RuntimeError("SANDBOX_UNSUPPORTED")
    lib = ctypes.CDLL("libseccomp.so.2", use_errno=True)
    lib.seccomp_init.argtypes = [ctypes.c_uint32]
    lib.seccomp_init.restype = ctypes.c_void_p
    lib.seccomp_syscall_resolve_name.argtypes = [ctypes.c_char_p]
    lib.seccomp_syscall_resolve_name.restype = ctypes.c_int
    lib.seccomp_rule_add.argtypes = [ctypes.c_void_p, ctypes.c_uint32, ctypes.c_int, ctypes.c_uint]
    lib.seccomp_export_bpf.argtypes = [ctypes.c_void_p, ctypes.c_int]
    lib.seccomp_release.argtypes = [ctypes.c_void_p]
    ctx = lib.seccomp_init(0x7FFF0000)  # SCMP_ACT_ALLOW; deny network and escape primitives.
    if not ctx:
        raise RuntimeError("SANDBOX_UNAVAILABLE")
    fd = os.memfd_create("preview-seccomp", flags=0)
    try:
        for name in ("socket", "socketpair", "socketcall", "connect", "bind", "listen", "accept", "accept4",
                     "sendto", "sendmsg", "sendmmsg", "recvfrom", "recvmsg", "recvmmsg",
                     "io_uring_setup", "io_uring_enter", "io_uring_register", "ptrace", "mount", "umount2",
                     "unshare", "setns", "bpf", "keyctl", "perf_event_open", "userfaultfd"):
            syscall = lib.seccomp_syscall_resolve_name(name.encode("ascii"))
            if syscall >= 0 and lib.seccomp_rule_add(ctx, 0x00050000 | errno.EPERM, syscall, 0) != 0:
                raise RuntimeError("SANDBOX_FILTER_FAILED")
        if lib.seccomp_export_bpf(ctx, fd) != 0:
            raise RuntimeError("SANDBOX_FILTER_FAILED")
        os.lseek(fd, 0, os.SEEK_SET)
        return fd
    except BaseException:
        os.close(fd)
        raise
    finally:
        lib.seccomp_release(ctx)


def preflight(path, deadline):
    # Reject macros, embedded executable payloads and external resources even before LO.
    # Expansion is bounded; XML DTD/entities never enter ElementTree.
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        if len(entries) > 10000 or sum(e.file_size for e in entries) > 256 * 1024 * 1024:
            raise ValueError("ARCHIVE_LIMIT")
        for entry in entries:
            if time.monotonic() >= deadline:
                raise RuntimeError("CONVERTER_TIMEOUT")
            name = entry.filename.lower()
            if "vbaproject" in name or "/embeddings/" in name or "activex" in name:
                raise ValueError("ACTIVE_CONTENT")
            if entry.flag_bits & 1 or entry.file_size > 32 * 1024 * 1024:
                raise ValueError("ARCHIVE_LIMIT")
            if name.endswith(".rels"):
                if entry.file_size > 1024 * 1024:
                    raise ValueError("RELATIONSHIP_LIMIT")
                data = archive.read(entry)
                upper = data.upper()
                if b"<!DOCTYPE" in upper or b"<!ENTITY" in upper:
                    raise ValueError("XML_ENTITY")
                for relation in ElementTree.fromstring(data):
                    if relation.attrib.get("TargetMode", "").lower() == "external":
                        raise ValueError("EXTERNAL_RESOURCE")


def convert(work, extension, export_filter, deadline):
    profile = work / "profile" / "user"
    profile.mkdir(parents=True)
    (profile / "registrymodifications.xcu").write_text(PROFILE, encoding="utf-8")
    (work / "out").mkdir()
    (work / "tmp").mkdir()
    fd = seccomp_filter()
    # prlimit avoids Python preexec_fn in a multithreaded HTTP service.
    command = ["/usr/bin/prlimit", "--as=536870912:536870912", "--fsize=33554432:33554432",
               "--cpu=55:55", "--core=0:0", "--nofile=128:128", "--",
               "/usr/bin/bwrap", "--unshare-all", "--uid", "10001", "--gid", "10001",
               "--die-with-parent", "--new-session", "--cap-drop", "ALL",
               "--ro-bind", "/usr", "/usr", "--ro-bind", "/lib", "/lib", "--dir", "/etc",
               "--ro-bind", "/etc/fonts", "/etc/fonts", "--proc", "/proc", "--dev", "/dev",
               "--remount-ro", "/dev", "--bind", str(work), "/work", "--symlink", "/work/tmp", "/tmp", "--chdir", "/work",
               "--clearenv", "--setenv", "HOME", "/work", "--setenv", "PATH", "/usr/bin:/bin",
               "--setenv", "LANG", "C.UTF-8", "--setenv", "SAL_USE_VCLPLUGIN", "svp"]
    if Path("/lib64").exists():
        command += ["--ro-bind", "/lib64", "/lib64"]
    # /bin is a merged-/usr symlink on the image; expose only immutable binaries.
    command += ["--symlink", "usr/bin", "/bin", "--remount-ro", "/", "--seccomp", str(fd), "--",
                "/usr/bin/libreoffice", "-env:UserInstallation=file:///work/profile", "--headless",
                "--invisible", "--nologo", "--nodefault", "--nofirststartwizard", "--norestore",
                "--convert-to", "pdf:" + export_filter, "--outdir", "/work/out", "/work/input." + extension]
    process = None
    try:
        process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                   stderr=subprocess.DEVNULL, pass_fds=(fd,), start_new_session=True)
        try:
            code = process.wait(timeout=max(0.001, deadline - time.monotonic()))
        except subprocess.TimeoutExpired:
            raise RuntimeError("CONVERTER_TIMEOUT") from None
        if code != 0:
            raise RuntimeError("SANDBOX_OR_CONVERSION_FAILED")
    finally:
        os.close(fd)
        if process is not None:
            # Always kill the entire group, including descendants left after a launcher exit.
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait()
    result = work / "out" / "input.pdf"
    info = result.lstat()
    if not stat.S_ISREG(info.st_mode) or not 5 <= info.st_size <= MAX_OUTPUT:
        raise ValueError("OUTPUT_LIMIT")
    data = result.read_bytes()
    if not data.startswith(b"%PDF-"):
        raise ValueError("OUTPUT_INVALID")
    return data


class Handler(http.server.BaseHTTPRequestHandler):
    server_version = "Preview"

    def log_message(self, *args):
        pass  # No document names, hashes, body or dependency exception strings in logs.

    def setup(self):
        super().setup()
        self.connection.settimeout(65)

    def do_POST(self):
        if self.path != "/convert" or self.headers.get("Transfer-Encoding"):
            self.send_error(400)
            return
        mime = self.headers.get("Content-Type", "")
        digest = self.headers.get("X-Content-SHA256", "")
        try:
            size = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            size = 0
        if mime not in MIMES or not 0 < size <= MAX_INPUT or len(digest) != 64 or any(c not in "0123456789abcdefABCDEF" for c in digest):
            self.send_error(422)
            return
        if self.headers.get("X-Converter-Version") != VERSION:
            self.send_error(409)
            return
        if not SLOTS.acquire(blocking=False):
            self.send_error(503)
            return
        try:
            with tempfile.TemporaryDirectory(prefix="job-", dir=ROOT) as directory:
                work = Path(directory)
                extension, export_filter = MIMES[mime]
                source = work / ("input." + extension)
                hashed = hashlib.sha256()
                with source.open("xb") as output:
                    remaining = size
                    while remaining:
                        block = self.rfile.read(min(81920, remaining))
                        if not block:
                            raise ValueError("TRUNCATED_INPUT")
                        output.write(block)
                        hashed.update(block)
                        remaining -= len(block)
                if hashed.hexdigest() != digest.lower():
                    raise ValueError("SOURCE_HASH_CHANGED")
                deadline = time.monotonic() + 60
                preflight(source, deadline)
                data = convert(work, extension, export_filter, deadline)
                self.send_response(200)
                self.send_header("Content-Type", "application/pdf")
                self.send_header("Content-Length", str(len(data)))
                self.send_header("Cache-Control", "no-store")
                self.send_header("X-Converter-Version", VERSION)
                self.end_headers()
                self.wfile.write(data)
        except (Exception, MemoryError):
            self.send_error(503, "CONVERSION_UNAVAILABLE")
        finally:
            SLOTS.release()
            self.close_connection = True


class BoundedServer(http.server.ThreadingHTTPServer):
    # Bound transport/header threads as well as conversion concurrency.
    request_queue_size = 8
    transport_slots = threading.BoundedSemaphore(8)

    def process_request(self, request, address):
        if not self.transport_slots.acquire(blocking=False):
            self.shutdown_request(request)
            return
        try:
            super().process_request(request, address)
        except BaseException:
            self.transport_slots.release()
            raise

    def process_request_thread(self, request, address):
        try:
            super().process_request_thread(request, address)
        finally:
            self.transport_slots.release()


if __name__ == "__main__":
    if os.getuid() == 0 or not ROOT.is_dir():
        raise SystemExit("Non-root Linux sandbox service required")
    # /jobs is a dedicated bounded ephemeral mount; remove crash leftovers before accepting jobs.
    for entry in ROOT.glob("job-*"):
        if entry.is_dir() and not entry.is_symlink():
            shutil.rmtree(entry)
    BoundedServer(("0.0.0.0", 8002), Handler).serve_forever()
