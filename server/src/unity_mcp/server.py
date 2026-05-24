"""FastMCP server exposing the Unity bridge to Claude.

Tools: smoke (unity_ping, unity_get_state), a generic passthrough (unity_request),
console reading (unity_get_console_logs, unity_clear_console), and compile control
(unity_recompile_and_wait, unity_get_compile_result).
"""

from __future__ import annotations

import base64
import time
from typing import Any, Optional

from mcp.server.fastmcp import FastMCP, Image

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
def unity_recompile_and_wait(force: bool = False, timeout_seconds: float = 180.0) -> dict:
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


# -- Phase 5 (Script / ScriptableObject) + UI button wiring -----------------

@mcp.tool()
def unity_create_script(path: str, content: str = None, class_name: str = None,
                        namespace: str = None, base_class: str = None, overwrite: bool = False) -> dict:
    """Write a C# script to the project (e.g. 'Assets/Scripts/MainMenu.cs').

    Provide full `content`, or omit it to generate a minimal class (class_name/namespace/base_class).
    Call unity_recompile_and_wait afterwards before using the new type.
    """
    params: dict = {"path": path, "overwrite": overwrite}
    if content is not None: params["content"] = content
    if class_name is not None: params["className"] = class_name
    if namespace is not None: params["namespace"] = namespace
    if base_class is not None: params["baseClass"] = base_class
    return _call("script", "create", params)


@mcp.tool()
def unity_read_script(path: str) -> dict:
    """Read a C# script file's contents."""
    return _call("script", "read", {"path": path})


@mcp.tool()
def unity_bind_button(target: str, component: str, method: str, handler_target: str = None) -> dict:
    """Wire a uGUI Button's OnClick to a public, parameterless method on a component.

    target: the Button GameObject. component: type name holding the method (e.g. 'MainMenu').
    method: the method name (e.g. 'Play'). handler_target: GameObject with that component
    (defaults to the button's own GameObject).
    """
    params: dict = {"target": target, "component": component, "method": method}
    if handler_target is not None: params["handlerTarget"] = handler_target
    return _call("ui", "bind_onclick", params)


@mcp.tool()
def unity_create_scriptable_class(class_name: str, fields: list = None, namespace: str = None,
                                  menu_name: str = None, folder: str = "Assets/Scripts") -> dict:
    """Generate a ScriptableObject C# class with [CreateAssetMenu].

    fields: list of {name, type, default?}. Recompile before creating instances.
    """
    params: dict = {"className": class_name, "folder": folder}
    if fields is not None: params["fields"] = fields
    if namespace is not None: params["namespace"] = namespace
    if menu_name is not None: params["menuName"] = menu_name
    return _call("scriptableobject", "create_class", params)


@mcp.tool()
def unity_create_scriptable_instance(class_name: str, asset_path: str, values: dict = None) -> dict:
    """Create a ScriptableObject asset instance of a (compiled) class, optionally setting field values."""
    params: dict = {"className": class_name, "assetPath": asset_path}
    if values is not None: params["values"] = values
    return _call("scriptableobject", "create_instance", params)


# -- Phase 6 (UI Toolkit + EditorWindow generator) --------------------------

@mcp.tool()
def unity_scaffold_editor_window(name: str, menu_path: str = None, title: str = None,
                                 namespace: str = None, use_uitk: bool = True) -> dict:
    """Generate a custom EditorWindow (C# + optionally .uxml/.uss) under Assets/Editor.

    name: the class name (e.g. 'LevelDesigner'). menu_path: menu item (default 'Tools/<Title>').
    Recompile, then unity_open_editor_window(name) to show it.
    """
    params: dict = {"name": name, "useUITK": use_uitk}
    if menu_path is not None: params["menuPath"] = menu_path
    if title is not None: params["title"] = title
    if namespace is not None: params["namespace"] = namespace
    return _call("editorwindow", "scaffold", params)


@mcp.tool()
def unity_open_editor_window(type_name: str) -> dict:
    """Open a (compiled) EditorWindow by its type name."""
    return _call("editorwindow", "open_window", {"typeName": type_name})


@mcp.tool()
def unity_uxml_add_element(uxml_path: str, element_type: str, parent_selector: str = "root",
                           name: str = None, text: str = None, classes: list = None,
                           attributes: dict = None) -> dict:
    """Add an element to a UXML file. element_type e.g. Label/Button/TextField/ListView/ScrollView.

    parent_selector: '#name', '.class', a Type, or 'root'. name/text/classes/attributes optional.
    """
    params: dict = {"uxmlPath": uxml_path, "elementType": element_type, "parentSelector": parent_selector}
    if name is not None: params["name"] = name
    if text is not None: params["text"] = text
    if classes is not None: params["classes"] = classes
    if attributes is not None: params["attributes"] = attributes
    return _call("uitoolkit", "add_element", params)


@mcp.tool()
def unity_uss_add_rule(uss_path: str, selector: str, properties: dict) -> dict:
    """Append a USS rule (e.g. selector='.header', properties={'font-size':'16px'})."""
    return _call("uitoolkit", "add_uss_rule", {"ussPath": uss_path, "selector": selector, "properties": properties})


# -- Phase 7 (Animation / Particles / Lighting / Audio) ---------------------

@mcp.tool()
def unity_create_light(type: str = "Directional", name: str = None, color=None, intensity: float = None,
                       range: float = None, position: list = None, rotation: list = None) -> dict:
    """Create a Light (Directional/Point/Spot/Area) in the scene."""
    params: dict = {"type": type}
    if name is not None: params["name"] = name
    if color is not None: params["color"] = color
    if intensity is not None: params["intensity"] = intensity
    if range is not None: params["range"] = range
    if position is not None: params["position"] = position
    if rotation is not None: params["rotation"] = rotation
    return _call("lighting", "create_light", params)


@mcp.tool()
def unity_set_fog(enabled: bool = True, color=None, density: float = None, mode: str = None) -> dict:
    """Toggle/configure scene fog (RenderSettings)."""
    params: dict = {"enabled": enabled}
    if color is not None: params["color"] = color
    if density is not None: params["density"] = density
    if mode is not None: params["mode"] = mode
    return _call("lighting", "set_fog", params)


@mcp.tool()
def unity_set_skybox(material_path: str) -> dict:
    """Set the scene skybox material (RenderSettings.skybox)."""
    return _call("lighting", "set_skybox", {"materialPath": material_path})


@mcp.tool()
def unity_create_particles(name: str = None, preset: str = "Default", parent: str = None, position: list = None) -> dict:
    """Create a ParticleSystem. preset: Default/Fire/Smoke/Explosion."""
    params: dict = {"preset": preset}
    if name is not None: params["name"] = name
    if parent is not None: params["parent"] = parent
    if position is not None: params["position"] = position
    return _call("particles", "create", params)


@mcp.tool()
def unity_add_audio_source(target: str, clip_path: str = None, volume: float = None,
                           loop: bool = None, play_on_awake: bool = None, spatial_blend: float = None) -> dict:
    """Add/configure an AudioSource on a GameObject."""
    params: dict = {"target": target}
    if clip_path is not None: params["clipPath"] = clip_path
    if volume is not None: params["volume"] = volume
    if loop is not None: params["loop"] = loop
    if play_on_awake is not None: params["playOnAwake"] = play_on_awake
    if spatial_blend is not None: params["spatialBlend"] = spatial_blend
    return _call("audio", "add_source", params)


@mcp.tool()
def unity_create_animator_controller(path: str) -> dict:
    """Create an AnimatorController asset (e.g. 'Assets/Animations/Player.controller')."""
    return _call("animation", "create_controller", {"path": path})


@mcp.tool()
def unity_animator_add_state(controller_path: str, state_name: str, clip_path: str = None, layer: int = 0) -> dict:
    """Add a state to an AnimatorController (optionally with a motion clip)."""
    params: dict = {"controllerPath": controller_path, "stateName": state_name, "layer": layer}
    if clip_path is not None: params["clipPath"] = clip_path
    return _call("animation", "add_state", params)


@mcp.tool()
def unity_assign_animator(target: str, controller_path: str) -> dict:
    """Add an Animator to a GameObject (if needed) and assign the controller."""
    return _call("animation", "assign_to_animator", {"target": target, "controllerPath": controller_path})


# -- Phase 8 (2D: Sprite + Tilemap) -----------------------------------------

@mcp.tool()
def unity_set_sprite(target: str, sprite_path: str, sub_sprite: str = None) -> dict:
    """Set the sprite on a GameObject's SpriteRenderer (adds one if missing).

    sprite_path: an asset path, or 'builtin:UI/Skin/UISprite.psd' for a built-in sprite.
    """
    params: dict = {"target": target, "spritePath": sprite_path}
    if sub_sprite is not None: params["subSprite"] = sub_sprite
    return _call("sprite", "set_sprite", params)


@mcp.tool()
def unity_create_grid(name: str = "Grid", cell_size: list = None) -> dict:
    """Create a 2D Grid GameObject (parent for Tilemaps)."""
    params: dict = {"name": name}
    if cell_size is not None: params["cellSize"] = cell_size
    return _call("tilemap", "create_grid", params)


@mcp.tool()
def unity_create_tilemap(grid_target: str, name: str = "Tilemap", collider: bool = False) -> dict:
    """Create a Tilemap (+ TilemapRenderer, optional TilemapCollider2D) under a Grid."""
    return _call("tilemap", "create_tilemap", {"gridTarget": grid_target, "name": name, "collider": collider})


@mcp.tool()
def unity_create_tile(path: str, sprite_path: str, collider_type: str = "Sprite") -> dict:
    """Create a Tile asset from a sprite (sprite_path may be 'builtin:UI/Skin/UISprite.psd')."""
    return _call("tilemap", "create_tile_asset",
                 {"path": path, "spritePath": sprite_path, "colliderType": collider_type})


@mcp.tool()
def unity_paint_tiles(tilemap_target: str, from_cell: list, to_cell: list, tile_path: str) -> dict:
    """Fill a rectangular box of cells (from_cell [x,y] to to_cell [x,y]) with a tile."""
    return _call("tilemap", "paint_box",
                 {"tilemapTarget": tilemap_target, "from": from_cell, "to": to_cell, "tilePath": tile_path})


# -- Phase 9 (Physics / Input / Timeline; Cinemachine stub) -----------------

@mcp.tool()
def unity_add_rigidbody(target: str, mass: float = None, use_gravity: bool = None,
                        is_kinematic: bool = None, dimensions: str = None) -> dict:
    """Add a Rigidbody (3D) or Rigidbody2D to a GameObject (auto-detected; force with dimensions='2D'/'3D')."""
    params: dict = {"target": target}
    if mass is not None: params["mass"] = mass
    if use_gravity is not None: params["useGravity"] = use_gravity
    if is_kinematic is not None: params["isKinematic"] = is_kinematic
    if dimensions is not None: params["dimensions"] = dimensions
    return _call("physics", "add_rigidbody", params)


@mcp.tool()
def unity_add_collider(target: str, shape: str, is_trigger: bool = False) -> dict:
    """Add a collider. shape: Box/Sphere/Capsule/Mesh (3D) or Box2D/Circle2D/Capsule2D/Polygon2D (2D)."""
    return _call("physics", "add_collider", {"target": target, "shape": shape, "isTrigger": is_trigger})


@mcp.tool()
def unity_create_input_actions(path: str, map: str = None) -> dict:
    """Create an Input System .inputactions asset (optionally with an initial action map).
    Requires the Input System package."""
    params: dict = {"path": path}
    if map is not None: params["map"] = map
    return _call("input", "create_action_asset", params)


@mcp.tool()
def unity_input_add_binding(path: str, map: str, action: str, binding: str, action_type: str = "Button") -> dict:
    """Ensure an action exists on a map and add a control binding (e.g. '<Keyboard>/space'). Requires Input System."""
    _call("input", "add_action", {"path": path, "map": map, "actionName": action, "type": action_type})
    return _call("input", "add_binding", {"path": path, "map": map, "action": action, "binding": binding})


@mcp.tool()
def unity_create_timeline(path: str) -> dict:
    """Create a TimelineAsset (.playable). Requires the Timeline package."""
    return _call("timeline", "create_timeline", {"path": path})


@mcp.tool()
def unity_timeline_add_track(timeline_path: str, track_type: str = "Animation", name: str = None) -> dict:
    """Add a track to a Timeline. track_type: Animation/Activation/Audio/Playable/Signal/Group."""
    params: dict = {"timelinePath": timeline_path, "trackType": track_type}
    if name is not None: params["name"] = name
    return _call("timeline", "add_track", params)


# -- Package Manager + animation-curve authoring ----------------------------

@mcp.tool()
def unity_list_packages() -> dict:
    """List the project's UPM packages (from Packages/manifest.json)."""
    return _call("package", "list")


@mcp.tool()
def unity_add_package(id: str, version: str = None) -> dict:
    """Add a UPM package (e.g. 'com.unity.cinemachine', 'com.unity.animation.rigging').

    UPM resolves asynchronously and usually triggers a recompile/reload; call
    unity_list_packages after a few seconds to confirm.
    """
    params: dict = {"id": id}
    if version is not None: params["version"] = version
    return _call("package", "add", params)


@mcp.tool()
def unity_remove_package(id: str, confirm: bool = False) -> dict:
    """Remove a UPM package (requires confirm=true)."""
    return _call("package", "remove", {"id": id, "confirm": confirm})


@mcp.tool()
def unity_animation_add_curve(clip_path: str, property: str, keyframes: list,
                              type: str = "Transform", relative_path: str = "") -> dict:
    """Author a float animation curve on a clip.

    property e.g. 'localPosition.y' or 'localEulerAngles.z'. keyframes: [{time, value}, ...].
    type: the animated component (default Transform). relative_path: child path under the
    animated GameObject (default '' = the object itself). Build walk/idle bobs, rotations, etc.
    """
    return _call("animation", "add_curve", {
        "clipPath": clip_path, "property": property, "keyframes": keyframes,
        "type": type, "relativePath": relative_path,
    })


@mcp.tool()
def unity_animation_add_sprite_curve(clip_path: str, keyframes: list, relative_path: str = "") -> dict:
    """Author a sprite-swap (frame-by-frame) 2D animation on a SpriteRenderer.

    keyframes: [{time, sprite}], where sprite is an asset path or 'builtin:...'. This is the
    classic 2D walk/idle approach (swap the SpriteRenderer's sprite over time).
    """
    return _call("animation", "add_object_curve", {
        "clipPath": clip_path, "type": "SpriteRenderer", "property": "m_Sprite",
        "keyframes": keyframes, "relativePath": relative_path,
    })


@mcp.tool()
def unity_setup_ik(target: str, root_bone: str = None, mid_bone: str = None, tip_bone: str = None,
                   target_object: str = None) -> dict:
    """Set up a 3D Two-Bone IK chain (Animation Rigging) on a character.

    Adds a RigBuilder to `target`, a Rig child, and a TwoBoneIKConstraint. Pass the three
    bones (e.g. upper-arm/fore-arm/hand or thigh/shin/foot); a target object is created if
    not provided. Requires com.unity.animation.rigging.
    """
    params: dict = {"target": target}
    if root_bone is not None: params["rootBone"] = root_bone
    if mid_bone is not None: params["midBone"] = mid_bone
    if tip_bone is not None: params["tipBone"] = tip_bone
    if target_object is not None: params["targetObject"] = target_object
    return _call("animation", "setup_ik", params)


# -- Phase 10 (Capture / Screenshot) — returns images Claude can see ---------

@mcp.tool()
def unity_screenshot_scene(width: int = 1280, height: int = 720) -> Image:
    """Capture the Unity Scene view as a PNG so you can see the current scene."""
    data = _call("capture", "screenshot_scene_view", {"width": width, "height": height})
    return Image(data=base64.b64decode(data["base64Png"]), format="png")


@mcp.tool()
def unity_screenshot_game(width: int = 1280, height: int = 720) -> Image:
    """Capture the Game view (renders the main camera) as a PNG."""
    data = _call("capture", "screenshot_game_view", {"width": width, "height": height})
    return Image(data=base64.b64decode(data["base64Png"]), format="png")


@mcp.tool()
def unity_render_camera(target: str, width: int = 1280, height: int = 720) -> Image:
    """Render a specific Camera GameObject (by instanceId/path/name) to a PNG."""
    data = _call("capture", "render_camera", {"cameraTarget": target, "width": width, "height": height})
    return Image(data=base64.b64decode(data["base64Png"]), format="png")


# -- Phase 11 (Build + Test) ------------------------------------------------

@mcp.tool()
def unity_get_player_settings() -> dict:
    """Read key Player Settings (company/product name, bundle id, version, scripting backend, active target)."""
    return _call("build", "get_player_settings")


@mcp.tool()
def unity_set_company_name(name: str) -> dict:
    """Set PlayerSettings.companyName."""
    return _call("build", "set_company_name", {"name": name})


@mcp.tool()
def unity_set_product_name(name: str) -> dict:
    """Set PlayerSettings.productName."""
    return _call("build", "set_product_name", {"name": name})


@mcp.tool()
def unity_set_define_symbols(symbols: list, group: str = "Standalone") -> dict:
    """Set scripting define symbols for a build target group (e.g. ['DEBUG_X','FEATURE_Y'])."""
    return _call("build", "set_define_symbols", {"symbols": symbols, "group": group})


@mcp.tool()
def unity_get_build_targets() -> dict:
    """List supported build targets and the active one."""
    return _call("build", "get_build_targets")


@mcp.tool()
def unity_build_player(target: str, output_path: str, development: bool = False, scenes: list = None) -> dict:
    """Build a player (e.g. target='StandaloneWindows64', output_path='Builds/Game.exe').

    Runs deferred (the editor freezes during the build); poll unity_get_build_result until done.
    scenes defaults to the enabled scenes in Build Settings.
    """
    params: dict = {"target": target, "outputPath": output_path, "development": development}
    if scenes is not None: params["scenes"] = scenes
    return _call("build", "build_player", params)


@mcp.tool()
def unity_get_build_result() -> dict:
    """Get the most recent build_player result ({done, result:{result,totalSize,totalTimeSeconds,...}})."""
    return _call("build", "get_build_result")


@mcp.tool()
def unity_run_edit_tests(filter: str = None) -> dict:
    """Run EditMode tests (TestRunnerApi). Poll unity_get_test_results until running=false."""
    params: dict = {}
    if filter is not None: params["filter"] = filter
    return _call("test", "run_editmode_tests", params)


@mcp.tool()
def unity_get_test_results() -> dict:
    """Get the latest test run results ({running, result:{status,passed,failed,skipped,...}})."""
    return _call("test", "get_test_results")


@mcp.tool()
def unity_list_tests(mode: str = "EditMode") -> dict:
    """List tests (mode: EditMode/PlayMode). Retrieval is async — call again to read the populated list."""
    return _call("test", "list_tests", {"mode": mode})


# -- Phase 12: Batch (one round-trip, one Undo group) -----------------------

@mcp.tool()
def unity_batch(ops: list, undo_group: str = "MCP Batch", atomic: bool = True) -> dict:
    """Run many operations in ONE round-trip and ONE Undo group (revert all with one Ctrl+Z).

    ops: a list of {"category","action","params"}. Earlier-created objects can be referenced
    by name in later ops (e.g. parent a Button under a Canvas created in the same batch).
    atomic=true reverts the whole batch if any op fails.
    Returns {success, count, failedIndex, results:[{success, data|error}]}.
    Example op: {"category":"ui","action":"create_canvas","params":{"name":"Menu"}}.
    """
    try:
        return _client.batch(ops, undo_group=undo_group, atomic=atomic)
    except UnityConnectionError as exc:
        raise RuntimeError(str(exc)) from exc
    except UnityError as exc:
        raise RuntimeError(f"Unity returned an error: {exc}") from exc


def main() -> None:
    """Console-script entry point: serve over stdio for Claude Desktop / Claude Code."""
    mcp.run()


if __name__ == "__main__":
    main()
