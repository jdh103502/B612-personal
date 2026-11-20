using UnityEngine;

namespace B612
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PlanetMapNodeGraphic : UnityEngine.UI.MaskableGraphic
    {
        public Color ringColor=Color.gray;
        public float iconScale=.8f;
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();Vector2 centre=rectTransform.rect.center;float radius=Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.36f*iconScale;
            const int steps=48;
            mesh.AddVert(centre,color,Vector2.zero);
            for(int i=0;i<=steps;i++)
            {
                float angle=i*Mathf.PI*2/steps;Vector2 direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                mesh.AddVert(centre+direction*radius,color,Vector2.zero);
                mesh.AddVert(centre+direction*(radius+4),ringColor,Vector2.zero);
            }
            for(int i=0;i<steps;i++)
            {
                int a=1+i*2,b=a+2;mesh.AddTriangle(0,a,b);mesh.AddTriangle(a,a+1,b+1);mesh.AddTriangle(a,b+1,b);
            }
        }
    }
}
