using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    [DefaultExecutionOrder(110)]
    public sealed class LambEatingIndicators : MonoBehaviour
    {
        public LambCardHand hand;
        public SproutHorizonIndicators horizon;
        public Camera viewCamera;
        public RectTransform canvasRect;
        [Min(0f)] public float height=0.95f;
        public int VisibleCount { get; private set; }

        sealed class View
        {
            public LambAgent lamb;
            public RectTransform rect;
            public CanvasGroup group;
            public EatingProgressGraphic graphic;
        }
        readonly List<View> views=new List<View>();
        readonly List<LambAgent> eating=new List<LambAgent>();

        void LateUpdate()
        {
            if(!hand || !viewCamera || !canvasRect || !horizon) return;
            eating.Clear();
            foreach(var lamb in hand.Deployed)
                if(lamb && lamb.gameObject.activeInHierarchy && lamb.State==LambAgent.LambState.Grazing && lamb.Target!=null && lamb.Target.IsActive)
                    eating.Add(lamb);
            while(views.Count<eating.Count) CreateView();
            VisibleCount=0;
            for(int i=0;i<views.Count;i++)
            {
                var view=views[i];
                var lamb=i<eating.Count?eating[i]:null;
                if(view.lamb!=lamb) { view.lamb=lamb; view.group.alpha=0f; }
                Vector3 point=Vector3.zero; float fraction=0f;
                bool show=lamb && TryGetPresentation(lamb,out point,out fraction);
                view.group.alpha=Mathf.MoveTowards(view.group.alpha,show?1f:0f,Time.unscaledDeltaTime*10f);
                if(!show) continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,viewCamera.WorldToScreenPoint(point),null,out var local);
                view.rect.anchoredPosition=local;
                view.graphic.Progress=fraction;
                if(view.group.alpha>.5f) VisibleCount++;
            }
        }

        public bool TryGetPresentation(LambAgent lamb,out Vector3 point,out float fraction)
        {
            point=Vector3.zero; fraction=0f;
            if(!lamb || !lamb.gameObject.activeInHierarchy || lamb.State!=LambAgent.LambState.Grazing || !lamb.Owner || lamb.Target==null || !lamb.Target.IsActive) return false;
            fraction=Mathf.Clamp01(lamb.EatingProgress/Mathf.Max(.01f,lamb.Owner.secondsPerSprout));
            Vector3 up=lamb.transform.up;
            point=lamb.transform.position+up*height;
            Vector3 viewport=viewCamera.WorldToViewportPoint(point);
            if(viewport.z<=viewCamera.nearClipPlane || viewport.x<0f || viewport.x>1f || viewport.y<0f || viewport.y>1f) return false;
            // Do not reveal a hidden lamb just because its floating progress circle clears the rim.
            return !horizon.IsPointOccluded(lamb.transform.position+up*.58f);
        }

        void CreateView()
        {
            var go=new GameObject("Lamb Eating Progress "+(views.Count+1),typeof(RectTransform),typeof(CanvasGroup),typeof(EatingProgressGraphic));
            var rect=go.GetComponent<RectTransform>(); rect.SetParent(transform,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f); rect.sizeDelta=new Vector2(52f,52f);
            var group=go.GetComponent<CanvasGroup>(); group.alpha=0f; group.interactable=false; group.blocksRaycasts=false;
            var graphic=go.GetComponent<EatingProgressGraphic>(); graphic.raycastTarget=false;
            views.Add(new View {rect=rect,group=group,graphic=graphic});
        }

        void OnDisable() { foreach(var view in views) if(view.group) view.group.alpha=0f; VisibleCount=0; }
    }
}
