using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace B612
{
    public sealed class StarSystemMapView : MonoBehaviour
    {
        public StarSystemFlowController flow;
        public CanvasGroup mapPanel,transitionPanel,tooltipGroup;
        public RectTransform board,tooltip,canvasRect;
        public TMP_Text tooltipLabel,statusLabel,noticeLabel,mapStatus;
        public TMP_FontAsset font;
        public UnityEngine.UI.Button closeButton;
        public UnityEngine.UI.Button nextChapterButton;
        string builtSystem;
        public string HoveredPlanetId { get; private set; }
        readonly Dictionary<string,PlanetMapNodeGraphic> nodes=new Dictionary<string,PlanetMapNodeGraphic>();
        readonly Dictionary<string,TMP_Text> markers=new Dictionary<string,TMP_Text>();
        readonly List<UnityEngine.UI.Image> edges=new List<UnityEngine.UI.Image>();
        void OnEnable(){if(flow)flow.Changed+=Refresh;if(closeButton)closeButton.onClick.AddListener(Close);if(nextChapterButton)nextChapterButton.onClick.AddListener(NextChapter);Refresh();}
        void OnDisable(){if(flow){flow.Changed-=Refresh;flow.CancelMapAndTravel();}if(closeButton)closeButton.onClick.RemoveListener(Close);if(nextChapterButton)nextChapterButton.onClick.RemoveListener(NextChapter);HideTooltip();}
        void NextChapter(){if(flow)flow.RequestNextChapter();}
        void Close(){if(flow)flow.CloseMap();}
        static void Show(CanvasGroup group,bool visible){if(group){group.alpha=visible?1:0;group.interactable=group.blocksRaycasts=visible;}}
        public void ShowMap(bool visible){Show(mapPanel,visible);HideTooltip();Refresh();}
        public void ShowTransition(bool visible){Show(transitionPanel,visible);HideTooltip();}
        public void Refresh()
        {
            if(!flow||flow.SystemData==null)return;
            if(builtSystem!=flow.SystemData.systemId)
            {
                foreach(Transform child in board){child.gameObject.SetActive(false);Destroy(child.gameObject);}nodes.Clear();markers.Clear();edges.Clear();builtSystem=flow.SystemData.systemId;HideTooltip();
            }
            if(nodes.Count==0)BuildNodes();
            if(statusLabel)statusLabel.text=flow.CanOpenMap||flow.IsMapOpen?"[M] 행성 지도":"바오밥 생성 종료 후 지도 해금";
            if(noticeLabel)noticeLabel.text=flow.Notice??"";
            if(mapStatus)mapStatus.text=flow.Progress.journeyComplete?"3챕터 여정 완료":flow.SystemComplete?(flow.CampaignEnabled?"챕터 완료 · 다음 챕터로 이동하세요":"테스트 항성계 완료")
                :flow.CampaignEnabled?$"챕터 {flow.Progress.chapter} · 밝은 테두리의 행성을 선택하세요":"밝은 테두리의 행성을 클릭해 이동하세요";
            if(nextChapterButton){nextChapterButton.gameObject.SetActive(flow.CampaignEnabled&&flow.SystemComplete&&flow.Progress.chapter<3);nextChapterButton.interactable=flow.CanAdvanceChapter;}
            foreach(var planet in flow.SystemData.planets)
            {
                string id=planet.spec.planetId;bool current=id==flow.Progress.currentPlanetId,available=flow.CanTravel(id);
                bool visited=flow.Progress.chosenPath.Exists(v=>v.planetId==id);
                var node=nodes[id];node.ringColor=current?new Color(.61f,.96f,.78f):available?new Color(1f,.84f,.48f):visited?new Color(.53f,.69f,.63f):new Color(.3f,.35f,.43f);
                ColorUtility.TryParseHtmlString(planet.spec.terrain.groundColor,out var color);color.a=current||available||visited?1f:.42f;node.color=color;node.SetVerticesDirty();
                markers[id].text=current?"지금 여기":available?"선택 가능":visited?"지나온 행성":"";
            }
            for(int i=0;i<edges.Count;i++)
            {
                var link=flow.SystemData.links[i];bool travelled=false;
                for(int j=1;j<flow.Progress.chosenPath.Count;j++)if(flow.Progress.chosenPath[j-1].planetId==link.fromPlanetId&&flow.Progress.chosenPath[j].planetId==link.toPlanetId)travelled=true;
                edges[i].color=travelled?new Color(.58f,.88f,.71f):link.fromPlanetId==flow.Progress.currentPlanetId?new Color(.73f,.66f,.45f):new Color(.2f,.25f,.32f);
            }
        }
        Vector2 Point(PlanetData p)=>new Vector2((p.map.position01.x-.5f)*board.rect.width,(p.map.position01.y-.5f)*board.rect.height);
        void BuildNodes()
        {
            foreach(var link in flow.SystemData.links)
            {
                Vector2 a=Point(flow.SystemData.Find(link.fromPlanetId)),b=Point(flow.SystemData.Find(link.toPlanetId)),delta=b-a;
                var rect=Rect("Route",board,new Vector2(delta.magnitude,3));rect.anchoredPosition=(a+b)*.5f;rect.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
                var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.raycastTarget=false;edges.Add(image);
                var arrow=Rect("Direction",rect,new Vector2(14,14));arrow.anchoredPosition=new Vector2(delta.magnitude*.17f,0);arrow.localRotation=Quaternion.Euler(0,0,-45);
                var tip=arrow.gameObject.AddComponent<UnityEngine.UI.Image>();tip.color=new Color(.36f,.43f,.5f);tip.raycastTarget=false;
            }
            foreach(var planet in flow.SystemData.planets)
            {
                string id=planet.spec.planetId;var rect=Rect(id,board,new Vector2(100,100));rect.anchoredPosition=Point(planet);
                var graphic=rect.gameObject.AddComponent<PlanetMapNodeGraphic>();graphic.iconScale=planet.spec.sizeClass==PlanetSizeClass.Small?.72f:planet.spec.sizeClass==PlanetSizeClass.Medium?.88f:1.06f;
                var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=graphic;button.transition=UnityEngine.UI.Selectable.Transition.None;
                button.onClick.AddListener(()=>flow.RequestTravel(id));
                var hover=rect.gameObject.AddComponent<PlanetMapNodeHover>();hover.view=this;hover.planetId=id;
                string name=planet.spec.role==PlanetRole.Start?"시작":planet.spec.role==PlanetRole.Destination?"도착":id.Substring(id.LastIndexOf('_')+1);
                Label("Name",rect,name,new Vector2(0,-64),new Vector2(160,30),20,new Color(.91f,.94f,.95f));
                markers[id]=Label("Marker",rect,"",new Vector2(0,66),new Vector2(180,30),17,new Color(.72f,.94f,.8f));nodes[id]=graphic;
            }
        }
        public void Hover(string id,Vector2 pointer)
        {
            if(!flow.IsMapOpen)return;var planet=flow.SystemData.Find(id);if(planet==null)return;
            HoveredPlanetId=id;tooltipLabel.text="행성 크기: "+PlanetMapPreview.From(planet).sizeLabel+(flow.CampaignEnabled?$"\n챕터 {planet.spec.chapter}"+(!flow.HasCapabilities(planet.spec.requiredCapabilities)?" · 능력 해금 필요":""):"");
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,pointer,null,out var point);
            point+=new Vector2(18,-18);Rect bounds=canvasRect.rect;
            point.x=Mathf.Clamp(point.x,bounds.xMin+8,bounds.xMax-tooltip.rect.width-8);
            point.y=Mathf.Clamp(point.y,bounds.yMin+tooltip.rect.height+8,bounds.yMax-8);
            tooltip.anchoredPosition=point;tooltipGroup.alpha=1;tooltipGroup.interactable=tooltipGroup.blocksRaycasts=false;
        }
        public void HideTooltip(){HoveredPlanetId=null;if(tooltipGroup)tooltipGroup.alpha=0;}
        public RectTransform NodeRect(string id)=>nodes.TryGetValue(id,out var node)?node.rectTransform:null;
        static RectTransform Rect(string name,Transform parent,Vector2 size)
        {var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=size;return rect;}
        TMP_Text Label(string name,Transform parent,string content,Vector2 pos,Vector2 size,float pointSize,Color color)
        {
            var rect=Rect(name,parent,size);rect.anchoredPosition=pos;var text=rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font=font;text.text=content;text.fontSize=pointSize;text.color=color;text.raycastTarget=false;text.alignment=TextAlignmentOptions.Center;text.textWrappingMode=TextWrappingModes.NoWrap;return text;
        }
    }
}
