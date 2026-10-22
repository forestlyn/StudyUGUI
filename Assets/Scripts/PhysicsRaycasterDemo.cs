using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Attached only to the copied test scene. The demonstration is built on entering Play Mode.
public sealed class PhysicsRaycasterDemo : MonoBehaviour
{
    private readonly List<RaycastResult> hits = new List<RaycastResult>();
    private readonly Queue<string> history = new Queue<string>();
    private EventSystem events;
    private Camera testCamera;
    private PhysicsRaycaster physicsRaycaster;
    private Image overlay;
    private GameObject cube;
    private Text status;
    private Text log;
    private Text overlaySwitch;
    private Text physicsSwitch;
    private Font font;
    private Material cubeMaterial;
    private string checkResult = "Checking raycast order...";

    private void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var cameraObject = new GameObject("Test Camera - PhysicsRaycaster", typeof(Camera));
        cameraObject.transform.SetParent(transform);
        testCamera = cameraObject.GetComponent<Camera>();
        testCamera.transform.position = new Vector3(0, 0, -10);
        testCamera.orthographic = true;
        testCamera.orthographicSize = 5;
        testCamera.clearFlags = CameraClearFlags.SolidColor;
        testCamera.backgroundColor = new Color(0.045f, 0.065f, 0.1f);
        physicsRaycaster = cameraObject.AddComponent<PhysicsRaycaster>();

        var eventObject = new GameObject("Test EventSystem", typeof(EventSystem));
        eventObject.transform.SetParent(transform);
        events = eventObject.GetComponent<EventSystem>();
        // OnEnable assigns the package's default mouse/keyboard/gamepad UI actions.
        eventObject.AddComponent<InputSystemUIInputModule>();

        cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "3D Cube (BoxCollider)";
        cube.transform.SetParent(transform);
        cube.transform.localScale = new Vector3(4.4f, 3.4f, 1);
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader != null)
        {
            cubeMaterial = new Material(shader);
            cubeMaterial.color = new Color(1f, 0.55f, 0.13f);
            cube.GetComponent<Renderer>().sharedMaterial = cubeMaterial;
        }
        AttachReceiver(cube, "CUBE");

        var canvasObject = new GameObject("Test Overlay Canvas", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        var canvas = canvasObject.transform;

        Label(canvas, "Title", "ONE EVENT SYSTEM / UI + 3D", new Vector2(0.5f, 0.92f),
            new Vector2(1160, 60), 30);
        overlaySwitch = MakeButton(canvas, "Toggle UI raycast", new Vector2(0.25f, 0.8f), () =>
        {
            overlay.raycastTarget = !overlay.raycastTarget;
            RefreshButtons();
        });
        physicsSwitch = MakeButton(canvas, "Toggle physics raycaster", new Vector2(0.75f, 0.8f), () =>
        {
            physicsRaycaster.enabled = !physicsRaycaster.enabled;
            RefreshButtons();
        });

        var panel = Rect(canvas, "Overlay Target", new Vector2(0.28f, 0.46f), new Vector2(220, 150));
        overlay = panel.gameObject.AddComponent<Image>();
        overlay.color = new Color(0.12f, 0.6f, 0.95f, 0.85f);
        AttachReceiver(panel.gameObject, "OVERLAY UI");
        Label(panel, "Target label", "OVERLAY UI\nClick here", new Vector2(0.5f, 0.5f),
            new Vector2(210, 100), 24);
        Label(canvas, "Instructions", "Orange = 3D cube with BoxCollider\n" +
            "1. Both ON: center click goes to UI.\n" +
            "2. UI OFF: same click goes to cube.\n" +
            "3. Both OFF: neither receives clicks.\n" +
            "The UI stays visible when its raycast is OFF.",
            new Vector2(0.28f, 0.15f), new Vector2(580, 160), 18);

        status = Label(canvas, "Live hit order", "", new Vector2(0.75f, 0.59f),
            new Vector2(570, 210), 17);
        status.alignment = TextAnchor.UpperLeft;
        log = Label(canvas, "Event log", "Move to the target, then click.", new Vector2(0.75f, 0.28f),
            new Vector2(570, 220), 16);
        log.alignment = TextAnchor.UpperLeft;
        RefreshButtons();
        PositionCube();
    }

    private void PositionCube()
    {
        cube.transform.position = testCamera.ViewportToWorldPoint(new Vector3(0.28f, 0.46f, 10));
    }

    private IEnumerator Start()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        Physics.SyncTransforms();
        var data = new PointerEventData(events)
        {
            position = RectTransformUtility.WorldToScreenPoint(null, overlay.rectTransform.position)
        };
        bool savedUI = overlay.raycastTarget;
        bool savedPhysics = physicsRaycaster.enabled;
        bool uiWins;
        bool cubeWins;
        bool noHit;
        try
        {
            overlay.raycastTarget = true;
            physicsRaycaster.enabled = true;
            events.RaycastAll(data, hits);
            uiWins = hits.Count > 0 && hits[0].gameObject == overlay.gameObject &&
                hits.Exists(hit => hit.gameObject == cube);
            overlay.raycastTarget = false;
            events.RaycastAll(data, hits);
            cubeWins = hits.Count > 0 && hits[0].gameObject == cube;
            physicsRaycaster.enabled = false;
            events.RaycastAll(data, hits);
            noHit = hits.Count == 0;
        }
        finally
        {
            overlay.raycastTarget = savedUI;
            physicsRaycaster.enabled = savedPhysics;
            RefreshButtons();
        }
        checkResult = uiWins && cubeWins && noHit
            ? "Raycast self-check: PASS (UI > cube / cube / none)"
            : "Raycast self-check: FAIL - see Console";
        Debug.Log("[Raycast Demo] " + checkResult +
            $"; overlayFirst={uiWins}, cubeFirst={cubeWins}, neither={noHit}", this);
    }

    private void LateUpdate()
    {
        PositionCube();
        var text = new StringBuilder();
        text.AppendLine("Input module: " + (events.currentInputModule != null
            ? events.currentInputModule.GetType().Name : "waiting"));
        text.AppendLine(checkResult);
        text.AppendLine("\nRaycastAll order at mouse (first wins):");
        if (Mouse.current != null)
        {
            var data = new PointerEventData(events) { position = Mouse.current.position.ReadValue() };
            events.RaycastAll(data, hits);
            for (int i = 0; i < Mathf.Min(hits.Count, 4); i++)
                text.AppendLine($"{i + 1}. {hits[i].gameObject.name}\n    {hits[i].module.GetType().Name}");
            if (hits.Count == 0) text.AppendLine("(none)");
        }
        else text.AppendLine("No mouse connected.");
        status.text = text.ToString();
    }

    public void Record(string message)
    {
        string entry = $"{Time.time:0.00}  {message}";
        history.Enqueue(entry);
        while (history.Count > 8) history.Dequeue();
        log.text = "ACTUAL POINTER EVENTS\n" + string.Join("\n", history.ToArray());
        Debug.Log("[Raycast Demo] " + entry, this);
    }

    private void AttachReceiver(GameObject target, string label)
    {
        var receiver = target.AddComponent<RaycastDemoTarget>();
        receiver.demo = this;
        receiver.targetLabel = label;
    }

    private void RefreshButtons()
    {
        overlaySwitch.text = "UI Raycast Target: " + (overlay.raycastTarget ? "ON" : "OFF");
        physicsSwitch.text = "PhysicsRaycaster: " + (physicsRaycaster.enabled ? "ON" : "OFF");
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
        text.fontSize = fontSize;
        text.text = value;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        return text;
    }

    private Text MakeButton(Transform parent, string name, Vector2 anchor, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(parent, name, anchor, new Vector2(440, 50));
        rect.gameObject.AddComponent<Image>().color = new Color(0.13f, 0.23f, 0.34f);
        var button = rect.gameObject.AddComponent<Button>();
        button.onClick.AddListener(action);
        return Label(rect, "Label", name, new Vector2(0.5f, 0.5f), new Vector2(430, 45), 21);
    }

    private void OnDestroy()
    {
        if (cubeMaterial != null) Destroy(cubeMaterial);
    }
}
