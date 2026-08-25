namespace SilverScreen.Infrastructure.YouTube;

internal static class YtDlpHostScript
{
    public const string Script = """
import sys, os, json, io, traceback, threading, contextlib
from concurrent.futures import ThreadPoolExecutor

REAL_STDOUT = sys.stdout

def bootstrap_yt_dlp():
    try:
        import yt_dlp
        return yt_dlp
    except ImportError:
        pass
    
    yt_path = os.environ.get("SILVERSCREEN_YT_DLP_PATH")
    if yt_path and os.path.exists(yt_path):
        abs_path = os.path.abspath(yt_path)
        if abs_path not in sys.path:
            sys.path.insert(0, abs_path)
        dirname = os.path.dirname(abs_path)
        if dirname not in sys.path:
            sys.path.insert(0, dirname)
        try:
            import yt_dlp
            return yt_dlp
        except ImportError:
            pass
    return None

yt_dlp = bootstrap_yt_dlp()
if not yt_dlp:
    REAL_STDOUT.write(json.dumps({"type": "error", "message": "Failed to import yt_dlp"}) + "\n")
    REAL_STDOUT.flush()
    sys.exit(1)

try:
    with yt_dlp.YoutubeDL({"quiet": True, "no_warnings": True}) as _:
        pass
except Exception:
    pass

version = getattr(getattr(yt_dlp, "version", None), "__version__", "unknown")
REAL_STDOUT.write(json.dumps({
    "type": "ready",
    "version": str(version),
    "python": sys.version.split()[0]
}) + "\n")
REAL_STDOUT.flush()

stdout_lock = threading.Lock()
opts_lock = threading.Lock()

def write_response(data):
    line = json.dumps(data)
    with stdout_lock:
        REAL_STDOUT.write(line + "\n")
        REAL_STDOUT.flush()

class CustomLogger:
    def __init__(self, err_buf):
        self.err_buf = err_buf
    def debug(self, msg): pass
    def info(self, msg): pass
    def warning(self, msg):
        self.err_buf.write(f"WARNING: {msg}\n")
    def error(self, msg):
        self.err_buf.write(f"ERROR: {msg}\n")

def execute_request(req_id, args):
    try:
        err_buf = io.StringIO()
        out_buf = io.StringIO()
        with opts_lock:
            try:
                with contextlib.redirect_stderr(err_buf), contextlib.redirect_stdout(out_buf):
                    parser, opts, urls, ydl_opts = yt_dlp.parse_options(args)
            except SystemExit as pe:
                write_response({
                    "id": req_id,
                    "exit_code": pe.code if isinstance(pe.code, int) else (0 if pe.code is None else 1),
                    "stdout": out_buf.getvalue(),
                    "stderr": err_buf.getvalue()
                })
                return
            except Exception as pe:
                write_response({
                    "id": req_id,
                    "exit_code": 1,
                    "stdout": out_buf.getvalue(),
                    "stderr": f"{err_buf.getvalue()}\n{pe}".strip()
                })
                return

        ydl_opts["quiet"] = True
        ydl_opts["no_warnings"] = True
        ydl_opts["logger"] = CustomLogger(err_buf)

        try:
            with yt_dlp.YoutubeDL(ydl_opts) as ydl:
                ydl._out_files.out = out_buf
                ydl._out_files.error = err_buf
                ydl._out_files.screen = err_buf
                ret = ydl.download(urls)
                write_response({
                    "id": req_id,
                    "exit_code": ret if isinstance(ret, int) else 0,
                    "stdout": out_buf.getvalue(),
                    "stderr": err_buf.getvalue()
                })
        except SystemExit as se:
            write_response({
                "id": req_id,
                "exit_code": se.code if isinstance(se.code, int) else (0 if se.code is None else 1),
                "stdout": out_buf.getvalue(),
                "stderr": err_buf.getvalue()
            })
        except Exception as ex:
            write_response({
                "id": req_id,
                "exit_code": 1,
                "stdout": out_buf.getvalue(),
                "stderr": f"{err_buf.getvalue()}\n{ex}".strip()
            })
    except Exception as fatal:
        write_response({
            "id": req_id,
            "exit_code": 1,
            "stdout": "",
            "stderr": f"Fatal helper error: {fatal}\n{traceback.format_exc()}"
        })

executor = ThreadPoolExecutor(max_workers=4)

try:
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
        except Exception as e:
            write_response({"id": None, "exit_code": 1, "stdout": "", "stderr": f"Invalid JSON request: {e}"})
            continue

        req_id = req.get("id")
        action = req.get("action")

        if action == "run":
            args = req.get("args", [])
            executor.submit(execute_request, req_id, args)
        elif action == "ping":
            write_response({"id": req_id, "status": "pong", "version": version})
        elif action == "shutdown":
            break
        else:
            write_response({"id": req_id, "exit_code": 1, "stdout": "", "stderr": f"Unknown action: {action}"})
finally:
    executor.shutdown(wait=False)
""";
}
