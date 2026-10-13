using UnityEngine;

public static class RuntimeProbe
{
    public static string Run()
    {
        var p = GameObject.Find("Player");
        var f = GameObject.Find("Fox");
        if (!p) return "no player (frame " + Time.frameCount + ")";
        var pw = p.GetComponent<B612.GravityWalker>();
        var pc = p.GetComponent<B612.PlayerController>();
        var cam = Camera.main;
        string s = "t=" + Time.time.ToString("F1") + " ";
        s += "player pos=" + p.transform.position.ToString("F2") + " up=" + pw.Up.ToString("F2") + " grounded=" + pw.Grounded + " src=" + (pw.Source ? pw.Source.name : "none") + " vUp=" + pw.VerticalSpeed.ToString("F2") + " speed=" + pw.TangentSpeed.ToString("F2") + " input=" + pw.moveInput.ToString("F2") + " stars=" + pc.StarsCollected + "/" + pc.StarsTotal;
        if (cam) s += " camDist=" + Vector3.Distance(cam.transform.position, p.transform.position).ToString("F2");
        if (f)
        {
            var fw = f.GetComponent<B612.GravityWalker>();
            s += " | fox grounded=" + fw.Grounded + " src=" + (fw.Source ? fw.Source.name : "none") + " dist=" + Vector3.Distance(p.transform.position, f.transform.position).ToString("F2");
        }
        var rb = p.GetComponent<Rigidbody>();
        Collider[] near = Physics.OverlapSphere(p.transform.position + pw.Up * 0.39f, 0.3f);
        s += " | touching:";
        foreach (var c in near) if (c.attachedRigidbody != rb) s += " " + c.name;
        return s;
    }
}
