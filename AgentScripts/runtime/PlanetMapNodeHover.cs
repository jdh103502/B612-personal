using UnityEngine;
using UnityEngine.EventSystems;

namespace B612
{
    public sealed class PlanetMapNodeHover : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerMoveHandler
    {
        public StarSystemMapView view;
        public string planetId;
        public void OnPointerEnter(PointerEventData data){view.Hover(planetId,data.position);}
        public void OnPointerMove(PointerEventData data){view.Hover(planetId,data.position);}
        public void OnPointerExit(PointerEventData data){view.HideTooltip();}
    }
}
