using UnityEngine;

namespace B612
{
    public sealed class LambAgent : MonoBehaviour
    {
        public enum LambState { Grazing, Pickup, Collected }
        public Transform visual;
        public LambState State { get; private set; }
        public float Age { get; private set; }
        public float EatingProgress { get; private set; }
        public int SproutsEaten { get; private set; }
        public int CardIndex { get; private set; }
        public BaobabSurge.SpawnRecord Target { get; private set; }
        public LambCardHand Owner { get; private set; }
        public float RemainingSeconds => Owner?Mathf.Max(0f,Owner.activeSeconds-Age):0f;

        Vector3 baseScale, baseLocalPosition, baseLocalUp;
        Quaternion baseLocalRotation;
        float pickupAge;
        LineRenderer rangeRing;
        bool initialized;

        public void Initialize(LambCardHand owner,int cardIndex,BaobabSurge.SpawnRecord preferred)
        {
            Owner=owner; CardIndex=cardIndex; State=LambState.Grazing;
            if(!visual) visual=transform.GetChild(0);
            baseScale=visual.localScale;
            baseLocalPosition=transform.localPosition; baseLocalRotation=transform.localRotation;
            baseLocalUp=transform.parent.InverseTransformDirection(transform.up);
            Target=owner.ClaimTarget(this,preferred);
            rangeRing=owner.CreateRing("Lamb "+(cardIndex+1)+" grazing range",0.012f,new Color(0.65f,0.88f,0.69f,0.3f));
            owner.DrawRing(rangeRing,transform.position,transform.up,owner.eatingRadius);
            rangeRing.enabled=true; initialized=true;
        }

        void Update()
        {
            if(!initialized || !Owner || Owner.surge.IsGameOver) return;
            Advance(Time.deltaTime);
            if(State==LambState.Grazing)
            {
                float breath=Mathf.Sin(Time.time*3f+CardIndex)*0.012f;
                bool eating=Target!=null && Target.IsActive;
                visual.localScale=Vector3.Scale(baseScale,new Vector3(1f-breath*0.4f,1f+breath,1f-breath*0.4f));
                visual.localRotation=Quaternion.Euler(eating?Mathf.Sin(Time.time*5f)*2.5f:0f,0f,0f);
                if(eating)
                {
                    Vector3 facing=Vector3.ProjectOnPlane(Owner.surge.surface.WorldPoint(Target.slot)-transform.position,transform.up);
                    if(facing.sqrMagnitude>0.001f) transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(facing.normalized,transform.up),1f-Mathf.Exp(-4f*Time.deltaTime));
                }
            }
            else if(State==LambState.Pickup)
            {
                float t=Mathf.Clamp01(pickupAge/0.22f);
                visual.localScale=baseScale*Mathf.Lerp(1f,0.27f,t*t*(3f-2f*t));
                visual.localRotation=Quaternion.identity;
                float lift=(0.2f+Mathf.Sin(pickupAge*3.5f)*0.035f)*Mathf.Clamp01(pickupAge/0.18f);
                transform.localPosition=baseLocalPosition+baseLocalUp*lift;
                transform.localRotation=Quaternion.AngleAxis(pickupAge*100f,baseLocalUp)*baseLocalRotation;
            }
        }

        public void Advance(float seconds)
        {
            if(!initialized || !Owner || Owner.surge.IsGameOver || State==LambState.Collected || seconds<0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            if(State==LambState.Pickup) { AdvancePickup(seconds); return; }
            float remaining=seconds;
            for(int guard=0;guard<64 && remaining>0.000001f && State==LambState.Grazing;guard++)
            {
                if(Target!=null && (!Target.IsActive || !InRange(Target)))
                { Owner.ReleaseTarget(this,Target); Target=null; EatingProgress=0f; }
                if(Target==null) Target=Owner.ClaimTarget(this);
                float slice=Mathf.Min(remaining,Mathf.Max(0f,Owner.activeSeconds-Age));
                if(Target!=null) slice=Mathf.Min(slice,Mathf.Max(0f,Owner.secondsPerSprout-EatingProgress));
                Age+=slice; remaining-=slice;
                if(Target!=null)
                {
                    EatingProgress+=slice;
                    if(EatingProgress>=Owner.secondsPerSprout-0.00001f)
                    {
                        var eaten=Target; Target=null; EatingProgress=0f;
                        Owner.ReleaseTarget(this,eaten);
                        if(eaten.IsActive && Owner.surge.RemoveSprout(eaten.instance)) SproutsEaten++;
                    }
                }
                // A second bite at exactly 20s completes before the lamb becomes an item.
                if(Age>=Owner.activeSeconds-0.00001f) BecomePickup();
                if(slice<=0.000001f && State==LambState.Grazing) break;
            }
            if(State==LambState.Pickup && remaining>0f) AdvancePickup(remaining);
        }

        bool InRange(BaobabSurge.SpawnRecord target)
        {
            return LambCardHand.SurfaceDistance(transform.position,Owner.surge.surface.WorldPoint(target.slot),Owner.surge.surface.transform.position)
                <=Owner.eatingRadius+Owner.rangeGrace;
        }

        void BecomePickup()
        {
            Owner.ReleaseTarget(this,Target); Target=null; EatingProgress=0f;
            State=LambState.Pickup; pickupAge=0f;
            baseLocalPosition=transform.localPosition; baseLocalRotation=transform.localRotation;
            if(rangeRing) rangeRing.enabled=false;
        }

        void AdvancePickup(float seconds)
        {
            pickupAge+=seconds;
            // Leave time for the shrink/spin cue even when the player is already standing beside it.
            if(pickupAge<0.8f || !Owner.player) return;
            Vector3 baseWorld=transform.parent.TransformPoint(baseLocalPosition);
            if(Vector3.Distance(Owner.player.transform.position,baseWorld)>Owner.pickupRadius) return;
            if(Owner.RecoverCard(CardIndex,this))
            {
                State=LambState.Collected; gameObject.SetActive(false); Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            if(rangeRing) Destroy(rangeRing.gameObject);
            if(Owner) { Owner.ReleaseTarget(this,Target); if(State!=LambState.Collected) Owner.RecoverCard(CardIndex,this); }
        }
    }
}
