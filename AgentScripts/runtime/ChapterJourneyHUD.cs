using TMPro;
using UnityEngine;

namespace B612
{
    public sealed class ChapterJourneyHUD:MonoBehaviour
    {
        public StarSystemFlowController flow;
        public TMP_Text status,prompt;
        float next;
        public const string Glyphs="챕터여정해금없음대쉬추가 점프활공다음 목표금색 기둥기본 이동으로 도달하고 E로 능력을 배우세요작물 조사와 길 안내 지점에서 이동 기록을 남길 수 있습니다지도에서 다음 챕터를 선택하세요여정 완료얕은 물에서는 보행 속도가 줄어듭니다잠긴 능력은 디버그 체크로 해금되지 않습니다";
        public static string Abilities(TraversalCapability mask)
        {
            string result="";foreach(var capability in new[]{TraversalCapability.Dash,TraversalCapability.AirJump,TraversalCapability.Glide})if((mask&capability)!=0)result+=(result.Length>0?" / ":"")+ChapterTerrainDefinition.AbilityName(capability);return result.Length>0?result:"없음";
        }
        void Update()
        {
            if(Time.unscaledTime<next||!flow||!flow.WorldReady)return;next=Time.unscaledTime+.1f;
            bool visible=!GameplayModalInput.IsOpen&&!flow.IsTravelling;
            if(status)status.transform.parent.gameObject.SetActive(visible);
            if(prompt)prompt.transform.parent.gameObject.SetActive(visible);
            if(status)status.text=$"챕터 {flow.Progress.chapter} / 3\n해금: {Abilities(flow.Progress.unlocked)}"+(flow.Progress.journeyComplete?"\n여정 완료":"");
            if(prompt)prompt.text=flow.Progress.journeyComplete?"여정 완료":flow.Sites?flow.Sites.Prompt():"";
        }
    }
}
