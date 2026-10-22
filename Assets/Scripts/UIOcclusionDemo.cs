using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// This component belongs only to UIOcclusionTest. Enter Play Mode to build the demonstration.
public sealed class UIOcclusionDemo : MonoBehaviour
{
    private Camera view;
    private Canvas targetCanvas;
    private GraphicRaycaster raycaster;
    private EventSystem events;
    private RectTransform target;
    private GameObject blocker;
    private BoxCollider blockerCollider;
    private Material material;
    private Font font;
    private Text blockingLabel, positionLabel, colliderLabel, status;
    private bool inFront = true;
    private int clicks;
    private string lastClick = "No click received yet.";
    private string checks = "Self-check pending...";
    private readonly List<RaycastResult> results = new List<RaycastResult>();

    private void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var cameraObject = new GameObject("UI Camera (no PhysicsRaycaster)", typeof(Camera));
        cameraObject.transform.SetParent(transform);
        view = cameraObject.GetComponent<Camera>();
        view.transform.position = new Vector3(0, 0, -10);
        view.orthographic = true;
        view.orthographicSize = 5;
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color(0.04f, 0.06f, 0.1f);

        var eventObject = new GameObject("EventSystem", typeof(EventSystem));
        eventObject.transform.SetParent(transform);
        events = eventObject.GetComponent<EventSystem>();
        eventObject.AddComponent<InputSystemUIInputModule>();

        targetCanvas = MakeCanvas("Camera Canvas - physical blocking", RenderMode.ScreenSpaceCamera);
        targetCanvas.worldCamera = view;
        targetCanvas.planeDistance = 10;
        raycaster = targetCanvas.GetComponent<GraphicRaycaster>();
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.ThreeD;
        raycaster.blockingMask = 1 << 0; // Only Default layer; the blocker is on this layer.
        target = Rect(targetCanvas.transform, "Clickable UI behind cube", new Vector2(0.28f, 0.46f), new Vector2(470, 260));
        var image = target.gameObject.AddComponent<Image>();
        image.color = new Color(0.12f, 0.58f, 0.93f);
        var button = target.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            clicks++;
            lastClick = $"Button.onClick received at {Time.time:0.00}s";
            Debug.Log("[UI Occlusion] " + lastClick + "; count=" + clicks, this);
        });
        Label(target, "Button label", "CLICK THE BLUE AREA\nCenter can be blocked", new Vector2(0.5f, 0.82f), new Vector2(450, 80), 23);

        blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.name = "Blocker - Default layer - BoxCollider";
        blocker.transform.SetParent(transform);
        blocker.layer = 0;
        blocker.transform.localScale = new Vector3(2.4f, 1.5f, 0.5f);
        blockerCollider = blocker.GetComponent<BoxCollider>();
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader != null)
        {
            material = new Material(shader);
            material.color = new Color(1f, 0.47f, 0.12f);
            blocker.GetComponent<Renderer>().sharedMaterial = material;
        }

        var hud = MakeCanvas("Overlay HUD - controls stay clickable", RenderMode.ScreenSpaceOverlay).transform;
        Label(hud, "Title", "UI BLOCKED BY A 3D COLLIDER", new Vector2(0.5f, 0.93f), new Vector2(1180, 60), 30);
        blockingLabel = Control(hud, "Toggle Blocking Objects", new Vector2(0.19f, 0.81f), () =>
        {
            raycaster.blockingObjects = raycaster.blockingObjects == GraphicRaycaster.BlockingObjects.None
                ? GraphicRaycaster.BlockingObjects.ThreeD : GraphicRaycaster.BlockingObjects.None;
            RefreshControls();
        });
        positionLabel = Control(hud, "Move blocker front or behind", new Vector2(0.5f, 0.81f), () =>
        {
            inFront = !inFront;
            PositionBlocker();
            RefreshControls();
        });
        colliderLabel = Control(hud, "Toggle Collider", new Vector2(0.81f, 0.81f), () =>
        {
            blockerCollider.enabled = !blockerCollider.enabled;
            RefreshControls();
        });
        Label(hud, "Center marker", "+", new Vector2(0.28f, 0.46f), new Vector2(30, 30), 25);
        Label(hud, "Instructions", "Click the + at the center of the cube.\n" +
            "Three D + FRONT + Collider ON: blocked.\n" +
            "None, BEHIND, or Collider OFF: UI receives click.\n" +
            "Blue edges outside the cube remain clickable.\n" +
            "Visual occlusion and click blocking are separate.",
            new Vector2(0.28f, 0.16f), new Vector2(610, 150), 18);
        status = Label(hud, "Live diagnostics", "", new Vector2(0.76f, 0.44f), new Vector2(550, 360), 19);
        status.alignment = TextAnchor.UpperLeft;
        Canvas.ForceUpdateCanvases();
        PositionBlocker();
        RefreshControls();
    }

    private void PositionBlocker()
    {
        var screen = RectTransformUtility.WorldToScreenPoint(view, target.position);
        blocker.transform.position = view.ScreenToWorldPoint(new Vector3(screen.x, screen.y, inFront ? 5 : 15));
        Physics.SyncTransforms();
    }

    private bool HitsTarget(Vector2 point)
    {
        var data = new PointerEventData(events) { position = point };
        // Query only the tested GraphicRaycaster: no PhysicsRaycaster is needed for blocking.
        results.Clear();
        raycaster.Raycast(data, results);
        return results.Exists(hit => hit.gameObject == target.gameObject);
    }

    private IEnumerator Start()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        PositionBlocker();
        var point = RectTransformUtility.WorldToScreenPoint(view, target.position);
        var savedBlocking = raycaster.blockingObjects;
        var savedFront = inFront;
        var savedCollider = blockerCollider.enabled;
        bool frontBlocked, noneAllowed, behindAllowed, disabledAllowed;
        try
        {
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.ThreeD;
            blockerCollider.enabled = true;
            inFront = true;
            PositionBlocker();
            frontBlocked = !HitsTarget(point);
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            noneAllowed = HitsTarget(point);
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.ThreeD;
            inFront = false;
            PositionBlocker();
            behindAllowed = HitsTarget(point);
            inFront = true;
            blockerCollider.enabled = false;
            PositionBlocker();
            disabledAllowed = HitsTarget(point);
        }
        finally
        {
            raycaster.blockingObjects = savedBlocking;
            inFront = savedFront;
            blockerCollider.enabled = savedCollider;
            PositionBlocker();
            RefreshControls();
        }
        checks = frontBlocked && noneAllowed && behindAllowed && disabledAllowed
            ? "Raycast self-check: PASS (4 cases)" : "Raycast self-check: FAIL (see Console)";
        Debug.Log($"[UI Occlusion] {checks}; frontBlocked={frontBlocked}, noneAllowed={noneAllowed}, " +
            $"behindAllowed={behindAllowed}, colliderOffAllowed={disabledAllowed}", this);
    }

    private void LateUpdate()
    {
        PositionBlocker();
        bool centerHit = HitsTarget(RectTransformUtility.WorldToScreenPoint(view, target.position));
        string mouseHit = Mouse.current == null ? "No mouse" : HitsTarget(Mouse.current.position.ReadValue()) ? "YES" : "NO";
        status.text = "Canvas: Screen Space - Camera\nUI plane distance: 10\n" +
            "Blocker distance: " + (inFront ? "5 (in front)" : "15 (behind)") +
            "\nBlocking Mask: Default\nPhysicsRaycaster: NOT attached\n\n" +
            "UI hit at center: " + (centerHit ? "YES" : "NO - blocked") +
            "\nUI hit at mouse: " + mouseHit + "\nButton clicks received: " + clicks +
            "\n" + lastClick + "\n\n" + checks;
    }

    private void RefreshControls()
    {
        blockingLabel.text = "Blocking: " + raycaster.blockingObjects;
        positionLabel.text = "Cube: " + (inFront ? "FRONT" : "BEHIND");
        colliderLabel.text = "Collider: " + (blockerCollider.enabled ? "ON" : "OFF");
    }

    private Canvas MakeCanvas(string name, RenderMode mode)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        obj.transform.SetParent(transform, false);
        var canvas = obj.GetComponent<Canvas>();
        canvas.renderMode = mode;
        var scaler = obj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.sizeDelta = size;
        return rect;
    }

    private Text Label(Transform parent, string name, string value, Vector2 anchor, Vector2 size, int fontSize)
    {
        var text = Rect(parent, name, anchor, size).gameObject.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        return text;
    }

    private Text Control(Transform parent, string name, Vector2 anchor, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(parent, name, anchor, new Vector2(350, 50));
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.14f, 0.24f, 0.36f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        return Label(rect, "Label", name, new Vector2(0.5f, 0.5f), new Vector2(340, 45), 22);
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}
