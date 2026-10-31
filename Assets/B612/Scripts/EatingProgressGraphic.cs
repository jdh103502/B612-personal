using UnityEngine;

namespace B612
{
    /// <summary>Clockwise pie, starting at noon, for one sprout's actual eating progress.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EatingProgressGraphic : UnityEngine.UI.MaskableGraphic
    {
        float progress;
        public float Progress
        {
            get=>progress;
            set { value=Mathf.Clamp01(value); if(Mathf.Abs(progress-value)>.0001f) { progress=value; SetVerticesDirty(); } }
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            const float radius=22f;
            Color rim=new Color(.97f,.97f,.87f),empty=new Color(.035f,.065f,.09f,.94f),filled=new Color(.64f,.89f,.66f,.98f);
            Sector(vh,radius,1f,empty);
            if(progress>0f) Sector(vh,radius-2f,progress,filled);
            const int segments=80;
            for(int i=0;i<segments;i++)
            {
                Vector2 a=Direction(i/(float)segments),b=Direction((i+1)/(float)segments);
                int start=vh.currentVertCount;
                vh.AddVert(a*radius,rim,Vector2.zero); vh.AddVert(b*radius,rim,Vector2.zero);
                vh.AddVert(b*(radius-1.8f),rim,Vector2.zero); vh.AddVert(a*(radius-1.8f),rim,Vector2.zero);
                vh.AddTriangle(start,start+1,start+2); vh.AddTriangle(start,start+2,start+3);
            }
            Line(vh,Vector2.zero,Vector2.up*(radius-1f),1.3f,rim);
            if(progress>0f && progress<1f) Line(vh,Vector2.zero,Direction(progress)*(radius-1f),1.3f,rim);
        }

        static Vector2 Direction(float fraction)
        {
            float a=Mathf.PI*.5f-fraction*Mathf.PI*2f;
            return new Vector2(Mathf.Cos(a),Mathf.Sin(a));
        }

        static void Sector(UnityEngine.UI.VertexHelper vh,float radius,float fraction,Color color)
        {
            int count=Mathf.Max(1,Mathf.CeilToInt(fraction*80f)),start=vh.currentVertCount;
            vh.AddVert(Vector2.zero,color,Vector2.zero);
            for(int i=0;i<=count;i++)
            {
                vh.AddVert(Direction(fraction*i/count)*radius,color,Vector2.zero);
                if(i>0) vh.AddTriangle(start,start+i,start+i+1);
            }
        }

        static void Line(UnityEngine.UI.VertexHelper vh,Vector2 from,Vector2 to,float width,Color color)
        {
            Vector2 d=(to-from).normalized,n=new Vector2(-d.y,d.x)*width*.5f;
            int start=vh.currentVertCount;
            vh.AddVert(from-n,color,Vector2.zero); vh.AddVert(from+n,color,Vector2.zero);
            vh.AddVert(to+n,color,Vector2.zero); vh.AddVert(to-n,color,Vector2.zero);
            vh.AddTriangle(start,start+1,start+2); vh.AddTriangle(start,start+2,start+3);
        }
    }
}
