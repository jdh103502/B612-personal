using UnityEngine;

public static class JumpProbe
{
    public static string Run()
    {
        var p = GameObject.Find("Player");
        var w = p.GetComponent<B612.GravityWalker>();
        var src = w.Source;
        float r = src ? (p.transform.position - src.transform.position).magnitude : 0f;
        return "t=" + Time.time.ToString("F2") + " r=" + r.ToString("F3") + " vUp=" + w.VerticalSpeed.ToString("F2") + " grounded=" + w.Grounded + " pos=" + p.transform.position.ToString("F2");
    }

    public static string Stones()
    {
        string s = "";
        Vector3 prev = Vector3.zero; bool has = false;
        for (int i = 1; i <= 4; i++)
        {
            var g = GameObject.Find("Stepping Stone " + i);
            if (!g) continue;
            var pos = g.transform.position;
            s += "stone" + i + " " + pos.ToString("F2") + (has ? " gap=" + Vector3.Distance(prev, pos).ToString("F2") : "") + "\n";
            prev = pos; has = true;
        }
        var cp = GameObject.Find("Clock Planet");
        if (cp) s += "clock " + cp.transform.position.ToString("F2") + " gap=" + (Vector3.Distance(prev, cp.transform.position) - 1.2f).ToString("F2") + " (to surface)\n";
        return s;
    }
}
