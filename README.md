# Unity MCP Bridge

Drive the Unity Editor from Claude (Claude Desktop / Claude Code) over a local
MCP bridge. Create GameObjects, add components, edit transforms, build scenes,
recompile scripts and read console errors — all in natural language.

**Repo:** https://github.com/Mehrdadgame/MCP-Unity-Mehrdad

🌐 **[English](#english) · [فارسی](#فارسی)**

---

## English

### What is it?

Two halves that talk to each other:

- **`unity-package/`** — a Unity *Editor* C# package (the **bridge**). It opens a
  local TCP server on `127.0.0.1:6400` and executes commands inside the Editor.
- **`server/`** — a Python **MCP server** (FastMCP). Claude talks to it over stdio;
  it forwards each request to the Unity bridge.

```
Claude  ──stdio──►  Python MCP server  ──TCP/JSON (127.0.0.1:6400)──►  Unity Editor bridge
```

Wire format: a 4-byte big-endian length prefix followed by UTF-8 JSON.

### Requirements

- **Unity** 2021.3 or newer (developed on Unity 6 / `6000.4`). URP or built-in both fine.
- **Python** 3.10+ on your PATH.
- **Claude Desktop** (or Claude Code).
- Network access to GitHub (in some regions a VPN/proxy is required).

### Install

#### A) Add the Unity package (per project)

In Unity: **Window ▸ Package Manager ▸ `+` ▸ Add package from git URL…** and paste:

```
https://github.com/Mehrdadgame/MCP-Unity-Mehrdad.git?path=/unity-package
```

Unity clones it and pulls its dependency (Newtonsoft JSON) automatically. To pin a
version, append `#main` or a tag, e.g. `…?path=/unity-package#main`.

> Editing the package yourself? Reference it locally instead — add to
> `Packages/manifest.json`:
> `"com.unitymcp.bridge": "file:ABSOLUTE/PATH/TO/unity-package"`.

#### B) Install the Python server (once per machine — it serves every project)

```bash
git clone https://github.com/Mehrdadgame/MCP-Unity-Mehrdad.git
cd MCP-Unity-Mehrdad/server
# Windows:
powershell -ExecutionPolicy Bypass -File .\setup.ps1
# macOS / Linux:
./setup.sh
```

The script creates a `.venv`, installs the server, and prints the exact Claude
config entry (with your machine's path filled in).

*No-clone alternative (needs [pipx](https://pipx.pypa.io/)):*
```bash
pipx install "git+https://github.com/Mehrdadgame/MCP-Unity-Mehrdad.git#subdirectory=server"
```

#### C) Wire it into Claude Desktop

Open `claude_desktop_config.json`:
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`

Add the entry the setup script printed, inside the top-level `mcpServers` object:

```json
{
  "mcpServers": {
    "unity-mcp": {
      "command": "C:\\path\\to\\server\\.venv\\Scripts\\python.exe",
      "args": ["-m", "unity_mcp.server"]
    }
  }
}
```

Then **fully quit Claude Desktop from the system tray** (Quit, not just close) and reopen.

### Connect & verify

1. Open your Unity project. The bridge auto-starts; the Console shows
   `[MCP] Bridge listening on 127.0.0.1:6400`.
   You can also control it from **Tools ▸ MCP** (Start / Stop / Print Status).
2. In Claude, run **`unity_ping`** — it returns the Unity version and project name.

### Tools

| Tool | What it does |
|---|---|
| `unity_ping` / `unity_get_state` | Connectivity + editor state (play/compiling/platform) |
| `unity_request(category, action, params)` | **Generic passthrough** to any bridge action |
| `unity_create_gameobject` | Create empty or primitive (Cube/Sphere/…), set name/parent/transform |
| `unity_delete_gameobject` / `unity_find_gameobjects` / `unity_get_hierarchy` | Delete, search, inspect the scene tree |
| `unity_add_component` / `unity_set_component_property` / `unity_get_components` | Components by type name + property edits |
| `unity_new_scene` / `unity_open_scene` / `unity_save_scene` | Scene create / open / save |
| `unity_recompile_and_wait` / `unity_get_compile_result` | Recompile C# and read compile errors (survives the reload) |
| `unity_get_console_logs` / `unity_clear_console` | Read / clear the Editor Console |

Anything not yet wrapped by a dedicated tool is reachable through `unity_request`.
Categories & actions today: `editor` (ping, get_state, play, stop, pause,
execute_menu_item, refresh) · `gameobject` (create, delete, find, find_all,
get_info, rename, set_parent, set_transform, set_active, duplicate, get_hierarchy,
set_tag, set_layer) · `component` (add, remove, list, get_properties, set_property,
set_properties) · `scene` (new, save, open, get_open_scenes, get_active) ·
`script` (recompile, get_compile_result) · `console` (get_logs, clear).

### Examples

```
Make a red ground: unity_create_gameobject(primitive="Plane", name="Ground")
Add physics:        unity_add_component(target="Ground", type="Rigidbody")
Tune it:            unity_set_component_property(target="Ground", type="Rigidbody", property="mass", value=10)
New scene:          unity_new_scene(path="Assets/Scenes/Level1.unity")
Via passthrough:    unity_request("gameobject", "create", {"primitive": "Cube", "position": [0, 1, 0]})
```

### Updating

- **Unity package:** Package Manager ▸ select the package ▸ **Update** (or change the `#tag`).
- **Python server:** `git pull`, then re-run `setup.ps1` / `setup.sh`.

### Troubleshooting

- **`spawn … python.exe ENOENT` / "Server disconnected"** — the `command` path in
  your Claude config points to a python that no longer exists. Re-run the setup
  script and paste the new path, then restart Claude Desktop.
- **`unity_ping` fails / connection refused** — make sure the Unity project is open
  and the bridge is running (**Tools ▸ MCP ▸ Start Bridge**). If Unity is mid-compile,
  wait a few seconds and retry.
- **`UNKNOWN_ACTION`** — that action isn't implemented yet (see the roadmap); it's not a bug.
- **GitHub unreachable** — enable your VPN/proxy; UPM, `git`, and `pipx` all need GitHub.

### Repository layout

```
unity-package/                 Unity Editor package (UPM)
  Editor/Core/                 MCPBridge, CommandRouter, MainThreadDispatcher, CompileWatcher
  Editor/Protocol/             Request, Response, ErrorCodes, HandlerException
  Editor/Utils/                Framing, ObjectFinder, TypeResolver, ValueParser, ConsoleLogReader
  Editor/Handlers/V1/          gameobject, component, scene, editor, console, script, …
server/                        Python FastMCP server
  src/unity_mcp/               server.py, unity_client.py, exceptions.py
  tests/                       test_framing.py
  setup.ps1 / setup.sh         one-time machine setup
```

### Roadmap

- ✅ Foundation (bridge, router, protocol), compile control + console reading
- ✅ Phase 2 — GameObject, Component, Scene basics, Editor controls
- ⏳ Phase 3+ — Asset, Prefab, Material, uGUI, UI Toolkit, Animation, Build, Tests, …

---

## فارسی

یک پل MCP که با آن می‌توانید **Unity Editor** را از داخل Claude کنترل کنید: ساخت
GameObject، افزودن کامپوننت، ویرایش ترنسفورم، ساخت صحنه، کامپایل اسکریپت و خواندن
خطاهای کنسول — همه با زبان طبیعی.

**ریپو:** https://github.com/Mehrdadgame/MCP-Unity-Mehrdad

### این چیست؟

دو بخش که با هم حرف می‌زنند:

- **`unity-package/`** — یک پکیج C# مخصوص *Editor* یونیتی (همان **bridge**). یک سرور
  TCP محلی روی `127.0.0.1:6400` باز می‌کند و دستورها را داخل ادیتور اجرا می‌کند.
- **`server/`** — یک سرور **MCP** پایتونی (FastMCP). Claude از طریق stdio با آن حرف
  می‌زند و هر درخواست را به bridge یونیتی می‌فرستد.

```
Claude  ──stdio──►  سرور MCP پایتون  ──TCP/JSON (127.0.0.1:6400)──►  bridge یونیتی
```

قالب پیام: ۴ بایت طول (big-endian) + سپس JSON با UTF-8.

### پیش‌نیازها

- **Unity** نسخهٔ 2021.3 یا جدیدتر (روی Unity 6 / `6000.4` توسعه داده شده).
- **Python** نسخهٔ 3.10 به بالا که روی PATH باشد.
- **Claude Desktop** (یا Claude Code).
- دسترسی به GitHub (در برخی مناطق نیاز به VPN/پروکسی دارد).

### نصب

#### الف) افزودن پکیج Unity (برای هر پروژه)

در یونیتی: **Window ▸ Package Manager ▸ دکمهٔ `+` ▸ Add package from git URL…** و این
آدرس را پیست کنید:

```
https://github.com/Mehrdadgame/MCP-Unity-Mehrdad.git?path=/unity-package
```

یونیتی خودش آن را clone می‌کند و وابستگی‌اش (Newtonsoft JSON) را می‌گیرد. برای قفل‌کردن
روی یک نسخه، آخرش `#main` یا یک tag بگذارید: `…?path=/unity-package#main`.

> اگر خودتان روی پکیج کد می‌زنید، به‌صورت محلی رفرنسش بدهید — در `Packages/manifest.json`:
> `"com.unitymcp.bridge": "file:مسیر/مطلق/به/unity-package"`.

#### ب) نصب سرور Python (یک‌بار روی هر سیستم — برای همهٔ پروژه‌ها کار می‌کند)

```bash
git clone https://github.com/Mehrdadgame/MCP-Unity-Mehrdad.git
cd MCP-Unity-Mehrdad/server
# ویندوز:
powershell -ExecutionPolicy Bypass -File .\setup.ps1
# مک / لینوکس:
./setup.sh
```

اسکریپت یک `.venv` می‌سازد، سرور را نصب می‌کند و بلوک دقیقِ کانفیگ Claude را (با مسیر
همان سیستم) چاپ می‌کند.

*روش بدون clone (نیاز به [pipx](https://pipx.pypa.io/)):*
```bash
pipx install "git+https://github.com/Mehrdadgame/MCP-Unity-Mehrdad.git#subdirectory=server"
```

#### ج) اتصال به Claude Desktop

فایل `claude_desktop_config.json` را باز کنید:
- ویندوز: `%APPDATA%\Claude\claude_desktop_config.json`
- مک: `~/Library/Application Support/Claude/claude_desktop_config.json`

بلوکی که اسکریپت چاپ کرد را داخل شیء `mcpServers` بگذارید:

```json
{
  "mcpServers": {
    "unity-mcp": {
      "command": "C:\\path\\to\\server\\.venv\\Scripts\\python.exe",
      "args": ["-m", "unity_mcp.server"]
    }
  }
}
```

بعد **Claude Desktop را کامل از system tray ببندید** (Quit، نه فقط بستن پنجره) و دوباره باز کنید.

### اتصال و تست

1. پروژهٔ یونیتی را باز کنید. bridge خودکار بالا می‌آید؛ در Console این را می‌بینید:
   `[MCP] Bridge listening on 127.0.0.1:6400`.
   از منوی **Tools ▸ MCP** هم می‌توانید آن را Start/Stop کنید یا وضعیتش را ببینید.
2. در Claude بگویید **`unity_ping`** — نسخهٔ Unity و نام پروژه را برمی‌گرداند.

### ابزارها

| ابزار | کار |
|---|---|
| `unity_ping` / `unity_get_state` | تست اتصال + وضعیت ادیتور (play/compiling/platform) |
| `unity_request(category, action, params)` | **پل عمومی** برای صدا زدن هر اکشن |
| `unity_create_gameobject` | ساخت آبجکت خالی یا primitive، تنظیم نام/پدر/ترنسفورم |
| `unity_delete_gameobject` / `unity_find_gameobjects` / `unity_get_hierarchy` | حذف، جستجو، دیدن درخت صحنه |
| `unity_add_component` / `unity_set_component_property` / `unity_get_components` | افزودن کامپوننت با اسم نوع + ویرایش پراپرتی |
| `unity_new_scene` / `unity_open_scene` / `unity_save_scene` | ساخت / باز کردن / ذخیرهٔ صحنه |
| `unity_recompile_and_wait` / `unity_get_compile_result` | کامپایل C# و خواندن خطاها (از domain reload جان سالم به‌در می‌برد) |
| `unity_get_console_logs` / `unity_clear_console` | خواندن / پاک‌کردن کنسول ادیتور |

هر چیزی که هنوز ابزار اختصاصی ندارد، از طریق `unity_request` در دسترس است.
دسته‌ها و اکشن‌های فعلی: `editor` (ping, get_state, play, stop, pause,
execute_menu_item, refresh) · `gameobject` (create, delete, find, find_all,
get_info, rename, set_parent, set_transform, set_active, duplicate, get_hierarchy,
set_tag, set_layer) · `component` (add, remove, list, get_properties, set_property,
set_properties) · `scene` (new, save, open, get_open_scenes, get_active) ·
`script` (recompile, get_compile_result) · `console` (get_logs, clear).

### مثال‌ها

```
ساخت زمین:        unity_create_gameobject(primitive="Plane", name="Ground")
افزودن فیزیک:      unity_add_component(target="Ground", type="Rigidbody")
تنظیم جرم:        unity_set_component_property(target="Ground", type="Rigidbody", property="mass", value=10)
صحنهٔ جدید:        unity_new_scene(path="Assets/Scenes/Level1.unity")
با پل عمومی:       unity_request("gameobject", "create", {"primitive": "Cube", "position": [0, 1, 0]})
```

### به‌روزرسانی

- **پکیج Unity:** در Package Manager پکیج را انتخاب و **Update** بزنید (یا `#tag` را عوض کنید).
- **سرور Python:** `git pull` و بعد دوباره `setup.ps1` / `setup.sh`.

### رفع اشکال

- **`spawn … python.exe ENOENT` یا «Server disconnected»** — مسیر `command` در کانفیگ
  Claude به پایتونی اشاره می‌کند که دیگر وجود ندارد. اسکریپت setup را دوباره اجرا و مسیر
  جدید را جایگزین کنید، بعد Claude Desktop را ری‌استارت کنید.
- **`unity_ping` کار نمی‌کند / connection refused** — مطمئن شوید پروژهٔ Unity باز است و
  bridge روشن است (**Tools ▸ MCP ▸ Start Bridge**). اگر یونیتی در حال کامپایل است، چند
  ثانیه صبر و دوباره امتحان کنید.
- **`UNKNOWN_ACTION`** — یعنی آن اکشن هنوز ساخته نشده (به roadmap نگاه کنید)؛ باگ نیست.
- **GitHub در دسترس نیست** — VPN/پروکسی را روشن کنید؛ UPM و `git` و `pipx` همگی به GitHub نیاز دارند.

### ساختار ریپو

```
unity-package/                 پکیج Editor یونیتی (UPM)
  Editor/Core/                 MCPBridge, CommandRouter, MainThreadDispatcher, CompileWatcher
  Editor/Protocol/             Request, Response, ErrorCodes, HandlerException
  Editor/Utils/                Framing, ObjectFinder, TypeResolver, ValueParser, ConsoleLogReader
  Editor/Handlers/V1/          gameobject, component, scene, editor, console, script, …
server/                        سرور FastMCP پایتون
  src/unity_mcp/               server.py, unity_client.py, exceptions.py
  tests/                       test_framing.py
  setup.ps1 / setup.sh         راه‌اندازی یک‌بارهٔ سیستم
```

### نقشهٔ راه

- ✅ پایه (bridge، router، protocol)، کنترل کامپایل + خواندن کنسول
- ✅ فاز ۲ — GameObject، Component، پایهٔ Scene، کنترل‌های Editor
- ⏳ فاز ۳ به بعد — Asset، Prefab، Material، uGUI، UI Toolkit، Animation، Build، Test، …
