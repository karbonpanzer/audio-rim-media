#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Rim-Radio Cover Downloader - Fast Mode (Selectable Redo + Genre Folders)
- Start All / Redo Selected / Redo Failed
- Saves into per-genre subfolders under the chosen output directory
  e.g., Output/Rock/Artist__Album__Year.jpg
"""

import csv, io, json, os, queue, re, sys, threading, time, tkinter as tk
from tkinter import ttk, filedialog, messagebox
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime
from difflib import SequenceMatcher

LOG_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "rim_radio_downloader.log")

def log_line(text):
    try:
        with open(LOG_PATH, "a", encoding="utf-8") as f:
            f.write(f"[{datetime.now().isoformat(timespec='seconds')}] {text}\n")
    except Exception:
        pass

# Optional deps
try:
    import requests
    from requests.adapters import HTTPAdapter
    from urllib3.util.retry import Retry
except Exception as e:
    requests = None
    HTTPAdapter = None
    Retry = None
    log_line(f"requests import failed: {e}")

try:
    from PIL import Image, ImageTk
    PIL_OK = True
except Exception as e:
    PIL_OK = False
    Image = ImageTk = None
    log_line(f"Pillow import failed: {e}")

UA = "RimRadioAlbumFetcher/fast-select-genrefolders"
REQ_TIMEOUT = 8
DEFAULT_PAR = 12
DEFAULT_LIMIT = 10
DEFAULT_YEAR_TOL = 1

_session = None
_json_cache = {}
_bytes_cache = {}

def get_session():
    global _session
    if _session is None and requests:
        try:
            s = requests.Session()
            s.headers.update({"User-Agent": UA})
            if HTTPAdapter and Retry:
                retry = Retry(total=2, backoff_factor=0.2, status_forcelist=[429, 500, 502, 503, 504])
                adapter = HTTPAdapter(pool_connections=64, pool_maxsize=64, max_retries=retry)
                s.mount("http://", adapter); s.mount("https://", adapter)
            _session = s
        except Exception as e:
            log_line(f"requests.Session failed: {e}")
            _session = None
    return _session

def http_get_json(url, params=None, headers=None, timeout=REQ_TIMEOUT):
    key = url + "?" + "&".join(f"{k}={params[k]}" for k in sorted(params or {}))
    if key in _json_cache:
        return _json_cache[key]
    try:
        s = get_session()
        hdrs = {"Accept": "application/json", "User-Agent": UA}
        if headers: hdrs.update(headers)
        if s:
            r = s.get(url, params=params, headers=hdrs, timeout=timeout)
            r.raise_for_status()
            data = r.json()
        else:
            import urllib.request, urllib.parse
            full = f"{url}?{urllib.parse.urlencode(params or {})}" if params else url
            req = urllib.request.Request(full, headers=hdrs)
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                text = resp.read().decode("utf-8", errors="replace")
            data = json.loads(text)
        _json_cache[key] = data
        return data
    except Exception as e:
        log_line(f"http_get_json failed {url}: {e}")
        raise

def http_get_bytes(url, timeout=max(REQ_TIMEOUT, 10), headers=None):
    if url in _bytes_cache:
        return _bytes_cache[url]
    try:
        s = get_session()
        hdrs = {"User-Agent": UA}
        if headers: hdrs.update(headers)
        if s:
            r = s.get(url, headers=hdrs, timeout=timeout)
            r.raise_for_status()
            data = r.content
        else:
            import urllib.request
            req = urllib.request.Request(url, headers=hdrs)
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                data = resp.read()
        _bytes_cache[url] = data
        return data
    except Exception as e:
        log_line(f"http_get_bytes failed {url}: {e}")
        raise

def slug(s):
    s = (s or "").strip().replace("/", "_")
    s = re.sub(r"\s+", "_", s)
    s = re.sub(r"[^A-Za-z0-9._+-]+", "", s)
    return s[:180]

def parse_year_from_date(d):
    if not d:
        return None
    m = re.match(r"^(\d{4})", str(d))
    return int(m.group(1)) if m else None

def soft_year_filter(candidates, target_year, tol):
    if target_year is None:
        return candidates
    Y = int(target_year); tol = max(0, int(tol))
    out = []
    for c in candidates:
        cy = c.get("year")
        if cy is None or abs(int(cy) - Y) <= tol:
            out.append(c)
    return out

def title_similarity(a, b):
    a = (a or "").lower()
    b = (b or "").lower()
    if not a or not b:
        return 0.0
    return SequenceMatcher(None, re.sub(r"[^a-z0-9]+","",a), re.sub(r"[^a-z0-9]+","",b)).ratio()

def dedup_by_image(candidates):
    seen = set(); out = []
    for c in candidates:
        u = c.get("image")
        if u and u not in seen:
            seen.add(u); out.append(c)
    return out

# Providers
def provider_itunes(artist, album, limit=DEFAULT_LIMIT):
    term = " ".join([artist or "", album or ""]).strip()
    if not term: return []
    data = http_get_json("https://itunes.apple.com/search",
                         {"term": term, "media": "music", "entity": "album", "limit": limit})
    out = []
    for r in data.get("results", []) or []:
        art = r.get("artworkUrl100") or r.get("artworkUrl60")
        full = thumb = None
        if art:
            full  = re.sub(r"/\d+x\d+bb\.(jpg|png)$", r"/1000x1000bb.\1", art)
            thumb = re.sub(r"/\d+x\d+bb\.(jpg|png)$", r"/200x200bb.\1", art)
        out.append({
            "provider": "iTunes",
            "image": full or art,
            "thumb": thumb or full or art,
            "title": r.get("collectionName"),
            "artist": r.get("artistName"),
            "year": parse_year_from_date(r.get("releaseDate")),
        })
    return out

def provider_deezer(artist, album, limit=DEFAULT_LIMIT, fast=True):
    q = " ".join([artist or "", album or ""]).strip()
    if not q: return []
    data = http_get_json("https://api.deezer.com/search/album", {"q": q})
    out = []
    for r in (data.get("data") or [])[:limit]:
        cover_m = r.get("cover_medium") or r.get("cover") or r.get("cover_big") or r.get("cover_xl")
        cover_f = r.get("cover_xl") or r.get("cover_big") or r.get("cover_medium") or r.get("cover")
        year = None
        if not fast:
            try:
                detail = http_get_json(f"https://api.deezer.com/album/{r.get('id')}")
                year = parse_year_from_date(detail.get("release_date"))
            except Exception:
                year = None
        out.append({
            "provider": "Deezer",
            "image": cover_f,
            "thumb": cover_m or cover_f,
            "title": r.get("title"),
            "artist": (r.get("artist") or {}).get("name"),
            "year": year,
        })
    return out

def provider_musicbrainz_caa(artist, album, year=None, limit=6):
    if not (artist or album): return []
    q = []
    if artist: q.append(f'artist:"{artist}"')
    if album:  q.append(f'release:"{album}"')
    if year:   q.append(f"date:{year}")
    params = {"fmt":"json","query":" AND ".join(q) if q else album,"limit":limit}
    data = http_get_json("https://musicbrainz.org/ws/2/release/", params, headers={"User-Agent": UA})
    out = []
    for r in data.get("releases", []) or []:
        mbid = r.get("id")
        if not mbid: continue
        for suffix in ("front-1200", "front", "front-500"):
            url = f"https://coverartarchive.org/release/{mbid}/{suffix}"
            try:
                b = http_get_bytes(url, timeout=6, headers={"Accept":"image/*"})
                if b and len(b) > 400:
                    out.append({
                        "provider":"CoverArtArchive",
                        "image": url,
                        "thumb": url,
                        "title": r.get("title"),
                        "artist": None,
                        "year": parse_year_from_date(r.get("date"))
                    })
                    break
            except Exception:
                continue
    return out

# Choice UI
class ChoiceDialog(tk.Toplevel):
    def __init__(self, parent, artist, album, year, candidates, show_previews=True):
        super().__init__(parent)
        self.title("Choose album cover")
        self.resizable(True, True)
        self.transient(parent); self.grab_set()

        self.selected_url = None
        self.retry_payload = None
        self.candidates = candidates
        self.show_previews = show_previews

        header = ttk.Frame(self); header.pack(fill="x", padx=8, pady=8)
        ttk.Label(header, text="Artist").grid(row=0, column=0, sticky="e")
        ttk.Label(header, text="Album").grid(row=1, column=0, sticky="e")
        ttk.Label(header, text="Year").grid(row=2, column=0, sticky="e")
        self.artist_var = tk.StringVar(value=artist or "")
        self.album_var  = tk.StringVar(value=album or "")
        self.year_var   = tk.StringVar(value=str(year) if year else "")
        ttk.Entry(header, textvariable=self.artist_var, width=48).grid(row=0, column=1, sticky="we", padx=6)
        ttk.Entry(header, textvariable=self.album_var,  width=48).grid(row=1, column=1, sticky="we", padx=6)
        ttk.Entry(header, textvariable=self.year_var,   width=10).grid(row=2, column=1, sticky="w",  padx=6)
        header.grid_columnconfigure(1, weight=1)

        body = ttk.Frame(self); body.pack(fill="both", expand=True, padx=8, pady=4)
        self.canvas = tk.Canvas(body, highlightthickness=0)
        self.scroll = ttk.Scrollbar(body, orient="vertical", command=self.canvas.yview)
        self.inner = ttk.Frame(self.canvas)
        self.inner.bind("<Configure>", lambda e: self.canvas.configure(scrollregion=self.canvas.bbox("all")))
        self.canvas.create_window((0, 0), window=self.inner, anchor="nw")
        self.canvas.configure(yscrollcommand=self.scroll.set)
        self.canvas.pack(side="left", fill="both", expand=True); self.scroll.pack(side="right", fill="y")

        btns = ttk.Frame(self); btns.pack(fill="x", padx=8, pady=8)
        ttk.Button(btns, text="Skip this album", command=self._skip).pack(side="left", padx=4)
        ttk.Button(btns, text="Retry with changes", command=self._retry).pack(side="left", padx=4)
        ttk.Button(btns, text="Cancel", command=self._cancel).pack(side="right", padx=4)
        ttk.Button(btns, text="Use selected", command=self._confirm).pack(side="right", padx=4)

        self._populate_cards()
        self.bind("<Escape>", lambda e: self._cancel())
        self.bind("s", lambda e: self._skip())
        self.bind("r", lambda e: self._retry())
        self.geometry("1000x700"); self.wait_visibility(); self.focus_set()

    def _populate_cards(self):
        cols = 3
        for i, c in enumerate(self.candidates):
            frame = ttk.Frame(self.inner, borderwidth=1, relief="solid", padding=6)
            r, col = divmod(i, cols)
            frame.grid(row=r, column=col, padx=8, pady=8, sticky="nsew")

            top = ttk.Frame(frame); top.pack(fill="x")
            ttk.Radiobutton(top, text="Select", value=c.get("image"),
                            command=lambda u=c.get("image"): self._select(u)).pack(side="left")
            ttk.Button(top, text="Use this", command=lambda u=c.get("image"): self._use_now(u)).pack(side="right")

            meta = f"{c.get('provider')}  |  {c.get('artist') or ''}  |  {c.get('title') or ''}  |  {c.get('year') or ''}"
            ttk.Label(frame, text=meta, justify="center", wraplength=280).pack(pady=(6,4))

            if self.show_previews and PIL_OK:
                lbl = ttk.Label(frame, text="[loading preview]", padding=8); lbl.pack()
                def loadthumb(url=c.get("thumb") or c.get("image"), L=lbl):
                    try:
                        data = http_get_bytes(url, timeout=6)
                        im = Image.open(io.BytesIO(data))
                        im.thumbnail((220,220), Image.LANCZOS if hasattr(Image, "LANCZOS") else Image.BICUBIC)
                        img = ImageTk.PhotoImage(im)
                        L.configure(image=img, text=""); L.image = img
                    except Exception as e:
                        log_line(f"thumb fail {url}: {e}")
                        L.configure(text="[preview failed]")
                threading.Thread(target=loadthumb, daemon=True).start()

        for c in range(cols):
            self.inner.grid_columnconfigure(c, weight=1)

    def _use_now(self, url):
        self.selected_url = url; self.grab_release(); self.destroy()

    def _select(self, url):
        self.selected_url = url

    def _confirm(self):
        if not self.selected_url:
            messagebox.showwarning("No selection", "Pick a result or click Skip.")
            return
        self.grab_release(); self.destroy()

    def _cancel(self):
        self.selected_url = None; self.grab_release(); self.destroy()

    def _skip(self):
        self.selected_url = None; self.grab_release(); self.destroy()

    def _retry(self):
        a = self.artist_var.get().strip()
        b = self.album_var.get().strip()
        y = self.year_var.get().strip()
        yint = int(y) if y.isdigit() else None
        self.retry_payload = ("retry", a, b, yint)
        self.grab_release(); self.destroy()

# App
class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Rim-Radio Cover Downloader - Fast Selectable (Genre Folders)")
        self.geometry("1280x780"); self.minsize(1024, 660)

        self.csv_path = tk.StringVar()
        self.out_dir = tk.StringVar()
        self.limit_var = tk.IntVar(value=0)
        self.status_var = tk.StringVar(value="Idle")
        self.progress_var = tk.DoubleVar(value=0.0)
        self.ask_always = tk.BooleanVar(value=False)
        self.year_tolerance = tk.IntVar(value=DEFAULT_YEAR_TOL)
        self.overwrite_existing = tk.BooleanVar(value=False)

        # speed controls
        self.parallelism = tk.IntVar(value=DEFAULT_PAR)
        self.max_per_provider = tk.IntVar(value=DEFAULT_LIMIT)
        self.show_previews = tk.BooleanVar(value=False)  # off for speed
        self.auto_pick = tk.BooleanVar(value=True)

        # providers
        self.use_itunes = tk.BooleanVar(value=True)
        self.use_deezer = tk.BooleanVar(value=True)
        self.use_mb = tk.BooleanVar(value=False)

        self.rows = []
        self.queue = queue.Queue()
        self.worker = None
        self.stop_flag = threading.Event()

        self._build_ui()

    def _build_ui(self):
        pad = 8
        top = ttk.Frame(self); top.pack(fill="x", padx=pad, pady=pad)
        ttk.Label(top, text="CSV file:").grid(row=0, column=0, sticky="w")
        ttk.Entry(top, textvariable=self.csv_path, width=68).grid(row=0, column=1, sticky="we", padx=(4,4))
        ttk.Button(top, text="Browse...", command=self.browse_csv).grid(row=0, column=2, sticky="w")

        ttk.Label(top, text="Output folder:").grid(row=1, column=0, sticky="w", pady=(6,0))
        ttk.Entry(top, textvariable=self.out_dir, width=68).grid(row=1, column=1, sticky="we", padx=(4,4), pady=(6,0))
        ttk.Button(top, text="Browse...", command=self.browse_out).grid(row=1, column=2, sticky="w", pady=(6,0))

        ttk.Label(top, text="Limit rows (0=all):").grid(row=0, column=3, sticky="e", padx=(12,0))
        ttk.Entry(top, textvariable=self.limit_var, width=6).grid(row=0, column=4, sticky="w")
        ttk.Label(top, text="Year tol ±").grid(row=1, column=3, sticky="e", padx=(12,0))
        ttk.Entry(top, textvariable=self.year_tolerance, width=4).grid(row=1, column=4, sticky="w")
        ttk.Checkbutton(top, text="Always ask", variable=self.ask_always).grid(row=0, column=5, padx=(12,0))
        ttk.Checkbutton(top, text="Overwrite existing", variable=self.overwrite_existing).grid(row=1, column=5, padx=(12,0))

        prov = ttk.LabelFrame(self, text="Providers"); prov.pack(fill="x", padx=pad, pady=(0,pad))
        ttk.Checkbutton(prov, text="iTunes", variable=self.use_itunes).grid(row=0, column=0, sticky="w", padx=6, pady=4)
        ttk.Checkbutton(prov, text="Deezer", variable=self.use_deezer).grid(row=0, column=1, sticky="w", padx=6, pady=4)
        ttk.Checkbutton(prov, text="MusicBrainz + CAA", variable=self.use_mb).grid(row=0, column=2, sticky="w", padx=6, pady=4)

        spd = ttk.LabelFrame(self, text="Speed/Behavior"); spd.pack(fill="x", padx=pad, pady=(0,pad))
        ttk.Label(spd, text="Parallel requests").grid(row=0, column=0, sticky="e")
        ttk.Entry(spd, textvariable=self.parallelism, width=5).grid(row=0, column=1, sticky="w", padx=(4,16))
        ttk.Label(spd, text="Max per provider").grid(row=0, column=2, sticky="e")
        ttk.Entry(spd, textvariable=self.max_per_provider, width=5).grid(row=0, column=3, sticky="w", padx=(4,16))
        ttk.Checkbutton(spd, text="Show previews (slower)", variable=self.show_previews).grid(row=0, column=4, sticky="w", padx=(4,16))
        ttk.Checkbutton(spd, text="Auto-pick when confident", variable=self.auto_pick).grid(row=0, column=5, sticky="w")

        mid = ttk.Frame(self); mid.pack(fill="both", expand=True, padx=pad, pady=(0, pad))
        self.tree = ttk.Treeview(mid, columns=("genre","artist","album","year","status"), show="headings", height=18, selectmode="extended")
        for col, w in [("genre",120), ("artist",240), ("album",380), ("year",80), ("status",480)]:
            self.tree.heading(col, text=col.capitalize())
            self.tree.column(col, width=w, anchor="w")
        self.tree.pack(side="left", fill="both", expand=True)
        sb = ttk.Scrollbar(mid, orient="vertical", command=self.tree.yview); sb.pack(side="left", fill="y")
        self.tree.configure(yscroll=sb.set)

        right = ttk.Frame(mid); right.pack(side="left", fill="both", expand=True, padx=(pad,0))
        ttk.Label(right, text="Log").pack(anchor="w")
        self.log = tk.Text(right, height=20, wrap="word"); self.log.pack(fill="both", expand=True)

        bottom = ttk.Frame(self); bottom.pack(fill="x", padx=pad, pady=(0, pad))
        self.pb = ttk.Progressbar(bottom, variable=self.progress_var, maximum=100); self.pb.pack(fill="x", expand=True, side="left")
        ttk.Label(bottom, textvariable=self.status_var, width=16).pack(side="left", padx=(8,0))
        ttk.Button(bottom, text="Load CSV", command=self.load_csv).pack(side="right", padx=(8,0))
        ttk.Button(bottom, text="Start All", command=self.start_all).pack(side="right", padx=(8,0))
        ttk.Button(bottom, text="Redo Selected", command=self.start_selected).pack(side="right", padx=(8,0))
        ttk.Button(bottom, text="Redo Failed", command=self.start_failed).pack(side="right")

        def tk_oops(exc, val, tb):
            log_line(f"Tk error: {exc}: {val}")
        self.report_callback_exception = tk_oops

    def browse_csv(self):
        p = filedialog.askopenfilename(title="Select CSV", filetypes=[("CSV","*.csv"), ("All files","*.*")])
        if p: self.csv_path.set(p)

    def browse_out(self):
        p = filedialog.askdirectory(title="Select output folder")
        if p: self.out_dir.set(p)

    def append_log(self, text):
        self.log.insert("end", text + "\n"); self.log.see("end"); self.log.update_idletasks()
        log_line(text)

    def load_csv(self):
        path = self.csv_path.get().strip()
        if not path or not os.path.isfile(path):
            messagebox.showerror("Error", "Choose a valid CSV file."); return
        try:
            with open(path, "r", encoding="utf-8") as f:
                reader = csv.DictReader(f)
                def norm(h): return (h or "").strip().lower()
                header_map = {norm(h): h for h in (reader.fieldnames or [])}
                def get(row, key, default=""):
                    for k in (key, key.replace("_"," "), key.replace("  "," "), key.replace("why it scales","why")):
                        h = header_map.get(k)
                        if h and h in row: return (row.get(h) or "").strip()
                    for h in row.keys():
                        if norm(h) == key: return (row.get(h) or "").strip()
                    return default
                self.rows = []
                for row in reader:
                    self.rows.append({
                        "Genre": get(row, "genre"),
                        "Artist": get(row, "artist"),
                        "Album": get(row, "album"),
                        "Year": get(row, "year"),
                        "Why": get(row, "why it scales"),
                    })
        except Exception as e:
            messagebox.showerror("Error", f"Failed to read CSV: {e}"); log_line(f"CSV read error: {e}"); return
        self.refresh_tree(); self.append_log(f"Loaded {len(self.rows)} rows from CSV.")

    def refresh_tree(self):
        self.tree.delete(*self.tree.get_children())
        for r in self.rows:
            self.tree.insert("", "end", values=(r["Genre"], r["Artist"], r["Album"], r["Year"], ""))

    def _collect_failed_indices(self):
        failed = []
        for i, iid in enumerate(self.tree.get_children()):
            status = self.tree.item(iid, "values")[-1]
            if any(k in str(status) for k in ("Not found", "Error", "Skipped")):
                failed.append(i)
        return failed

    def _collect_selected_indices(self):
        indices = []
        all_iids = list(self.tree.get_children())
        for iid in self.tree.selection():
            try:
                indices.append(all_iids.index(iid))
            except ValueError:
                pass
        return sorted(set(indices))

    def start_all(self):
        limit = self.limit_var.get() or 0
        if limit > 0:
            indices = list(range(min(limit, len(self.rows))))
        else:
            indices = list(range(len(self.rows)))
        self._start(indices, label="All")

    def start_selected(self):
        indices = self._collect_selected_indices()
        if not indices:
            messagebox.showinfo("Nothing selected", "Select one or more rows in the table first.")
            return
        self._start(indices, label="Selected")

    def start_failed(self):
        indices = self._collect_failed_indices()
        if not indices:
            messagebox.showinfo("No failed rows", "No rows marked Not found / Error / Skipped.")
            return
        self._start(indices, label="Failed")

    def _start(self, indices, label="Run"):
        if not self.rows:
            messagebox.showwarning("No data", "Load the CSV first."); return
        out = self.out_dir.get().strip()
        if not out:
            messagebox.showwarning("No output", "Choose an output folder."); return
        os.makedirs(out, exist_ok=True)
        if self.worker and self.worker.is_alive():
            messagebox.showinfo("Working", "A download is already in progress."); return
        self.stop_flag.clear()
        self.status_var.set(f"Running ({label})"); self.progress_var.set(0)
        self.append_log(f"Starting {label.lower()} run for {len(indices)} item(s)...")
        self.worker = threading.Thread(target=self._worker_thread, args=(out, indices), daemon=True)
        self.worker.start(); self.after(100, self._poll_queue)

    def stop(self):
        self.stop_flag.set(); self.status_var.set("Stopping...")

    def _poll_queue(self):
        try:
            while True:
                fn, args = self.queue.get_nowait(); fn(*args)
        except queue.Empty:
            pass
        if self.worker and self.worker.is_alive():
            self.after(100, self._poll_queue)
        else:
            self.status_var.set("Idle")

    def _set_row_status(self, idx, text):
        iid = self.tree.get_children()[idx]
        vals = list(self.tree.item(iid, "values")); vals[-1] = text
        self.tree.item(iid, values=vals)

    def choose_url_ui(self, artist, album, year, candidates):
        dlg = ChoiceDialog(self, artist, album, year, candidates, show_previews=False)
        self.wait_window(dlg)
        if dlg.retry_payload: return dlg.retry_payload
        return dlg.selected_url

    def _maybe_autopick(self, artist, album, year, candidates):
        if not candidates: return None
        target_title = album or ""
        target_year = int(year) if year and str(year).isdigit() else None
        tol = int(self.year_tolerance.get())
        best = None; best_score = 0.0
        for c in candidates:
            score = title_similarity(target_title, c.get("title"))
            cy = c.get("year")
            if target_year is not None and cy is not None and abs(int(cy) - target_year) > tol:
                continue
            if score > best_score:
                best_score = score; best = c
        return best.get("image") if best and best_score >= 0.92 else None

    def _worker_thread(self, out_dir, indices):
        total = len(indices)
        done = 0
        for pos, idx in enumerate(indices):
            if self.stop_flag.is_set():
                self.queue.put((self.append_log, (f"Stopped by user at item {pos+1}/{total}.",))); break

            r = self.rows[idx]
            artist = r["Artist"]; album = r["Album"]; year = (r["Year"].strip() if r["Year"] else None)
            genre = r["Genre"]

            def rowlog(msg): self.queue.put((self.append_log, (f"(Row {idx+1}) [{artist} - {album}] {msg}",)))
            self.queue.put((self._set_row_status, (idx, "Searching...")))

            try:
                while True:
                    yr_int = int(year) if year and str(year).isdigit() else None
                    limit_per = max(1, int(self.max_per_provider.get()))
                    jobs = []; candidates = []
                    with ThreadPoolExecutor(max_workers=max(2, int(self.parallelism.get()))) as ex:
                        if self.use_itunes.get():
                            jobs.append(ex.submit(provider_itunes, artist, album, limit_per))
                        if self.use_deezer.get():
                            jobs.append(ex.submit(provider_deezer, artist, album, limit_per, True))
                        if self.use_mb.get():
                            jobs.append(ex.submit(provider_musicbrainz_caa, artist, album, yr_int, max(3, limit_per//2)))
                        for fut in as_completed(jobs):
                            try:
                                part = fut.result() or []
                                candidates.extend(part)
                            except Exception as e:
                                rowlog(f"provider error: {e}")

                    candidates = soft_year_filter(candidates, yr_int, self.year_tolerance.get())
                    candidates = dedup_by_image(candidates)

                    if not candidates:
                        self.queue.put((self._set_row_status, (idx, "Not found"))); rowlog("No image candidates found."); break

                    url = None
                    if not self.ask_always.get():
                        url = self._maybe_autopick(artist, album, yr_int, candidates)

                    if not url:
                        if len(candidates) > 1 or self.ask_always.get():
                            rowlog(f"{len(candidates)} candidates - awaiting selection.")
                            result_q = queue.Queue()
                            def ask():
                                choice = self.choose_url_ui(artist, album, yr_int, candidates)
                                result_q.put(choice)
                            self.queue.put((ask, tuple()))
                            while result_q.empty():
                                if self.stop_flag.is_set(): break
                                time.sleep(0.05)
                            choice = result_q.get() if not result_q.empty() else None

                            if isinstance(choice, tuple) and choice and choice[0] == "retry":
                                _, artist, album, year = choice
                                rowlog(f"Retry: {artist} - {album} ({year if year else 'NA'})")
                                continue

                            url = choice
                            if not url:
                                self.queue.put((self._set_row_status, (idx, "Skipped"))); rowlog("User skipped."); break
                        else:
                            url = candidates[0]["image"]

                    # Save - now into per-genre folder
                    ext = ".jpg"
                    m = re.search(r"\.(jpg|jpeg|png|webp|tif|tiff)(\?|$)", url, re.I)
                    if m: ext = "." + m.group(1).lower()

                    genre_dir = os.path.join(out_dir, slug(genre))
                    os.makedirs(genre_dir, exist_ok=True)

                    fname = f"{slug(artist)}__{slug(album)}__{slug(str(yr_int) if yr_int else 'NA')}{ext}"
                    fpath = os.path.join(genre_dir, fname)
                    if os.path.isfile(fpath) and not self.overwrite_existing.get():
                        self.queue.put((self._set_row_status, (idx, "Exists - skipped"))); rowlog(f"Exists, skipped: {fpath}")
                        break

                    rowlog(f"Downloading: {url}")
                    data = http_get_bytes(url, timeout=12)
                    with open(fpath, "wb") as f: f.write(data)
                    self.queue.put((self._set_row_status, (idx, "OK"))); rowlog(f"Saved -> {fpath}")
                    break

            except Exception as e:
                self.queue.put((self._set_row_status, (idx, f"Error: {e}"))); rowlog(f"Error: {e}")

            done += 1
            self.queue.put((self.progress_var.set, (100.0 * done / total,)))
        self.queue.put((self.status_var.set, ("Done",))); self.queue.put((self.append_log, ("Finished.",)))

def _fatal(msg):
    try:
        root = tk.Tk(); root.withdraw()
        messagebox.showerror("Fatal error", msg)
        root.destroy()
    except Exception:
        print(msg, file=sys.stderr)

if __name__ == "__main__":
    try:
        if sys.platform.startswith("win"):
            try:
                from ctypes import windll
                windll.shcore.SetProcessDpiAwareness(1)
            except Exception as e:
                log_line(f"DPI awareness set failed: {e}")
        app = App()
        app.mainloop()
    except Exception as exc:
        log_line(f"App crashed at start: {exc}")
        _fatal(f"App crashed at start:\n{exc}\n\nSee log:\n{LOG_PATH}")
