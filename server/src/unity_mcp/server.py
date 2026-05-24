"""FastMCP server exposing the Unity bridge to Claude.

Tools: smoke (unity_ping, unity_get_state), a generic passthrough (unity_request),
console reading (unity_get_console_logs, unity_clear_console), and compile control
(unity_recompile_and_wait, unity_get_compile_result).
"""

from __future__ import annotations

import time
from typing import Any, Optional

from mcp.server.fastmcp import FastMCP

from .exceptions import UnityConnectionError, UnityError
from .unity_client import UnityClient

mcp = FastMCP("unity-mcp")
_client = UnityClient()


def _call(category: str, action: str, params: Optional[dict] = None) -> Any:
    """Invoke a bridge action, translating client errors into clear tool errors."""
    try:
        return _client.request(category, action, params)
    except UnityConnectionError as exc:
        raise RuntimeError(str(exc)) from exc
    except UnityError as exc:
        raise RuntimeError(f"Unity returned an error: {exc}") from exc


@mcp.tool()
def unity_ping() -> dict:
    """Smoke-test the Unity MCP bridge.

    Returns Unity version, product name, and a server timestamp. Use this to
    confirm the Editor is reachable before issuing other commands.
    """
    return _call("editor", "ping")


@mcp.tool()
def unity_get_state() -> dict:
    """Report the current Unity Editor state.

    Includes play/pause flags, whether the Editor is compiling or updating,
    the active platform, and the Unity version.
    """
    return _call("editor", "get_state")


@mcp.tool()
def unity_request(category: str, action: str, params: Optional[dict] = None) -> Any:
    """Low-level passthrough to any Unity bridge handler.

    Sends {category, action, params} straight to the Editor and returns its data.
    Handy for actions that don't have a dedicated tool yet, e.g.
    unity_request("editor", "get_state").
    """
    return _call(category, action, params or {})


@mcp.tool()
def unity_get_console_logs(level: str = "All", limit: int = 100) -> dict:
    """Read the Unity Editor Console.

    level: "All" | "Error" | "Warning" | "Log". limit: max (most recent) entries.
    Returns {total, errorCount, warningCount, shown, entries:[{level,message,file,line}]}.
    """
    return _call("console", "get_logs", {"level": level, "limit": limit})


@mcp.tool()
def unity_clear_console() -> dict:
    """Clear the Unity Editor Console."""
    return _call("console", "clear")


@mcp.tool()
def unity_get_compile_result() -> dict:
    """Return the most recent C# compilation result without triggering a new compile.

    {isCompiling, succeeded, errorCount, warningCount, errors[], warnings[]}.
    """
    return _call("script", "get_compile_result")


def _format_compile(res: dict) -> dict:
    return {
        "succeeded": res.get("succeeded", False),
        "isCompiling": res.get("isCompiling", False),
        "errorCount": res.get("errorCount", 0),
        "warningCount": res.get("warningCount", 0),
        "errors": res.get("errors", []),
        "warnings": res.get("warnings", []),
    }


@mcp.tool()
def unity_recompile_and_wait(force: bool = True, timeout_seconds: float = 180.0) -> dict:
    """Recompile C# in the Editor and wait for the result.

    Use after editing scripts in the package. Transparently rides out the domain
    reload (the bridge drops and reconnects). Returns
    {succeeded, errorCount, warningCount, errors[], warnings[]}; each error carries
    {message, file, line, column, assembly}.
    """
    try:
        started = _client.request("script", "recompile", {"force": force})
    except (UnityConnectionError, UnityError) as exc:
        raise RuntimeError(str(exc)) from exc
    prev = int(started.get("previousFinishedAtTicks", 0))

    start_time = time.time()
    deadline = start_time + timeout_seconds
    time.sleep(1.5)  # let Unity register the compile / begin the domain reload

    last: Optional[dict] = None
    saw_activity = False
    while time.time() < deadline:
        try:
            res = _client.request("script", "get_compile_result")
        except (UnityConnectionError, UnityError):
            saw_activity = True  # a dropped socket almost always means a reload is underway
            time.sleep(1.0)
            continue
        last = res
        if res.get("isCompiling") or res.get("isUpdating"):
            saw_activity = True
            time.sleep(1.0)
            continue
        if int(res.get("finishedAtTicks", 0)) > prev:
            return _format_compile(res)
        if not force and not saw_activity and (time.time() - start_time) > 5.0:
            out = _format_compile(res)
            out["note"] = "No compilation was triggered (no script changes detected)."
            return out
        time.sleep(1.0)

    out: dict = {"timedOut": True, "note": f"Compile did not finish within {timeout_seconds}s."}
    if last:
        out.update(_format_compile(last))
    return out


# -- Phase 2: GameObject / Component / Scene convenience tools --------------

@mcp.tool()
def unity_create_gameobject(name: str = None, primitive: str = None, parent: str = None,
                            position: list = None, rotation: list = None, scale: list = None) -> dict:
    """Create a GameObject in the active scene.

    primitive: optional Cube/Sphere/Capsule/Cylinder/Plane/Quad (omit for an empty GameObject).
    parent: optional instanceId or hierarchy path to parent under.
    position/rotation/scale: optional [x,y,z] (local space). Returns the new object's info incl. instanceId.
    """
    params: dict = {}
    if name is not None: params["name"] = name
    if primitive is not None: params["primitive"] = primitive
    if parent is not None: params["parent"] = parent
    if position is not None: params["position"] = position
    if rotation is not None: params["rotation"] = rotation
    if scale is not None: params["scale"] = scale
    return _call("gameobject", "create", params)


@mcp.tool()
def unity_delete_gameobject(target: str) -> dict:
    """Delete a GameObject. target = instanceId or hierarchy path/name."""
    return _call("gameobject", "delete", {"target": target})


@mcp.tool()
def unity_find_gameobjects(name: str = None, tag: str = None, path: str = None,
                           include_inactive: bool = True) -> dict:
    """Find GameObjects by name, tag, and/or full hierarchy path. Returns all matches."""
    params: dict = {"includeInactive": include_inactive}
    if name is not None: params["name"] = name
    if tag is not None: params["tag"] = tag
    if path is not None: params["path"] = path
    return _call("gameobject", "find_all", params)


@mcp.tool()
def unity_get_hierarchy(root: str = None, max_depth: int = 6) -> dict:
    """Get the scene hierarchy as a tree. root: optional instanceId/path to start from (else all scene roots)."""
    params: dict = {"maxDepth": max_depth}
    if root is not None: params["root"] = root
    return _call("gameobject", "get_hierarchy", params)


@mcp.tool()
def unity_add_component(target: str, type: str, values: dict = None) -> dict:
    """Add a component by type name (e.g. 'Rigidbody', 'BoxCollider') to a GameObject, optionally setting values."""
    params: dict = {"target": target, "type": type}
    if values is not None: params["values"] = values
    return _call("component", "add", params)


@mcp.tool()
def unity_set_component_property(target: str, type: str, property: str, value: Any) -> dict:
    """Set one property/field on a component (e.g. type='Rigidbody', property='mass', value=5)."""
    return _call("component", "set_property",
                 {"target": target, "type": type, "property": property, "value": value})


@mcp.tool()
def unity_get_components(target: str) -> dict:
    """List the components on a GameObject."""
    return _call("component", "list", {"target": target})


@mcp.tool()
def unity_new_scene(path: str = None, additive: bool = False) -> dict:
    """Create a new scene with default camera+light. If path is given (e.g. 'Assets/Scenes/My.unity'), saves it."""
    params: dict = {"additive": additive}
    if path is not None: params["path"] = path
    return _call("scene", "new", params)


@mcp.tool()
def unity_open_scene(path: str, additive: bool = False) -> dict:
    """Open a scene by asset path (e.g. 'Assets/Scenes/Main.unity')."""
    return _call("scene", "open", {"path": path, "additive": additive})


@mcp.tool()
def unity_save_scene(path: str = None) -> dict:
    """Save the active scene. path optional (Save As to that asset path)."""
    params: dict = {}
    if path is not None: params["path"] = path
    return _call("scene", "save", params)


@mcp.tool()
def unity_save_all_scenes() -> dict:
    """Save all open scenes."""
    return _call("scene", "save_all")


# -- Phase 3: Asset / Prefab convenience tools ------------------------------

@mcp.tool()
def unity_find_assets(filter: str = "", folders: list = None, max_results: int = 200) -> dict:
    """Search the AssetDatabase. filter uses Unity syntax, e.g. 't:Material wall', 't:Prefab', 'l:MyLabel'.

    folders: optional list of folders to search within (e.g. ['Assets/Prefabs']).
    Returns matches as {guid, path, type}.
    """
    params: dict = {"filter": filter, "maxResults": max_results}
    if folders is not None: params["folders"] = folders
    return _call("asset", "find_assets", params)


@mcp.tool()
def unity_create_folder(path: str) -> dict:
    """Create a project folder (and any missing parents), e.g. 'Assets/Prefabs/Enemies'."""
    return _call("asset", "create_folder", {"path": path})


@mcp.tool()
def unity_delete_asset(path: str, confirm: bool = False) -> dict:
    """Delete an asset (not undoable). Pass confirm=True to actually delete."""
    return _call("asset", "delete", {"path": path, "confirm": confirm})


@mcp.tool()
def unity_save_assets() -> dict:
    """Write any pending asset changes to disk (AssetDatabase.SaveAssets)."""
    return _call("asset", "save")


@mcp.tool()
def unity_create_prefab(target: str, path: str, connect: bool = True) -> dict:
    """Save a scene GameObject as a prefab asset.

    target: instanceId or hierarchy path. path: e.g. 'Assets/Prefabs/Enemy.prefab'.
    connect=True turns the scene object into an instance of the new prefab.
    """
    return _call("prefab", "create", {"target": target, "path": path, "connect": connect})


@mcp.tool()
def unity_instantiate_prefab(path: str, parent: str = None, position: list = None,
                             name: str = None, unpack: bool = False) -> dict:
    """Instantiate a prefab into the active scene.

    path: prefab asset path. parent: optional instanceId/path. position: optional [x,y,z].
    unpack=True breaks the prefab link on the new instance.
    """
    params: dict = {"path": path, "unpack": unpack}
    if parent is not None: params["parent"] = parent
    if position is not None: params["position"] = position
    if name is not None: params["name"] = name
    return _call("prefab", "instantiate", params)


# -- Phase 4 (uGUI) convenience tools ---------------------------------------

@mcp.tool()
def unity_create_canvas(name: str = "Canvas") -> dict:
    """Create a screen-space UI Canvas (CanvasScaler + GraphicRaycaster) and an EventSystem if missing."""
    return _call("ui", "create_canvas", {"name": name})


@mcp.tool()
def unity_create_ui_element(type: str, parent: str, name: str = None, text: str = None,
                            anchored_position: list = None, size: list = None) -> dict:
    """Create a uGUI element under a Canvas/parent.

    type: Panel/Button/Text/Image/RawImage/InputField/Toggle/Slider/Scrollbar/Dropdown/ScrollView/Empty.
    parent: instanceId or hierarchy path (must be under a Canvas).
    text: sets the element's Text (e.g. a Button label). anchored_position/size: [x,y].
    """
    params: dict = {"type": type, "parent": parent}
    if name is not None: params["name"] = name
    if text is not None: params["text"] = text
    if anchored_position is not None: params["anchoredPosition"] = anchored_position
    if size is not None: params["size"] = size
    return _call("ui", "create_element", params)


@mcp.tool()
def unity_set_ui_text(target: str, text: str) -> dict:
    """Set the Text on a UI element (or its child Text, e.g. a Button label)."""
    return _call("ui", "set_text", {"target": target, "text": text})


# -- Phase 4 (Material) convenience tools -----------------------------------

@mcp.tool()
def unity_detect_render_pipeline() -> dict:
    """Detect the active render pipeline (BuiltIn / URP / HDRP) and its default lit shader."""
    return _call("material", "detect_pipeline")


@mcp.tool()
def unity_create_material(path: str, shader: str = None, render_pipeline: str = None) -> dict:
    """Create a material asset (e.g. 'Assets/Materials/Wall.mat').

    shader: optional shader name; if omitted, the default lit shader for the detected
    pipeline is used (URP -> 'Universal Render Pipeline/Lit', etc.).
    """
    params: dict = {"path": path}
    if shader is not None: params["shader"] = shader
    if render_pipeline is not None: params["renderPipeline"] = render_pipeline
    return _call("material", "create", params)


@mcp.tool()
def unity_set_material_color(path: str, color, property: str = None) -> dict:
    """Set a material color. color = #hex, name, or [r,g,b,a]. property defaults to
    the pipeline base-color property (_BaseColor on URP/HDRP, _Color on Built-in)."""
    params: dict = {"path": path, "color": color}
    if property is not None: params["property"] = property
    return _call("material", "set_color", params)


@mcp.tool()
def unity_set_material_texture(path: str, texture_path: str, property: str = None) -> dict:
    """Bind a texture to a material. property defaults to _BaseMap (URP) or _MainTex."""
    params: dict = {"path": path, "texturePath": texture_path}
    if property is not None: params["property"] = property
    return _call("material", "set_texture", params)


@mcp.tool()
def unity_list_material_properties(path: str) -> dict:
    """List the shader properties available on a material (name, type, description)."""
    return _call("material", "list_properties", {"path": path})


@mcp.tool()
def unity_assign_material(target: str, material_path: str, slot: int = None) -> dict:
    """Assign a material to a GameObject's Renderer (or SpriteRenderer). slot = material index (default 0)."""
    params: dict = {"target": target, "materialPath": material_path}
    if slot is not None: params["slot"] = slot
    return _call("material", "assign_to_renderer", params)


def main() -> None:
    """Console-script entry point: serve over stdio for Claude Desktop / Claude Code."""
    mcp.run()


if __name__ == "__main__":
    main()
