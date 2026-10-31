using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    /// <summary>Planet occlusion + surface-geodesic horizon hints on the existing overlay Canvas.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class SproutHorizonIndicators : MonoBehaviour
    {
        public BaobabSurge surge;
        public Camera viewCamera;
        public RectTransform canvasRect;
        public LambCardHand lambHand;
        [Min(0.02f)] public float refreshSeconds = 0.08f;
        [Min(0f)] public float horizonGap = 43f;
        public float maxTailLength = 160f;
        [Min(0f)] public float nearDistance = 1.5f;
        [Min(0f)] public float farDistance = 12f;
        [Min(0.1f)] public float nearScale = 1.2f;
        [Min(0.1f)] public float farScale = 0.62f;
        public int VisibleMarkerCount { get; private set; }
        public int VisiblePickupMarkerCount { get; private set; }

        struct HintTarget
        {
            public object key;
            public BaobabSurge.SpawnRecord sprout;
            public LambAgent lamb;
            public Vector3 Position => lamb ? lamb.transform.position : sprout.instance.transform.position;
        }

        sealed class Marker
        {
            public object key;
            public RectTransform rect;
            public SproutBubbleGraphic graphic;
            public CanvasGroup group;
            public Vector2 anchor, desired;
            public float desiredScale=1f;
            public bool pickup;
            public bool show, initialized;
        }

        readonly List<Marker> markers = new List<Marker>();
        readonly List<HintTarget> active = new List<HintTarget>();
        float nextRefresh;

        void OnDisable()
        {
            foreach (var marker in markers) if (marker.group) marker.group.alpha = 0f;
            VisibleMarkerCount = 0;
            VisiblePickupMarkerCount = 0;
        }

        void LateUpdate()
        {
            if (!surge || !surge.surface || !surge.surface.ground || !viewCamera || !canvasRect) return;
            active.Clear();
            foreach (var record in surge.Spawned)
                if (record.IsActive && !IsBeingEaten(record)) active.Add(new HintTarget {key=record,sprout=record});
            if(lambHand)
                foreach(var lamb in lambHand.Deployed)
                    if(lamb && lamb.gameObject.activeInHierarchy && lamb.State==LambAgent.LambState.Pickup)
                        active.Add(new HintTarget {key=lamb,lamb=lamb});
            while (markers.Count < active.Count) CreateMarker();
            bool refresh = Time.unscaledTime >= nextRefresh;
            if (refresh) nextRefresh = Time.unscaledTime + refreshSeconds;
            VisibleMarkerCount = 0;
            VisiblePickupMarkerCount = 0;
            for (int i=0;i<markers.Count;i++)
            {
                var marker = markers[i];
                var target = i<active.Count ? active[i] : default;
                bool changed = marker.key != target.key;
                if (changed)
                {
                    marker.key=target.key; marker.initialized=false; marker.group.alpha=0f;
                    marker.pickup=target.lamb;
                    marker.graphic.Icon=marker.pickup?SproutBubbleGraphic.BubbleIcon.Lamb:SproutBubbleGraphic.BubbleIcon.Sprout;
                }
                if (target.key == null) marker.show = false;
                else if (refresh || changed)
                {
                    Vector3 horizon = Vector3.zero;
                    bool hidden=target.lamb?IsPickupBehindPlanet(target.lamb):IsBehindPlanet(target.sprout);
                    marker.show = hidden && TryGetHorizon(target.Position, out horizon);
                    if (marker.show)
                    {
                        marker.desiredScale=ScaleForPosition(target.Position);
                        Vector3 screen = viewCamera.WorldToScreenPoint(horizon);
                        if (screen.z <= viewCamera.nearClipPlane)
                        {
                            // The actual rim is behind the camera: point toward that side of the screen.
                            Vector3 local = viewCamera.transform.InverseTransformDirection(horizon-viewCamera.transform.position);
                            Vector2 direction = new Vector2(local.x,local.y);
                            if (direction.sqrMagnitude < 0.001f) direction=Vector2.down;
                            screen = new Vector3(Screen.width*.5f + direction.normalized.x*Screen.width,
                                Screen.height*.5f + direction.normalized.y*Screen.height,1f);
                        }
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,screen,null,out marker.anchor);
                        marker.desired = PlaceBubble(marker.anchor+Vector2.up*horizonGap*marker.desiredScale,i,marker.desiredScale);
                    }
                }
                marker.group.alpha = Mathf.MoveTowards(marker.group.alpha,marker.show?1f:0f,Time.unscaledDeltaTime*9f);
                if (!marker.show) continue;
                if (!marker.initialized)
                {
                    marker.rect.anchoredPosition=marker.desired; marker.rect.localScale=Vector3.one*marker.desiredScale;
                    marker.initialized=true;
                }
                else marker.rect.anchoredPosition=Vector2.Lerp(marker.rect.anchoredPosition,marker.desired,1f-Mathf.Exp(-16f*Time.unscaledDeltaTime));
                float scale=Mathf.Lerp(marker.rect.localScale.x,marker.desiredScale,1f-Mathf.Exp(-10f*Time.unscaledDeltaTime));
                marker.rect.localScale=Vector3.one*scale;
                Vector2 tail = (marker.anchor-marker.rect.anchoredPosition)/Mathf.Max(.1f,scale);
                // At an off-screen horizon the hint remains at the viewport edge, with a short pointer.
                marker.graphic.TailTip = Vector2.ClampMagnitude(tail,maxTailLength);
                if (marker.group.alpha > 0.5f) { VisibleMarkerCount++; if(marker.pickup) VisiblePickupMarkerCount++; }
            }
        }

        void CreateMarker()
        {
            var go = new GameObject("Hidden Sprout Balloon "+(markers.Count+1),typeof(RectTransform),typeof(CanvasGroup),typeof(SproutBubbleGraphic));
            var rect=go.GetComponent<RectTransform>(); rect.SetParent(transform,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0.5f,0.5f); rect.sizeDelta=new Vector2(74,84);
            var group=go.GetComponent<CanvasGroup>(); group.alpha=0; group.interactable=false; group.blocksRaycasts=false;
            var graphic=go.GetComponent<SproutBubbleGraphic>(); graphic.raycastTarget=false;
            markers.Add(new Marker {rect=rect,graphic=graphic,group=group});
        }

        Vector2 PlaceBubble(Vector2 desired,int index,float scale)
        {
            Rect bounds=canvasRect.rect;
            Vector2 result=Clamp(desired,bounds,scale);
            for (int attempt=0;attempt<16;attempt++)
            {
                bool overlap=false;
                for (int i=0;i<index;i++)
                    if (markers[i].show && Mathf.Abs(result.x-markers[i].desired.x)<39f*(scale+markers[i].desiredScale)
                        && Mathf.Abs(result.y-markers[i].desired.y)<31f*(scale+markers[i].desiredScale))
                    { overlap=true; break; }
                if (!overlap) return result;
                int step=attempt/2+1;
                result=Clamp(desired+new Vector2((attempt%2==0?1:-1)*step*80f*scale,0f),bounds,scale);
            }
            return result;
        }

        static Vector2 Clamp(Vector2 point,Rect rect,float scale)
        {
            return new Vector2(Mathf.Clamp(point.x,rect.xMin+42f*scale,rect.xMax-42f*scale),
                Mathf.Clamp(point.y,rect.yMin+55f*scale,rect.yMax-155f));
        }

        public bool IsBeingEaten(BaobabSurge.SpawnRecord record)
        {
            if(!lambHand || record==null || !record.IsActive) return false;
            foreach(var lamb in lambHand.Deployed)
                if(lamb && lamb.isActiveAndEnabled && lamb.State==LambAgent.LambState.Grazing && lamb.Target==record)
                    return true;
            return false;
        }

        public float ScaleForPosition(Vector3 position)
        {
            float distance=surge.player?LambCardHand.SurfaceDistance(surge.player.position,position,surge.surface.transform.position)
                :Vector3.Distance(viewCamera.transform.position,position);
            return ScaleForDistance(distance);
        }

        public float ScaleForDistance(float distance)
        {
            return Mathf.Lerp(nearScale,farScale,Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(nearDistance,Mathf.Max(nearDistance+.01f,farDistance),distance)));
        }

        public bool IsPickupBehindPlanet(LambAgent lamb)
        {
            if(!lamb || !lamb.gameObject.activeInHierarchy || lamb.State!=LambAgent.LambState.Pickup) return false;
            Vector3 point=lamb.transform.position,up=lamb.transform.up;
            return IsPointOccluded(point+up*.05f) && IsPointOccluded(point+up*.12f) && IsPointOccluded(point+up*.17f);
        }

        public bool IsBehindPlanet(BaobabSurge.SpawnRecord record)
        {
            if (!record.IsActive) return false;
            // A visible leaf/top is enough to suppress the balloon, even if the base is hidden.
            Vector3 p=record.instance.transform.position, up=record.instance.transform.up;
            return IsPointOccluded(p+up*0.18f) && IsPointOccluded(p+up*0.38f) && IsPointOccluded(p+up*0.57f);
        }

        public bool IsPointOccluded(Vector3 point)
        {
            Vector3 delta=point-viewCamera.transform.position;
            float distance=delta.magnitude;
            return distance>0.05f && surge.surface.ground.Raycast(new Ray(viewCamera.transform.position,delta/distance),out var hit,distance)
                && hit.distance < distance-0.035f;
        }

        public bool TryGetHorizon(BaobabSurge.SpawnRecord record,out Vector3 horizon)
        {
            return TryGetHorizon(surge.surface.WorldPoint(record.slot),out horizon);
        }

        public bool TryGetHorizon(Vector3 position,out Vector3 horizon)
        {
            var surface=surge.surface;
            Vector3 centre=surface.transform.position;
            Vector3 start=(surge.player ? surge.player.position-centre : viewCamera.transform.position-centre).normalized;
            Vector3 end=(position-centre).normalized;
            if (!Sample(start,out var first,out var firstNormal) || IsPointOccluded(first+firstNormal*0.06f))
                start=(viewCamera.transform.position-centre).normalized;
            float angle=Mathf.Acos(Mathf.Clamp(Vector3.Dot(start,end),-1f,1f));
            Vector3 tangent=Vector3.ProjectOnPlane(end,start);
            if (tangent.sqrMagnitude<0.00001f) tangent=Vector3.ProjectOnPlane(viewCamera.transform.forward,start);
            if (tangent.sqrMagnitude<0.00001f) tangent=Vector3.Cross(start,Vector3.right);
            tangent.Normalize();
            float previous=0f;
            horizon=Vector3.zero;
            for (int step=1;step<=32;step++)
            {
                float current=angle*step/32f;
                Vector3 direction=start*Mathf.Cos(current)+tangent*Mathf.Sin(current);
                if (!Sample(direction,out var p,out var n)) continue;
                if (IsPointOccluded(p+n*0.06f))
                {
                    float low=previous,high=current;
                    for (int iteration=0;iteration<10;iteration++)
                    {
                        float mid=(low+high)*0.5f;
                        Vector3 d=start*Mathf.Cos(mid)+tangent*Mathf.Sin(mid);
                        if (Sample(d,out var mp,out var mn) && !IsPointOccluded(mp+mn*0.06f)) low=mid;
                        else high=mid;
                    }
                    Vector3 rim=start*Mathf.Cos(low)+tangent*Mathf.Sin(low);
                    if (!Sample(rim,out p,out n)) return false;
                    horizon=p+n*0.025f;
                    return true;
                }
                previous=current;
            }
            return false;
        }

        bool Sample(Vector3 worldDirection,out Vector3 point,out Vector3 normal)
        {
            return surge.surface.TryProject(surge.surface.transform.InverseTransformVector(worldDirection),out point,out normal);
        }
    }
}
