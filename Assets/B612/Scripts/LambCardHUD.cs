using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace B612
{
    /// <summary>Four real inventory cards fanned along the bottom; deployed cards leave the hand.</summary>
    public sealed class LambCardHUD : MonoBehaviour
    {
        public LambCardHand hand;
        public Sprite frameSprite, illustrationSprite;
        public TMP_FontAsset font;
        public RectTransform handRoot;
        public TMP_Text instructionLabel;
        public TMP_Text messageLabel;
        public float cardWidth=160f,cardHeight=214f,cardSpacing=100f;

        sealed class CardView
        {
            public RectTransform rect;
            public CanvasGroup group;
            public UnityEngine.UI.Outline outline;
            public int index;
        }
        readonly List<CardView> views=new List<CardView>();
        string controlsText;

        void Start()
        {
            if(!hand || !handRoot || !frameSprite || !illustrationSprite || !font) { enabled=false; return; }
            for(int i=0;i<hand.CardCount;i++) CreateCard(i);
            controlsText="WHEEL / 1-"+hand.CardCount+"  SELECT     Q  SUMMON     WALK TO COLLECT";
        }

        void LateUpdate()
        {
            if(!hand) return;
            int count=hand.InHandCount,order=0;
            float dt=1f-Mathf.Exp(-14f*Time.unscaledDeltaTime);
            CardView selected=null;
            foreach(var card in views)
            {
                bool available=hand.IsInHand(card.index),chosen=available&&card.index==hand.SelectedIndex;
                card.group.alpha=Mathf.MoveTowards(card.group.alpha,available?1f:0f,Time.unscaledDeltaTime*8f);
                float offset=order-(count-1)*0.5f;
                Vector2 position=available?new Vector2(offset*cardSpacing,-34f-Mathf.Abs(offset)*6f+(chosen?42f:0f))
                    :new Vector2(card.rect.anchoredPosition.x,-cardHeight-80f);
                card.rect.anchoredPosition=Vector2.Lerp(card.rect.anchoredPosition,position,dt);
                float angle=chosen?0f:-offset*9f;
                card.rect.localRotation=Quaternion.Slerp(card.rect.localRotation,Quaternion.Euler(0,0,angle),dt);
                card.rect.localScale=Vector3.Lerp(card.rect.localScale,Vector3.one*(chosen?1.08f:1f),dt);
                card.outline.enabled=chosen;
                if(available) order++;
                if(chosen) selected=card;
            }
            if(selected!=null) selected.rect.SetAsLastSibling();
            if(instructionLabel)
                instructionLabel.text=count>0?controlsText
                    :"LAMBS ARE ON THE PLANET  -  COLLECT THE SMALL FLOATING LAMBS";
            if(messageLabel)
            {
                string message=hand.Message;
                if(message==null && count>0)
                {
                    if(!hand.player.Grounded) message="LAND TO SUMMON";
                    else if(hand.TryGetPlacement(out _,out _,out var target)) message=target!=null?"A NEARBY SPROUT IS IN RANGE":"NO SPROUT NEARBY - MOVE CLOSER";
                    else message="MOVE A LITTLE TO MAKE ROOM";
                }
                messageLabel.text=message??"";
            }
        }

        void CreateCard(int index)
        {
            var rect=Rect("Lamb Card "+(index+1),handRoot,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,-cardHeight),new Vector2(cardWidth,cardHeight));
            var group=rect.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts=false; group.interactable=false;
            var art=Rect("Illustration",rect,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(cardWidth*.91f,cardHeight*.94f));
            var image=art.gameObject.AddComponent<UnityEngine.UI.Image>(); image.sprite=illustrationSprite; image.raycastTarget=false;
            var frame=Rect("Frame",rect,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(cardWidth,cardHeight));
            var frameImage=frame.gameObject.AddComponent<UnityEngine.UI.Image>(); frameImage.sprite=frameSprite; frameImage.raycastTarget=false;
            var outline=frame.gameObject.AddComponent<UnityEngine.UI.Outline>(); outline.effectColor=new Color(1f,.87f,.43f,.9f); outline.effectDistance=new Vector2(2f,-2f);
            Label("Card Name",rect,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-12),new Vector2(cardWidth*.78f,24),12f,(index+1)+"  CLOUD LAMB",new Color(.24f,.14f,.075f));
            Label("Duration",rect,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,34),new Vector2(cardWidth*.72f,17),12f,$"{hand.activeSeconds:0.#}s  ON THE PLANET",new Color(.28f,.19f,.11f));
            Label("Eating Speed",rect,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,20),new Vector2(cardWidth*.72f,17),11f,$"1 SPROUT / {hand.secondsPerSprout:0.#}s",new Color(.3f,.22f,.13f));
            views.Add(new CardView {rect=rect,group=group,outline=outline,index=index});
        }

        TMP_Text Label(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size,float fontSize,string content,Color color)
        {
            var rect=Rect(name,parent,anchor,pivot,position,size);
            var label=rect.gameObject.AddComponent<TextMeshProUGUI>(); label.font=font; label.fontSize=fontSize;
            label.alignment=TextAlignmentOptions.Center; label.textWrappingMode=TextWrappingModes.NoWrap; label.color=color; label.raycastTarget=false; label.text=content;
            return label;
        }

        static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent,false); rect.anchorMin=rect.anchorMax=anchor; rect.pivot=pivot;
            rect.sizeDelta=size; rect.anchoredPosition=position; return rect;
        }
    }
}
