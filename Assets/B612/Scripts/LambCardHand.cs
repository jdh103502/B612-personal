using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace B612
{
    public sealed class LambCardHand : MonoBehaviour
    {
        public BaobabSurge surge;
        public GravityWalker player;
        public GameObject lambPrefab;
        public Material ringMaterial;
        [Range(1,9)] public int startingCards = 4;
        [Min(1f)] public float activeSeconds = 20f;
        [Min(0.1f)] public float secondsPerSprout = 7f;
        [Min(0.1f)] public float secondsPerSapling = 10f;
        [Min(0.1f)] public float secondsPerTree = 13f;
        [Min(0.1f)] public float eatingRadius = 1.7f;
        [Min(0f)] public float rangeGrace = 0.25f;
        [Min(0f)] public float intentRadius = 2.05f;
        [Min(0.1f)] public float pickupRadius = 0.9f;
        public int SelectedIndex { get; private set; }
        public int CardCount => cards == null ? startingCards : cards.Length;
        public int InHandCount { get { int n=0; if(cards!=null) foreach(var c in cards) if(!c) n++; return n; } }
        public IReadOnlyList<LambAgent> Deployed => deployed;
        public string Message { get; private set; }
        public event Action Changed;

        public float EatingSeconds(BaobabStage stage) => Mathf.Max(.1f,
            stage == BaobabStage.Tree ? secondsPerTree : stage == BaobabStage.Sapling ? secondsPerSapling : secondsPerSprout);

        LambAgent[] cards;
        readonly List<LambAgent> deployed = new List<LambAgent>();
        readonly Dictionary<BaobabSurge.SpawnRecord,LambAgent> claims = new Dictionary<BaobabSurge.SpawnRecord,LambAgent>();
        LineRenderer previewRing, targetRing;
        float nextPreview, messageUntil;
        static readonly Key[] numberKeys = { Key.Digit1,Key.Digit2,Key.Digit3,Key.Digit4,Key.Digit5,Key.Digit6,Key.Digit7,Key.Digit8,Key.Digit9 };

        void Awake()
        {
            cards = new LambAgent[Mathf.Clamp(startingCards,1,9)];
            SelectedIndex=0;
        }

        void Start()
        {
            if (!surge || !surge.surface || !player || !lambPrefab) { Debug.LogError("Assign the lamb hand's surge, player and prefab.",this); enabled=false; return; }
            previewRing=CreateRing("Lamb placement range",0.018f,new Color(0.56f,0.95f,0.79f,0.65f));
            targetRing=CreateRing("Intended sprout",0.025f,new Color(1f,0.86f,0.39f,0.95f));
            Changed?.Invoke();
        }

        void Update()
        {
            if (!surge || !player) return;
            if (surge.IsGameOver)
            {
                if (previewRing) previewRing.enabled=false;
                if (targetRing) targetRing.enabled=false;
                return;
            }
            if (Time.timeScale>0f && !player.Suspended)
            {
                var kb=Keyboard.current;
                if(kb!=null)
                {
                    for(int i=0;i<cards.Length;i++) if(kb[numberKeys[i]].wasPressedThisFrame) SelectCard(i);
                }
                var mouse=Mouse.current;
                bool zoom=kb!=null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
                if(mouse!=null && !zoom && Mathf.Abs(mouse.scroll.ReadValue().y)>0.01f)
                    SelectNext(mouse.scroll.ReadValue().y>0f?-1:1);
                if(kb!=null && kb.qKey.wasPressedThisFrame) TrySummonSelected();
            }
            if(Message!=null && Time.unscaledTime>messageUntil) { Message=null; Changed?.Invoke(); }
            if(Time.unscaledTime>=nextPreview)
            {
                nextPreview=Time.unscaledTime+0.08f;
                Vector3 point=Vector3.zero,normal=Vector3.up;
                BaobabSurge.SpawnRecord target=null;
                bool show=IsInHand(SelectedIndex) && player.Grounded && TryGetPlacement(out point,out normal,out target);
                if(previewRing) previewRing.enabled=show;
                if(targetRing) targetRing.enabled=show && target!=null;
                if(show)
                {
                    DrawRing(previewRing,point,normal,eatingRadius);
                    if(target!=null) DrawRing(targetRing,surge.surface.WorldPoint(target.slot),surge.surface.WorldNormal(target.slot),0.28f);
                }
            }
        }

        public bool IsInHand(int index) => cards!=null && index>=0 && index<cards.Length && !cards[index];
        public LambAgent LambForCard(int index) => cards!=null && index>=0 && index<cards.Length ? cards[index] : null;

        public bool SelectCard(int index)
        {
            if(!IsInHand(index)) { Notify("That lamb is already on the planet."); return false; }
            SelectedIndex=index; Changed?.Invoke(); return true;
        }

        public void SelectNext(int step)
        {
            if(cards==null || InHandCount==0) return;
            int start=SelectedIndex<0?0:SelectedIndex;
            for(int i=1;i<=cards.Length;i++)
            {
                int index=(start+(step<0?-i:i)+cards.Length*2)%cards.Length;
                if(IsInHand(index)) { SelectCard(index); return; }
            }
        }

        public bool TrySummonSelected()
        {
            if(!surge || surge.IsGameOver) return false;
            if(!IsInHand(SelectedIndex)) { Notify("Collect a floating lamb to recover its card."); return false; }
            if(!player.Grounded || player.Suspended) { Notify("Land on the planet to summon a lamb."); return false; }
            if(!TryGetPlacement(out var point,out var normal,out var intended)) { Notify("Move a little to find room for the lamb."); return false; }
            Vector3 facing=Vector3.ProjectOnPlane(intended!=null?surge.surface.WorldPoint(intended.slot)-point:player.transform.forward,normal);
            if(facing.sqrMagnitude<0.001f) facing=Vector3.ProjectOnPlane(player.transform.right,normal);
            int index=SelectedIndex;
            var go=Instantiate(lambPrefab,point+normal*0.01f,Quaternion.LookRotation(facing.normalized,normal),surge.surface.transform);
            var agent=go.GetComponent<LambAgent>();
            if(!agent) { Destroy(go); Notify("The lamb prefab is missing its agent."); return false; }
            cards[index]=agent; deployed.Add(agent);
            agent.Initialize(this,index,intended);
            SelectedIndex=-1;
            for(int i=1;i<=cards.Length;i++) { int next=(index+i)%cards.Length; if(IsInHand(next)) { SelectedIndex=next; break; } }
            Notify("Lamb summoned."); Changed?.Invoke();
            return true;
        }

        public bool TryGetPlacement(out Vector3 point,out Vector3 normal,out BaobabSurge.SpawnRecord intended)
        {
            point=normal=Vector3.zero; intended=null;
            if(!surge || surge.IsGameOver || !surge.surface || !player) return false;
            var surface=surge.surface;
            Vector3 centre=surface.transform.position;
            Vector3 playerDirection=(player.transform.position-centre).normalized;
            Vector3 forward=Vector3.ProjectOnPlane(player.transform.forward,playerDirection).normalized;
            float best=float.PositiveInfinity;
            foreach(var record in surge.Spawned)
            {
                if(!record.IsActive || IsClaimed(record)) continue;
                Vector3 p=surface.WorldPoint(record.slot);
                float distance=SurfaceDistance(player.transform.position,p,centre);
                if(distance>intentRadius) continue;
                Vector3 to=Vector3.ProjectOnPlane(p-player.transform.position,playerDirection).normalized;
                float facing=Vector3.Dot(forward,to);
                if(facing < -0.35f && distance>1.1f) continue;
                float score=distance-facing*0.35f;
                if(score<best) { best=score; intended=record; }
            }
            Vector3 direction;
            if(intended!=null)
            {
                Vector3 targetDirection=(surface.WorldPoint(intended.slot)-centre).normalized;
                float distance=SurfaceDistance(player.transform.position,surface.WorldPoint(intended.slot),centre);
                direction=distance>0.75f?Vector3.Slerp(playerDirection,targetDirection,Mathf.Clamp01((distance-0.55f)/distance))
                    : (player.transform.position-centre+forward*0.8f).normalized;
            }
            else direction=(player.transform.position-centre+forward*0.85f).normalized;
            for(int attempt=0;attempt<9;attempt++)
            {
                Vector3 candidate=direction;
                if(attempt>0)
                {
                    float angle=(attempt%2==1?1f:-1f)*((attempt+1)/2)*35f;
                    candidate=(player.transform.position-centre+Quaternion.AngleAxis(angle,playerDirection)*forward*0.9f).normalized;
                }
                if(!surface.TryProject(surface.transform.InverseTransformVector(candidate),out var p,out var n)) continue;
                if(Vector3.Angle(n,(p-centre).normalized)>55f || Vector3.Distance(p,player.transform.position)<0.42f) continue;
                bool room=true;
                foreach(var lamb in deployed)
                    if(lamb && lamb.State==LambAgent.LambState.Grazing && SurfaceDistance(p,lamb.transform.position,centre)<0.75f) { room=false; break; }
                if(!room) continue;
                if(intended!=null && SurfaceDistance(p,surface.WorldPoint(intended.slot),centre)>eatingRadius+rangeGrace) continue;
                point=p; normal=n; return true;
            }
            return false;
        }

        public BaobabSurge.SpawnRecord ClaimTarget(LambAgent agent,BaobabSurge.SpawnRecord preferred=null)
        {
            if(preferred!=null && CanClaim(agent,preferred)) { Reserve(agent,preferred); return preferred; }
            BaobabSurge.SpawnRecord best=null; float nearest=float.PositiveInfinity;
            foreach(var record in surge.Spawned)
            {
                if(!CanClaim(agent,record)) continue;
                float distance=SurfaceDistance(agent.transform.position,surge.surface.WorldPoint(record.slot),surge.surface.transform.position);
                if(distance<nearest) { nearest=distance; best=record; }
            }
            if(best!=null) Reserve(agent,best);
            return best;
        }

        bool CanClaim(LambAgent agent,BaobabSurge.SpawnRecord record)
        {
            return record.IsActive && (!claims.TryGetValue(record,out var other) || !other || !other.isActiveAndEnabled || other==agent || other.State!=LambAgent.LambState.Grazing)
                && SurfaceDistance(agent.transform.position,surge.surface.WorldPoint(record.slot),surge.surface.transform.position)<=eatingRadius+rangeGrace;
        }

        bool IsClaimed(BaobabSurge.SpawnRecord record) => claims.TryGetValue(record,out var lamb) && lamb && lamb.isActiveAndEnabled && lamb.State==LambAgent.LambState.Grazing;

        void Reserve(LambAgent agent,BaobabSurge.SpawnRecord record)
        {
            surge.RefreshStage(record);
            claims[record]=agent; record.FeedingLamb=agent;
        }

        public bool OwnsTarget(LambAgent agent,BaobabSurge.SpawnRecord record)
            => record!=null && claims.TryGetValue(record,out var owner) && owner==agent;

        public void ReleaseTarget(LambAgent agent,BaobabSurge.SpawnRecord record)
        {
            if(record!=null && claims.TryGetValue(record,out var other) && other==agent)
            {
                claims.Remove(record);
                if(record.FeedingLamb==agent) record.FeedingLamb=null;
            }
        }

        public bool RecoverCard(int index,LambAgent agent)
        {
            if(cards==null || index<0 || index>=cards.Length || cards[index]!=agent) return false;
            cards[index]=null; deployed.Remove(agent);
            if(SelectedIndex<0) SelectedIndex=index;
            Notify("Lamb card recovered."); Changed?.Invoke(); return true;
        }

        public void Notify(string message) { Message=message; messageUntil=Time.unscaledTime+2f; Changed?.Invoke(); }

        public static float SurfaceDistance(Vector3 a,Vector3 b,Vector3 centre)
        {
            Vector3 da=a-centre, db=b-centre;
            float angle=Mathf.Atan2(Vector3.Cross(da.normalized,db.normalized).magnitude,Vector3.Dot(da.normalized,db.normalized));
            return angle*(da.magnitude+db.magnitude)*0.5f;
        }

        public LineRenderer CreateRing(string name,float width,Color color)
        {
            var go=new GameObject(name); go.transform.SetParent(surge.surface.transform,false);
            var line=go.AddComponent<LineRenderer>(); line.sharedMaterial=ringMaterial; line.useWorldSpace=false;
            line.loop=true; line.positionCount=48; line.widthMultiplier=width;
            line.startColor=line.endColor=color; line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows=false; line.enabled=false; return line;
        }

        public void DrawRing(LineRenderer line,Vector3 point,Vector3 normal,float radius)
        {
            if(!line) return;
            Vector3 axis=Vector3.Cross(normal,Mathf.Abs(normal.y)<0.9f?Vector3.up:Vector3.right).normalized;
            Vector3 tangent=Vector3.Cross(normal,axis);
            var surface=surge.surface;
            for(int i=0;i<line.positionCount;i++)
            {
                float angle=i*Mathf.PI*2f/line.positionCount;
                Vector3 direction=(point-surface.transform.position+radius*(axis*Mathf.Cos(angle)+tangent*Mathf.Sin(angle))).normalized;
                if(surface.TryProject(surface.transform.InverseTransformVector(direction),out var p,out var n))
                    line.SetPosition(i,line.transform.InverseTransformPoint(p+n*0.035f));
                else line.SetPosition(i,line.transform.InverseTransformPoint(point));
            }
        }

        void OnDisable() { if(previewRing) previewRing.enabled=false; if(targetRing) targetRing.enabled=false; }
        void OnDestroy() { if(previewRing) Destroy(previewRing.gameObject); if(targetRing) Destroy(targetRing.gameObject); }
    }
}
