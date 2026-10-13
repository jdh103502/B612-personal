using UnityEngine;

namespace B612
{
    /// <summary>Minimal on-screen controls help and star counter.</summary>
    public class DemoHUD : MonoBehaviour
    {
        public PlayerController player;

        GUIStyle panel, text, title, counter, banner, prompt;
        Font font;

        void Init()
        {
            if (text != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 16);
            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.1f, 0.08f, 0.16f, 0.55f));
            bg.Apply();
            panel = new GUIStyle { normal = { background = bg }, padding = new RectOffset(12, 14, 8, 9) };
            text = new GUIStyle { font = font, fontSize = 14, stretchWidth = false, margin = new RectOffset(0, 0, 2, 2), normal = { textColor = new Color(1f, 0.97f, 0.9f) } };
            title = new GUIStyle(text) { fontSize = 16, fontStyle = FontStyle.Bold, margin = new RectOffset(0, 0, 0, 5) };
            counter = new GUIStyle(text) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(1f, 0.85f, 0.3f) } };
            banner = new GUIStyle(text) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.92f, 0.6f) } };
            prompt = new GUIStyle(panel) { font = font, fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(18, 18, 8, 9), normal = { background = bg, textColor = new Color(1f, 0.88f, 0.45f) } };
        }

        bool showHelp = true;

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.hKey.wasPressedThisFrame) showHelp = !showHelp;
        }

        void OnGUI()
        {
            Init();
            // the panel hugs its text; H hides it
            GUILayout.BeginArea(new Rect(16, 16, 320, Screen.height - 32));
            GUILayout.BeginVertical(panel, GUILayout.ExpandWidth(false));
            if (showHelp)
            {
                GUILayout.Label("B-612 데모", title);
                GUILayout.Label("이동  WASD / 방향키", text);
                GUILayout.Label("점프  Space (길게 누르면 더 높이)", text);
                GUILayout.Label("카메라  마우스 드래그 · Q / E", text);
                GUILayout.Label("처음 자리로  R   ·   도움말 숨기기  H", text);
            }
            else GUILayout.Label("도움말  H", text);
            GUILayout.EndVertical();
            GUILayout.EndArea();

            if (!player) return;
            if (player.NearbyLaunch && !player.NearbyLaunch.Busy)
            {
                var content = new GUIContent("Space   ★ 메인 행성으로 날아가기");
                var size = prompt.CalcSize(content);
                GUI.Label(new Rect((Screen.width - size.x) * 0.5f, Screen.height - size.y - 40f, size.x, size.y), content, prompt);
            }
            GUI.Label(new Rect(Screen.width - 236, 16, 220, 40), "★ " + player.StarsCollected + " / " + player.StarsTotal, counter);
            if (player.StarsTotal > 0 && player.StarsCollected >= player.StarsTotal)
                GUI.Label(new Rect(0, Screen.height * 0.18f, Screen.width, 50), "별을 모두 모았어요!", banner);
        }
    }
}
