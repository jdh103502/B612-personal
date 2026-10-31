using UnityEngine;

namespace B612
{
    /// <summary>Small vector speech balloon. The stem/leaf icon stays upright as the tail moves.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SproutBubbleGraphic : UnityEngine.UI.MaskableGraphic
    {
        public enum BubbleIcon { Sprout, Lamb }
        BubbleIcon icon;
        public BubbleIcon Icon
        {
            get => icon;
            set { if(icon!=value) { icon=value; SetVerticesDirty(); } }
        }
        Vector2 tip = new Vector2(0f, -38f);
        public Vector2 TailTip
        {
            get => tip;
            set { if ((tip-value).sqrMagnitude > 0.01f) { tip = value; SetVerticesDirty(); } }
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            const int sides = 48;
            Color border = icon==BubbleIcon.Lamb ? new Color(1f,0.9f,0.68f) : new Color(0.95f, 0.98f, 0.91f, 1f);
            Color background = new Color(0.035f, 0.075f, 0.10f, 0.96f);
            float angle = Mathf.Atan2(tip.y / 23f, tip.x / 33f);
            // A radial fan works for this star-shaped contour, including its pointed tail.
            for (int i = 0; i < sides; i++)
            {
                float a = angle - Mathf.PI + i * Mathf.PI * 2f / sides;
                Vector2 outer = i == sides/2 ? tip : new Vector2(Mathf.Cos(a)*33f, Mathf.Sin(a)*23f);
                Vector2 inner = outer - outer.normalized * 1.8f;
                vh.AddVert(outer, border, Vector2.zero);
                vh.AddVert(inner, background, Vector2.zero);
            }
            int centre = vh.currentVertCount;
            vh.AddVert(Vector2.zero, background, Vector2.zero);
            for (int i = 0; i < sides; i++)
            {
                int a = i*2, b = (i+1)%sides*2;
                vh.AddTriangle(a,b,b+1); vh.AddTriangle(a,b+1,a+1);
                vh.AddTriangle(centre,a+1,b+1);
            }
            if(icon==BubbleIcon.Lamb)
            {
                DrawLamb(vh);
                return;
            }
            Color leaf = new Color(0.38f,0.82f,0.46f);
            Color vein = new Color(0.78f,1f,0.68f);
            Polygon(vh, leaf, new[] { new Vector2(0,2), new Vector2(-7,12), new Vector2(-20,14), new Vector2(-22,9), new Vector2(-13,1) });
            Polygon(vh, leaf, new[] { new Vector2(0,5), new Vector2(7,14), new Vector2(21,16), new Vector2(18,8), new Vector2(9,2) });
            Line(vh, new Vector2(0,-13), new Vector2(1,-3), 2.8f, vein);
            Line(vh, new Vector2(1,-3), new Vector2(-1,7), 2.8f, vein);
            Line(vh, new Vector2(0,3), new Vector2(-16,10), 1.5f, vein);
            Line(vh, new Vector2(0,5), new Vector2(16,12), 1.5f, vein);
        }

        static void DrawLamb(UnityEngine.UI.VertexHelper vh)
        {
            Color wool=new Color(.97f,.91f,.77f), face=new Color(.64f,.52f,.39f);
            Line(vh,new Vector2(-11,-7),new Vector2(-11,-15),3f,face);
            Line(vh,new Vector2(2,-7),new Vector2(2,-15),3f,face);
            Disc(vh,new Vector2(-5,0),14f,10f,wool);
            Disc(vh,new Vector2(-15,4),6f,6f,wool); Disc(vh,new Vector2(-8,10),6f,6f,wool);
            Disc(vh,new Vector2(0,11),6f,6f,wool); Disc(vh,new Vector2(7,6),6f,6f,wool);
            Disc(vh,new Vector2(12,-2),6.5f,9f,face);
            Disc(vh,new Vector2(18,3),5f,2.7f,face);
            Disc(vh,new Vector2(13,2),1.2f,1.6f,new Color(.06f,.06f,.08f));
        }

        static void Disc(UnityEngine.UI.VertexHelper vh,Vector2 centre,float rx,float ry,Color color)
        {
            const int steps=20;
            int start=vh.currentVertCount;
            vh.AddVert(centre,color,Vector2.zero);
            for(int i=0;i<=steps;i++)
            {
                float angle=i*Mathf.PI*2f/steps;
                vh.AddVert(centre+new Vector2(Mathf.Cos(angle)*rx,Mathf.Sin(angle)*ry),color,Vector2.zero);
                if(i>0) vh.AddTriangle(start,start+i,start+i+1);
            }
        }

        static void Polygon(UnityEngine.UI.VertexHelper vh, Color color, Vector2[] points)
        {
            int start = vh.currentVertCount;
            foreach (var p in points) vh.AddVert(p,color,Vector2.zero);
            for (int i=1;i<points.Length-1;i++) vh.AddTriangle(start,start+i,start+i+1);
        }

        static void Line(UnityEngine.UI.VertexHelper vh, Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 d = (to-from).normalized;
            Vector2 normal = new Vector2(-d.y,d.x)*width*0.5f;
            int start=vh.currentVertCount;
            vh.AddVert(from-normal,color,Vector2.zero); vh.AddVert(from+normal,color,Vector2.zero);
            vh.AddVert(to+normal,color,Vector2.zero); vh.AddVert(to-normal,color,Vector2.zero);
            vh.AddTriangle(start,start+1,start+2); vh.AddTriangle(start,start+2,start+3);
        }
    }
}
