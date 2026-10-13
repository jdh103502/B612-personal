using System.Text;
using UnityEditor;
using UnityEngine;

public static class Probe
{
    public static string Run()
    {
        var sb = new StringBuilder();
        foreach (var name in new[] { "Prince", "Fox" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/B612/Models/" + name + ".fbx");
            sb.AppendLine("== " + name);
            Dump(go.transform, sb, 0);
        }
        return sb.ToString();
    }

    static void Dump(Transform t, StringBuilder sb, int depth)
    {
        sb.AppendLine(new string(' ', depth * 2) + t.name + " pos=" + t.localPosition.ToString("F3") + " rot=" + t.localEulerAngles.ToString("F0") + " scale=" + t.localScale.ToString("F2"));
        for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), sb, depth + 1);
    }
}
