using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEditor.Events;
using System.Reflection;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
using UnityMCP.Utils;

namespace UnityMCP.Handlers.V1
{
    /// <summary>uGUI authoring: Canvas + EventSystem, elements via DefaultControls, text/rect/color edits.</summary>
    public class UIHandler : HandlerBase
    {
        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "create_canvas": return CreateCanvas(p);
                case "create_element": return CreateElement(p);
                case "set_text": return SetText(p);
                case "set_rect": return SetRect(p);
                case "set_color": return SetColor(p);
                case "bind_onclick": return BindOnClick(p);
                default: throw UnknownAction(action);
            }
        }

        static object CreateCanvas(JObject p)
        {
            string name = OptString(p, "name", "Canvas");
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            EnsureEventSystem();
            MarkDirty(go);
            Selection.activeGameObject = go;
            return Info(go);
        }

        static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
        }

        static object CreateElement(JObject p)
        {
            string type = RequireString(p, "type");
            var parent = ObjectFinder.Resolve(p["parent"]);
            var res = StandardResources();

            GameObject el;
            switch (type.ToLowerInvariant())
            {
                case "panel": el = DefaultControls.CreatePanel(res); break;
                case "button": el = DefaultControls.CreateButton(res); break;
                case "text": el = DefaultControls.CreateText(res); break;
                case "image": el = DefaultControls.CreateImage(res); break;
                case "rawimage": el = DefaultControls.CreateRawImage(res); break;
                case "inputfield": el = DefaultControls.CreateInputField(res); break;
                case "toggle": el = DefaultControls.CreateToggle(res); break;
                case "slider": el = DefaultControls.CreateSlider(res); break;
                case "scrollbar": el = DefaultControls.CreateScrollbar(res); break;
                case "dropdown": el = DefaultControls.CreateDropdown(res); break;
                case "scrollview": el = DefaultControls.CreateScrollView(res); break;
                case "empty":
                case "rect": el = new GameObject("UIElement", typeof(RectTransform)); break;
                default: throw Invalid("Unknown UI type '" + type + "'. Use Panel/Button/Text/Image/RawImage/InputField/Toggle/Slider/Scrollbar/Dropdown/ScrollView/Empty.");
            }

            string name = OptString(p, "name", null);
            if (!string.IsNullOrEmpty(name)) el.name = name;
            Undo.RegisterCreatedObjectUndo(el, "Create UI " + type);
            el.transform.SetParent(parent.transform, false);

            string text = OptString(p, "text", null);
            if (text != null) ApplyText(el, text);

            var rt = el.GetComponent<RectTransform>();
            if (rt != null)
            {
                if (p["anchoredPosition"] != null) rt.anchoredPosition = ValueParser.ToVector2(p["anchoredPosition"]);
                if (p["size"] != null) rt.sizeDelta = ValueParser.ToVector2(p["size"]);
            }
            MarkDirty(el);
            Selection.activeGameObject = el;
            return Info(el);
        }

        static object SetText(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            string text = RequireString(p, "text");
            if (!ApplyText(go, text))
                throw new HandlerException(ErrorCodes.NOT_FOUND, "No Text component on '" + go.name + "' or its children.");
            MarkDirty(go);
            return new { ok = true, text = text };
        }

        static bool ApplyText(GameObject go, string text)
        {
            var t = go.GetComponentInChildren<Text>(true);
            if (t == null) return false;
            Undo.RecordObject(t, "Set Text");
            t.text = text;
            return true;
        }

        static object SetColor(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var g = go.GetComponent<Graphic>();
            if (g == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No Image/Text (Graphic) on '" + go.name + "'.");
            Undo.RecordObject(g, "Set Color");
            g.color = ValueParser.ToColor(p["color"]);
            MarkDirty(go);
            return new { ok = true };
        }

        static object SetRect(JObject p)
        {
            var go = ObjectFinder.Resolve(p["target"]);
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) throw new HandlerException(ErrorCodes.INVALID_PARAMS, "'" + go.name + "' has no RectTransform.");
            Undo.RecordObject(rt, "Set Rect");
            if (p["anchoredPosition"] != null) rt.anchoredPosition = ValueParser.ToVector2(p["anchoredPosition"]);
            if (p["size"] != null) rt.sizeDelta = ValueParser.ToVector2(p["size"]);
            if (p["anchorMin"] != null) rt.anchorMin = ValueParser.ToVector2(p["anchorMin"]);
            if (p["anchorMax"] != null) rt.anchorMax = ValueParser.ToVector2(p["anchorMax"]);
            if (p["pivot"] != null) rt.pivot = ValueParser.ToVector2(p["pivot"]);
            MarkDirty(go);
            return Info(go);
        }

        static object BindOnClick(JObject p)
        {
            var buttonGo = ObjectFinder.Resolve(p["target"]);
            var button = buttonGo.GetComponent<Button>();
            if (button == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "No Button on '" + buttonGo.name + "'.");

            var handlerGo = (p["handlerTarget"] != null && p["handlerTarget"].Type != JTokenType.Null)
                ? ObjectFinder.Resolve(p["handlerTarget"]) : buttonGo;
            string compName = RequireString(p, "component");
            var compType = TypeResolver.ResolveComponentType(compName);
            if (compType == null) throw new HandlerException(ErrorCodes.TYPE_NOT_FOUND, "Type '" + compName + "' not found.");
            var comp = handlerGo.GetComponent(compType);
            if (comp == null) throw new HandlerException(ErrorCodes.NOT_FOUND, "Component '" + compName + "' is not on '" + handlerGo.name + "'.");

            string method = RequireString(p, "method");
            var mi = compType.GetMethod(method, BindingFlags.Public | BindingFlags.Instance, null, System.Type.EmptyTypes, null);
            if (mi == null)
                throw new HandlerException(ErrorCodes.NOT_FOUND, "No public parameterless method '" + method + "' on '" + compName + "'.");

            var action = (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), comp, mi);
            Undo.RecordObject(button, "Bind OnClick");
            UnityEventTools.AddVoidPersistentListener(button.onClick, action);
            EditorUtility.SetDirty(button);
            if (buttonGo.scene.IsValid()) EditorSceneManager.MarkSceneDirty(buttonGo.scene);
            return new { ok = true, button = buttonGo.name, bound = compName + "." + method };
        }

        static DefaultControls.Resources StandardResources()
        {
            var r = new DefaultControls.Resources();
            r.standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            r.background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            r.inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd");
            r.knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            r.checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
            r.dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd");
            r.mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd");
            return r;
        }

        static void MarkDirty(GameObject go)
        {
            EditorUtility.SetDirty(go);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
        }

        static object Info(GameObject go) => new
        {
            instanceId = go.GetInstanceID(),
            name = go.name,
            path = ObjectFinder.GetPath(go),
        };
    }
}
