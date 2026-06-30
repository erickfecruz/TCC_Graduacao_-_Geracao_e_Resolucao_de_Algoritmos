using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace MazeDOTS
{
    /// <summary>
    /// Scene entry point. Builds a small runtime uGUI (so no manual scene wiring is required),
    /// registers the bootstrap tag in the default ECS world, and forwards user input to the
    /// <see cref="MazeOrchestratorSystem"/> as commands. This is the only MonoBehaviour in the project.
    /// </summary>
    public class MazeBootstrap : MonoBehaviour
    {
        [Header("Defaults")]
        public int defaultWidth = 25;
        public int defaultHeight = 25;
        public float defaultStepInterval = 0.01f;

        InputField _widthField;
        InputField _heightField;
        InputField _speedField;
        GenAlgorithm _gen = GenAlgorithm.RecursiveBacktracker;
        Text _status;

        void Start()
        {
            // Register the tag entity that gates the orchestrator system.
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                var em = world.EntityManager;
                if (em.CreateEntityQuery(typeof(MazeBootstrapTag)).IsEmpty)
                {
                    var e = em.CreateEntity();
                    em.AddComponent<MazeBootstrapTag>(e);
                }
            }

            BuildUI();
        }

        void Generate()
        {
            int w = ParseInt(_widthField.text, defaultWidth);
            int h = ParseInt(_heightField.text, defaultHeight);
            float s = ParseFloat(_speedField.text, defaultStepInterval);
            MazeCommands.Enqueue(new MazeCommand
            {
                kind = MazeCommand.Kind.Generate,
                width = w,
                height = h,
                stepInterval = s,
                seed = (uint)Random.Range(1, int.MaxValue),
                gen = _gen
            });
        }

        void Solve(SolveAlgorithm algo)
        {
            MazeCommands.Enqueue(new MazeCommand { kind = MazeCommand.Kind.Solve, solve = algo });
        }

        void ResetSolution()
        {
            MazeCommands.Enqueue(new MazeCommand { kind = MazeCommand.Kind.ResetSolution });
        }

        // The orchestrator publishes authoritative status (incl. clamping / progress); mirror it here.
        void Update()
        {
            if (_status != null) _status.text = MazeStatus.Text;
        }

        static int ParseInt(string s, int fallback) => int.TryParse(s, out var v) && v > 0 ? v : fallback;
        static float ParseFloat(string s, float fallback) =>
            float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : fallback;

        // ----------------------------------------------------------------- runtime uGUI

        void BuildUI()
        {
            var canvasGo = new GameObject("MazeCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            float y = -10f;
            _widthField = AddField(canvasGo.transform, "Width", defaultWidth.ToString(), ref y);
            _heightField = AddField(canvasGo.transform, "Height", defaultHeight.ToString(), ref y);
            _speedField = AddField(canvasGo.transform, "Step (s)", defaultStepInterval.ToString("0.###"), ref y);

            AddButton(canvasGo.transform, "Gen: Binary Tree", ref y, () => { _gen = GenAlgorithm.BinaryTree; Generate(); });
            AddButton(canvasGo.transform, "Gen: Backtracker", ref y, () => { _gen = GenAlgorithm.RecursiveBacktracker; Generate(); });
            AddButton(canvasGo.transform, "Gen: Eller", ref y, () => { _gen = GenAlgorithm.Eller; Generate(); });

            AddButton(canvasGo.transform, "Solve: Wall Follower L", ref y, () => Solve(SolveAlgorithm.WallFollowerLeft));
            AddButton(canvasGo.transform, "Solve: Wall Follower R", ref y, () => Solve(SolveAlgorithm.WallFollowerRight));
            AddButton(canvasGo.transform, "Solve: Dead-end Fill", ref y, () => Solve(SolveAlgorithm.DeadEndFilling));
            AddButton(canvasGo.transform, "Solve: Flood Fill", ref y, () => Solve(SolveAlgorithm.FloodFill));
            AddButton(canvasGo.transform, "Clear Solution", ref y, ResetSolution);

            _status = AddLabel(canvasGo.transform, "Ready", ref y);
        }

        InputField AddField(Transform parent, string label, string value, ref float y)
        {
            var go = NewUIElement(parent, label + "Field", 200f, 26f, 10f, ref y);
            var img = go.AddComponent<Image>();
            img.color = new Color(1, 1, 1, 0.9f);
            var input = go.AddComponent<InputField>();

            var textGo = NewChildText(go.transform, "Text", Color.black);
            input.textComponent = textGo.GetComponent<Text>();
            var ph = NewChildText(go.transform, "Placeholder", Color.gray);
            ph.GetComponent<Text>().text = label;
            input.placeholder = ph.GetComponent<Text>();
            input.text = value;
            return input;
        }

        void AddButton(Transform parent, string label, ref float y, UnityEngine.Events.UnityAction onClick)
        {
            var go = NewUIElement(parent, label, 200f, 26f, 10f, ref y);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.4f, 0.8f, 0.95f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            var t = NewChildText(go.transform, "Text", Color.white);
            t.GetComponent<Text>().text = label;
        }

        Text AddLabel(Transform parent, string text, ref float y)
        {
            var go = NewUIElement(parent, "Status", 260f, 26f, 10f, ref y);
            var t = NewChildText(go.transform, "Text", Color.yellow);
            t.GetComponent<Text>().text = text;
            return t.GetComponent<Text>();
        }

        static GameObject NewUIElement(Transform parent, string name, float w, float h, float x, ref float y)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
            y -= (h + 4f);
            return go;
        }

        static GameObject NewChildText(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6, 2);
            rt.offsetMax = new Vector2(-6, -2);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.color = color;
            t.alignment = TextAnchor.MiddleLeft;
            t.fontSize = 14;
            return go;
        }
    }
}
