
#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Template + Cover + Spine + Overlay Composer (Tkinter)
- Spine: pasted at native size at (0,0), below the final overlay.
- Cover: scaling modes (FIT, FILL, STRETCH) to the blue area.
- Output: auto-naming "album_Genre_(numbers).png" with no overwrite and optional genre subfolders.
"""

def _fatal_dialog(msg, title="Error"):
    try:
        import ctypes
        ctypes.windll.user32.MessageBoxW(0, msg, title, 0)
    except Exception:
        print(f"{title}: {msg}")

try:
    import sys, os, re, json, threading
    from pathlib import Path
    from typing import Optional, Tuple, List, Dict
except Exception as e:
    _fatal_dialog(f"Standard library import failed: {e}")
    raise

try:
    import tkinter as tk
    from tkinter import ttk, filedialog, messagebox
except Exception as e:
    _fatal_dialog("Tkinter is not available. Install Python with Tcl/Tk support.")
    raise

try:
    from PIL import Image, ImageTk, ImageDraw
except Exception as e:
    _fatal_dialog("Pillow (PIL) is not installed.\nInstall with: pip install pillow")
    raise

APP_TITLE = "Template + Cover + Spine + Overlay Composer"
CONFIG_FILE = "composer_last_session.json"
VALID_IMG_EXT = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff"}

DEFAULT_SPINE_FILES: Dict[str, str] = {
    "Rock": "Spine_Rock.png",
    "Electronic": "Spine_Electronic.png",
    "Country": "Spine_Country.png",
    "Classical": "Spine_Classical.png",
    "Pop": "Spine_Pop.png",
    "Soul": "Spine_Soul.png",
    "Metal": "Spine_Metal.png",
    "Jazz": "Spine_Jazz.png",
    "Folk": "Spine_Folk.png",
    "Rap": "Spine_Rap.png",
}

def load_image(path: Optional[str]) -> Optional[Image.Image]:
    if not path:
        return None
    try:
        return Image.open(path).convert("RGBA")
    except Exception as e:
        print(f"Failed to load image {path}: {e}")
        return None

def list_images_in_folder(folder: str) -> List[str]:
    if not folder or not os.path.isdir(folder):
        return []
    files = [str(p) for p in Path(folder).iterdir() if p.suffix.lower() in VALID_IMG_EXT]
    files.sort()
    return files

def fit_into(src: Image.Image, box_w: int, box_h: int) -> Image.Image:
    if box_w <= 0 or box_h <= 0:
        return src.copy()
    w, h = src.size
    scale = min(box_w / w, box_h / h)
    new_w = max(1, int(round(w * scale)))
    new_h = max(1, int(round(h * scale)))
    return src.resize((new_w, new_h), Image.LANCZOS)

def fill_crop_to_box(src: Image.Image, box_w: int, box_h: int) -> Image.Image:
    w, h = src.size
    scale = max(box_w / w, box_h / h)
    new_w = max(1, int(round(w * scale)))
    new_h = max(1, int(round(h * scale)))
    img = src.resize((new_w, new_h), Image.LANCZOS)
    left = max(0, (new_w - box_w) // 2)
    top = max(0, (new_h - box_h) // 2)
    right = left + box_w
    bottom = top + box_h
    img = img.crop((left, top, right, bottom))
    return img

def stretch_to_box(src: Image.Image, box_w: int, box_h: int) -> Image.Image:
    box_w = max(1, int(box_w)); box_h = max(1, int(box_h))
    return src.resize((box_w, box_h), Image.BILINEAR)

def paste_centered(base: Image.Image, overlay: Image.Image, box: Tuple[int, int, int, int]) -> None:
    x0, y0, x1, y1 = box
    bw = x1 - x0
    bh = y1 - y0
    ow, oh = overlay.size
    ox = x0 + (bw - ow) // 2
    oy = y0 + (bh - oh) // 2
    base.alpha_composite(overlay, (ox, oy))

def detect_largest_blue_rect(img: Image.Image, blue_min=140, rg_max=120, pad=0) -> Optional[Tuple[int, int, int, int]]:
    if img.mode != "RGBA":
        img = img.convert("RGBA")
    w, h = img.size
    px = img.load()
    mask = Image.new("L", (w, h), 0)
    mp = mask.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a > 0 and b >= blue_min and r <= rg_max and g <= rg_max and b > r and b > g:
                mp[x, y] = 255
    bbox = mask.getbbox()
    if not bbox:
        return None
    x0, y0, x1, y1 = bbox
    x0 = max(0, x0 - pad); y0 = max(0, y0 - pad)
    x1 = min(w, x1 + pad); y1 = min(h, y1 + pad)
    return (x0, y0, x1, y1)

class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title(APP_TITLE)
        self.geometry("1260x860")

        # Paths
        self.template_path: Optional[str] = None
        self.overlay_path: Optional[str] = None
        self.covers_folder: Optional[str] = None
        self.output_folder: Optional[str] = None
        self.spines_folder: Optional[str] = None

        # Images
        self.template_img: Optional[Image.Image] = None
        self.overlay_img: Optional[Image.Image] = None

        # Cover area
        self.blue_rect: Optional[Tuple[int, int, int, int]] = None

        # Draw helpers
        self.drag_start: Optional[Tuple[int, int]] = None

        # Thresholds
        self.blue_min = tk.IntVar(value=140)
        self.rg_max = tk.IntVar(value=120)
        self.pad_blue = tk.IntVar(value=0)

        # Spine
        self.spine_choice_var = tk.StringVar(value="Rock")
        self.spine_registry: Dict[str, str] = DEFAULT_SPINE_FILES.copy()

        # Cover scale mode
        self.cover_mode = tk.StringVar(value="fit")  # fit, fill, stretch

        # Output naming
        self.suffix = tk.StringVar(value="_composited")  # legacy fallback
        self.auto_name = tk.BooleanVar(value=True)
        self.num_digits = tk.IntVar(value=3)
        self.genre_subfolder = tk.BooleanVar(value=True)

        # Preview
        self.preview_imgtk: Optional[ImageTk.PhotoImage] = None

        self._build_ui()
        self._load_config_silent()
        self._refresh_spine_menu()
        self._refresh_covers_list()
        self._update_preview()

    def _build_ui(self):
        root = ttk.Frame(self, padding=8)
        root.pack(fill="both", expand=True)
        left = ttk.Frame(root); left.pack(side="left", fill="y")
        right = ttk.Frame(root); right.pack(side="right", fill="both", expand=True)

        sec1 = ttk.LabelFrame(left, text="Inputs", padding=8); sec1.pack(fill="x", pady=6)
        ttk.Button(sec1, text="Choose Template...", command=self.choose_template).pack(fill="x", pady=2)
        ttk.Button(sec1, text="Choose Overlay...", command=self.choose_overlay).pack(fill="x", pady=2)
        ttk.Button(sec1, text="Choose Spines Folder...", command=self.choose_spines_folder).pack(fill="x", pady=2)
        ttk.Button(sec1, text="Choose Covers Folder...", command=self.choose_covers_folder).pack(fill="x", pady=2)
        ttk.Button(sec1, text="Choose Output Folder...", command=self.choose_output_folder).pack(fill="x", pady=2)

        sec_spine = ttk.LabelFrame(left, text="Spine (native size @ 0,0)", padding=8); sec_spine.pack(fill="x", pady=6)
        ttk.Label(sec_spine, text="Pick spine:").grid(row=0, column=0, sticky="w")
        self.spine_optionmenu = ttk.OptionMenu(sec_spine, self.spine_choice_var, "Rock")
        self.spine_optionmenu.grid(row=0, column=1, sticky="we")
        sec_spine.grid_columnconfigure(1, weight=1)

        sec_blue = ttk.LabelFrame(left, text="Cover Area (Blue)", padding=8); sec_blue.pack(fill="x", pady=6)
        ttk.Label(sec_blue, text="Detect").grid(row=0, column=0, columnspan=2, sticky="w")
        ttk.Label(sec_blue, text="blue_min").grid(row=1, column=0, sticky="w")
        ttk.Entry(sec_blue, textvariable=self.blue_min, width=6).grid(row=1, column=1, sticky="w")
        ttk.Label(sec_blue, text="rg_max").grid(row=2, column=0, sticky="w")
        ttk.Entry(sec_blue, textvariable=self.rg_max, width=6).grid(row=2, column=1, sticky="w")
        ttk.Label(sec_blue, text="pad").grid(row=3, column=0, sticky="w")
        ttk.Entry(sec_blue, textvariable=self.pad_blue, width=6).grid(row=3, column=1, sticky="w")
        ttk.Button(sec_blue, text="Detect Blue Area", command=self.detect_blue_area).grid(row=4, column=0, columnspan=2, sticky="we", pady=4)

        ttk.Label(sec_blue, text="Manual rectangle").grid(row=5, column=0, columnspan=2, sticky="w", pady=(6, 2))
        mrow = ttk.Frame(sec_blue); mrow.grid(row=6, column=0, columnspan=2, sticky="we")
        self.manual_x0 = tk.IntVar(value=0); self.manual_y0 = tk.IntVar(value=0)
        self.manual_x1 = tk.IntVar(value=0); self.manual_y1 = tk.IntVar(value=0)
        for lbl, var in [("x0", self.manual_x0), ("y0", self.manual_y0), ("x1", self.manual_x1), ("y1", self.manual_y1)]:
            ttk.Label(mrow, text=lbl).pack(side="left", padx=(0, 4))
            ttk.Entry(mrow, textvariable=var, width=7).pack(side="left", padx=(0, 8))
        ttk.Button(sec_blue, text="Use Manual Rectangle", command=self.use_manual_rect).grid(row=7, column=0, columnspan=2, sticky="we")

        ttk.Label(sec_blue, text="Cover scale mode").grid(row=8, column=0, sticky="w", pady=(8, 2))
        ttk.OptionMenu(sec_blue, self.cover_mode, "fit", "fit", "fill", "stretch", command=lambda *_: self._update_preview()).grid(row=8, column=1, sticky="we")

        sec_out = ttk.LabelFrame(left, text="Output", padding=8); sec_out.pack(fill="x", pady=6)
        ttk.Checkbutton(sec_out, text="Auto-name album_Genre_(numbers).png", variable=self.auto_name).grid(row=0, column=0, columnspan=2, sticky="w")
        ttk.Label(sec_out, text="Digits").grid(row=1, column=0, sticky="w")
        ttk.Spinbox(sec_out, from_=1, to=6, textvariable=self.num_digits, width=5).grid(row=1, column=1, sticky="w")
        ttk.Checkbutton(sec_out, text="Put in Genre subfolder", variable=self.genre_subfolder).grid(row=2, column=0, columnspan=2, sticky="w", pady=(4, 6))
        ttk.Label(sec_out, text="Fallback suffix").grid(row=3, column=0, sticky="w")
        ttk.Entry(sec_out, textvariable=self.suffix, width=14).grid(row=3, column=1, sticky="w")

        ttk.Button(sec_out, text="Process Selected Covers", command=self.process_selected).grid(row=4, column=0, columnspan=2, sticky="we", pady=(6, 2))
        ttk.Button(sec_out, text="Process All Covers", command=self.process_all).grid(row=5, column=0, columnspan=2, sticky="we")

        top_right = ttk.Frame(right); top_right.pack(fill="both", expand=True)
        self.preview_canvas = tk.Canvas(top_right, bg="#222222", highlightthickness=1, relief="solid")
        self.preview_canvas.pack(side="left", fill="both", expand=True, padx=(0, 8))
        self.preview_canvas.bind("<Configure>", lambda e: self._update_preview())
        self.preview_canvas.bind("<Button-1>", self._on_canvas_down)
        self.preview_canvas.bind("<B1-Motion>", self._on_canvas_drag)
        self.preview_canvas.bind("<ButtonRelease-1>", self._on_canvas_up)

        sidebar = ttk.Frame(top_right, width=300); sidebar.pack(side="right", fill="y")
        ttk.Label(sidebar, text="Covers in Folder").pack(anchor="w")
        self.covers_list = tk.Listbox(sidebar, selectmode="extended", height=20)
        self.covers_list.pack(fill="both", expand=True, pady=(2, 6))
        self.covers_list.bind("<<ListboxSelect>>", lambda e: self._update_preview())
        self.progress = ttk.Progressbar(sidebar, orient="horizontal", mode="determinate")
        self.progress.pack(fill="x")

        bottom_bar = ttk.Frame(right); bottom_bar.pack(fill="x", pady=(6, 0))
        ttk.Button(bottom_bar, text="Save Session", command=self._save_config).pack(side="left")
        ttk.Button(bottom_bar, text="Reload Session", command=self._load_config_btn).pack(side="left", padx=6)
        ttk.Button(bottom_bar, text="Refresh Covers", command=self._refresh_covers_list).pack(side="left", padx=6)

    # File pickers
    def choose_template(self):
        p = filedialog.askopenfilename(title="Choose template image", filetypes=[("Images", "*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff")])
        if p:
            self.template_path = p
            self.template_img = load_image(p)
            self._update_preview()

    def choose_overlay(self):
        p = filedialog.askopenfilename(title="Choose overlay image", filetypes=[("Images", "*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff")])
        if p:
            self.overlay_path = p
            self.overlay_img = load_image(p)
            self._update_preview()

    def choose_spines_folder(self):
        p = filedialog.askdirectory(title="Choose spines folder")
        if p:
            self.spines_folder = p
            reg = {}
            for name, fname in DEFAULT_SPINE_FILES.items():
                candidate = Path(p) / fname
                if candidate.exists():
                    reg[name] = str(candidate)
            for extra in Path(p).glob("Spine_*.png"):
                label = extra.stem.replace("Spine_", "")
                reg[label] = str(extra)
            if reg:
                self.spine_registry = reg
            self._refresh_spine_menu()

    def choose_covers_folder(self):
        p = filedialog.askdirectory(title="Choose covers folder")
        if p:
            self.covers_folder = p
            self._refresh_covers_list()

    def choose_output_folder(self):
        p = filedialog.askdirectory(title="Choose output folder")
        if p:
            self.output_folder = p

    # Spines
    def _refresh_spine_menu(self):
        base = Path(self.spines_folder) if self.spines_folder else Path.cwd()
        resolved = {}
        for name, rel in self.spine_registry.items():
            path = Path(rel)
            if not path.is_absolute():
                path = base / rel
            resolved[name] = str(path)
        self.spine_registry = resolved

        names = list(self.spine_registry.keys())
        menu = self.spine_optionmenu["menu"]
        menu.delete(0, "end")
        for n in names:
            menu.add_command(label=n, command=lambda val=n: self._set_spine_choice(val))
        if names and self.spine_choice_var.get() not in names:
            self.spine_choice_var.set(names[0])
        self._update_preview()

    def _set_spine_choice(self, val: str):
        self.spine_choice_var.set(val)
        self._update_preview()

    # Detection
    def detect_blue_area(self):
        if not self.template_img:
            messagebox.showerror("Error", "Load a template first.")
            return
        rect = detect_largest_blue_rect(self.template_img, self.blue_min.get(), self.rg_max.get(), self.pad_blue.get())
        if not rect:
            messagebox.showwarning("No blue area", "Could not find a blue area. Set the rectangle manually.")
            return
        self.blue_rect = rect
        x0, y0, x1, y1 = rect
        self.manual_x0.set(x0); self.manual_y0.set(y0)
        self.manual_x1.set(x1); self.manual_y1.set(y1)
        self._update_preview()

    def use_manual_rect(self):
        x0 = self.manual_x0.get(); y0 = self.manual_y0.get()
        x1 = self.manual_x1.get(); y1 = self.manual_y1.get()
        if x1 <= x0 or y1 <= y0:
            messagebox.showerror("Error", "Manual rectangle is invalid.")
            return
        self.blue_rect = (x0, y0, x1, y1)
        self._update_preview()

    # Covers
    def _refresh_covers_list(self):
        self.covers_list.delete(0, "end")
        for p in list_images_in_folder(self.covers_folder or ""):
            self.covers_list.insert("end", Path(p).name)
        self._update_preview()

    # Cover scaling helper
    def _scale_cover_for_box(self, img: Image.Image, bw: int, bh: int) -> Image.Image:
        mode = (self.cover_mode.get() or "fit").lower()
        if mode == "fill":
            return fill_crop_to_box(img, bw, bh)
        elif mode == "stretch":
            return stretch_to_box(img, bw, bh)
        else:
            return fit_into(img, bw, bh)

    # Preview
    def _make_composite_preview(self) -> Optional[Image.Image]:
        if not self.template_img:
            return None
        preview = self.template_img.copy()
        draw = ImageDraw.Draw(preview, "RGBA")

        if self.blue_rect:
            x0, y0, x1, y1 = self.blue_rect
            draw.rectangle((x0, y0, x1, y1), outline=(50, 200, 255, 255), width=3)

        # Spine first (native size)
        spine_label = self.spine_choice_var.get()
        spine_path = self.spine_registry.get(spine_label, "")
        if spine_path:
            spine_img = load_image(spine_path)
            if spine_img:
                preview.alpha_composite(spine_img, (0, 0))

        # Selected cover into blue area
        sel = self.covers_list.curselection()
        cover_path = None
        if sel and self.covers_folder:
            idx = sel[0]
            all_paths = list_images_in_folder(self.covers_folder)
            if 0 <= idx < len(all_paths):
                cover_path = all_paths[idx]
        if cover_path and self.blue_rect:
            cover = load_image(cover_path)
            if cover:
                bx0, by0, bx1, by1 = self.blue_rect
                bw, bh = bx1 - bx0, by1 - by0
                prepared = self._scale_cover_for_box(cover, bw, bh)
                if prepared.size == (bw, bh):
                    preview.alpha_composite(prepared, (bx0, by0))
                else:
                    paste_centered(preview, prepared, self.blue_rect)

        if self.overlay_img:
            ov = self.overlay_img if self.overlay_img.size == preview.size else self.overlay_img.resize(preview.size, Image.LANCZOS)
            preview.alpha_composite(ov)

        return preview

    def _update_preview(self, *_):
        self.preview_canvas.delete("all")
        img = self._make_composite_preview()
        if not img:
            return
        cw = self.preview_canvas.winfo_width()
        ch = self.preview_canvas.winfo_height()
        if cw <= 2 or ch <= 2:
            return
        iw, ih = img.size
        scale = min(cw / iw, ch / ih)
        scale = max(0.05, min(1.0, scale))
        disp = img.resize((int(iw * scale), int(ih * scale)), Image.BILINEAR)
        self.preview_imgtk = ImageTk.PhotoImage(disp)
        self.preview_canvas.create_image(cw // 2, ch // 2, image=self.preview_imgtk, anchor="center")

    # Mouse drawing
    def _canvas_to_image_xy(self, cx: int, cy: int) -> Tuple[int, int]:
        img = self.template_img
        if not img:
            return (0, 0)
        iw, ih = img.size
        cw = self.preview_canvas.winfo_width()
        ch = self.preview_canvas.winfo_height()
        scale = min(cw / iw, ch / ih)
        scale = max(0.05, min(1.0, scale))
        disp_w = int(iw * scale); disp_h = int(ih * scale)
        off_x = (cw - disp_w) // 2; off_y = (ch - disp_h) // 2
        x = int((cx - off_x) / scale); y = int((cy - off_y) / scale)
        x = max(0, min(iw - 1, x)); y = max(0, min(ih - 1, y))
        return (x, y)

    def _on_canvas_down(self, event):
        if not self.template_img:
            return
        self.drag_start = self._canvas_to_image_xy(event.x, event.y)

    def _on_canvas_drag(self, event):
        if not self.template_img or not self.drag_start:
            return
        x0, y0 = self.drag_start
        x1, y1 = self._canvas_to_image_xy(event.x, event.y)
        rect = (min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1))
        self.blue_rect = rect
        self.manual_x0.set(rect[0]); self.manual_y0.set(rect[1])
        self.manual_x1.set(rect[2]); self.manual_y1.set(rect[3])
        self._update_preview()

    def _on_canvas_up(self, event):
        self.drag_start = None

    # Naming helpers
    def _next_album_filename(self, out_dir: Path, genre: str, digits: int) -> Path:
        out_dir.mkdir(parents=True, exist_ok=True)
        pattern = re.compile(rf'^album_\({re.escape(genre)}\)_(\d+)\.png$', re.IGNORECASE)
        max_n = 0
        # scan files that look like album_(genre)_NNN.png
        for p in out_dir.iterdir():
            if not p.is_file():
                continue
            m = pattern.match(p.name)
            if m:
                try:
                    n = int(m.group(1))
                    if n > max_n:
                        max_n = n
                except ValueError:
                    pass
        n = max_n + 1
        while True:
            candidate = out_dir / f"album_{genre}_{n:0{digits}d}.png"
            if not candidate.exists():
                return candidate
            n += 1

    # Processing
    def _compose_one(self, cover_path: str) -> Optional[Image.Image]:
        if not self.template_img or not self.blue_rect:
            return None
        base = self.template_img.copy()

        # Spine first
        spine_label = self.spine_choice_var.get()
        spine_path = self.spine_registry.get(spine_label, "")
        if spine_path:
            sp_img = load_image(spine_path)
            if sp_img:
                base.alpha_composite(sp_img, (0, 0))

        # Cover
        cover = load_image(cover_path)
        if cover:
            bx0, by0, bx1, by1 = self.blue_rect
            bw, bh = bx1 - bx0, by1 - by0
            prepared = self._scale_cover_for_box(cover, bw, bh)
            if prepared.size == (bw, bh):
                base.alpha_composite(prepared, (bx0, by0))
            else:
                paste_centered(base, prepared, self.blue_rect)

        # Final overlay
        if self.overlay_img:
            ov = self.overlay_img if self.overlay_img.size == base.size else self.overlay_img.resize(base.size, Image.LANCZOS)
            base.alpha_composite(ov)

        return base

    def _save_image(self, img: Image.Image, cover_path: str):
        if not self.output_folder:
            raise RuntimeError("Output folder not set.")
        out_root = Path(self.output_folder)
        genre = self.spine_choice_var.get().strip() or "Unknown"
        if self.genre_subfolder.get():
            out_dir = out_root / genre
        else:
            out_dir = out_root

        if self.auto_name.get():
            out_path = self._next_album_filename(out_dir, genre, max(1, int(self.num_digits.get())))
        else:
            out_dir.mkdir(parents=True, exist_ok=True)
            stem = Path(cover_path).stem
            out_path = out_dir / f"{stem}{self.suffix.get()}.png"
            # Avoid overwrite - bump a numeric suffix if needed
            if out_path.exists():
                i = 1
                while True:
                    candidate = out_dir / f"{stem}{self.suffix.get()}_{i}.png"
                    if not candidate.exists():
                        out_path = candidate
                        break
                    i += 1

        img.save(out_path, "PNG")

    def _process_paths(self, cover_paths: List[str]):
        if not cover_paths:
            messagebox.showinfo("Nothing to do", "No covers selected.")
            return
        if not self.template_img:
            messagebox.showerror("Error", "Load a template first.")
            return
        if not self.blue_rect:
            messagebox.showerror("Error", "Define the blue target area first.")
            return
        if not self.output_folder:
            messagebox.showerror("Error", "Choose an output folder first.")
            return

        self.progress["value"] = 0
        self.progress["maximum"] = len(cover_paths)
        errors = []

        def worker():
            nonlocal errors
            for i, p in enumerate(cover_paths, 1):
                try:
                    comp = self._compose_one(p)
                    if comp:
                        self._save_image(comp, p)
                    else:
                        errors.append(p)
                except Exception as e:
                    errors.append(f"{p} -> {e}")
                self.after(0, lambda v=i: self.progress.configure(value=v))
            def finish():
                if errors:
                    messagebox.showwarning("Done with errors", "Some items failed:\n\n" + "\n".join(errors[:20]))
                else:
                    messagebox.showinfo("Done", "All covers processed.")
            self.after(0, finish)
        threading.Thread(target=worker, daemon=True).start()

    def process_selected(self):
        if not self.covers_folder:
            messagebox.showerror("Error", "Choose a covers folder first.")
            return
        all_paths = list_images_in_folder(self.covers_folder)
        sel = self.covers_list.curselection()
        paths = [all_paths[i] for i in sel] if sel else []
        self._process_paths(paths)

    def process_all(self):
        if not self.covers_folder:
            messagebox.showerror("Error", "Choose a covers folder first.")
            return
        paths = list_images_in_folder(self.covers_folder)
        self._process_paths(paths)

    # Config
    def _save_config(self):
        cfg = {
            "template_path": self.template_path,
            "overlay_path": self.overlay_path,
            "covers_folder": self.covers_folder,
            "output_folder": self.output_folder,
            "spines_folder": self.spines_folder,
            "spine_choice": self.spine_choice_var.get(),
            "spine_registry": self.spine_registry,
            "blue_rect": self.blue_rect,
            "blue_min": self.blue_min.get(),
            "rg_max": self.rg_max.get(),
            "pad_blue": self.pad_blue.get(),
            "cover_mode": self.cover_mode.get(),
            "suffix": self.suffix.get(),
            "auto_name": self.auto_name.get(),
            "num_digits": int(self.num_digits.get()),
            "genre_subfolder": self.genre_subfolder.get(),
        }
        try:
            with open(CONFIG_FILE, "w", encoding="utf-8") as f:
                json.dump(cfg, f, indent=2)
            messagebox.showinfo("Saved", f"Session saved to {CONFIG_FILE}.")
        except Exception as e:
            messagebox.showerror("Error", f"Could not save config: {e}")

    def _load_config_btn(self):
        self._load_config_silent()
        self._refresh_spine_menu()
        self._refresh_covers_list()
        self._update_preview()

    def _load_config_silent(self):
        try:
            if not os.path.exists(CONFIG_FILE):
                return
            with open(CONFIG_FILE, "r", encoding="utf-8") as f:
                cfg = json.load(f)
            self.template_path = cfg.get("template_path")
            self.overlay_path = cfg.get("overlay_path")
            self.covers_folder = cfg.get("covers_folder")
            self.output_folder = cfg.get("output_folder")
            self.spines_folder = cfg.get("spines_folder")
            self.template_img = load_image(self.template_path) if self.template_path else None
            self.overlay_img = load_image(self.overlay_path) if self.overlay_path else None
            self.spine_registry = cfg.get("spine_registry", self.spine_registry)
            self.spine_choice_var.set(cfg.get("spine_choice", self.spine_choice_var.get()))
            self.blue_rect = tuple(cfg["blue_rect"]) if cfg.get("blue_rect") else None
            if self.blue_rect:
                x0, y0, x1, y1 = self.blue_rect
                self.manual_x0.set(x0); self.manual_y0.set(y0)
                self.manual_x1.set(x1); self.manual_y1.set(y1)
            self.blue_min.set(cfg.get("blue_min", self.blue_min.get()))
            self.rg_max.set(cfg.get("rg_max", self.rg_max.get()))
            self.pad_blue.set(cfg.get("pad_blue", self.pad_blue.get()))
            self.cover_mode.set(cfg.get("cover_mode", self.cover_mode.get()))
            self.suffix.set(cfg.get("suffix", self.suffix.get()))
            self.auto_name.set(cfg.get("auto_name", self.auto_name.get()))
            self.num_digits.set(int(cfg.get("num_digits", self.num_digits.get())))
            self.genre_subfolder.set(cfg.get("genre_subfolder", self.genre_subfolder.get()))
        except Exception as e:
            print(f"Config load warning: {e}")

def main():
    try:
        app = App()
        app.mainloop()
    except Exception as e:
        _fatal_dialog(f"Fatal error: {e}")
        raise

if __name__ == "__main__":
    main()
