using UnityEditor;
using UnityEngine.UIElements;

namespace B612.EditorTools
{
    [CustomEditor(typeof(ProceduralPlanetTerrain))]
    public sealed class ProceduralPlanetTerrainEditor:UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root=new VisualElement();var text=new Label();text.style.whiteSpace=WhiteSpace.Normal;root.Add(text);
            void Refresh()
            {
                if(!(target is ProceduralPlanetTerrain terrain)||!terrain)return;
                var plan=terrain.GenerationPlan;
                text.text=plan==null?"Play에서 행성을 준비한 뒤 생성 결과를 표시합니다.":
                    $"지역 지형 생성 · 읽기 전용\nSeed: {plan.seed} / 버전: {plan.version} / 챕터: {plan.chapter}\n반경: {plan.radius:0.##}m / 높이 진폭: {plan.amplitude:0.##}m\n" +
                    $"큰 지형: {plan.appearance.macroFrequency:0.##} / 중간층: {plan.appearance.hillScaleMeters:0.##}m / 코스 전이: {plan.appearance.transitionMeters:0.##}m\n" +
                    $"지역: {plan.patches.Count} / 타일: {plan.regions.Count} / 물길: {plan.river!=null}\n요소: {plan.features.Count} / 요청: {plan.requestedFeatures}\n배치 시도: {plan.placementAttempts} / 메시 준비: {plan.buildAttempts} / 최대 5\n보행 최대 경사: {plan.maximumWalkSlope:0.##}°\n메시+이동 검증: {plan.validationMilliseconds:0.0}ms\n안전 평지 대체: {plan.safeFallback}\n"+string.Join("\n",plan.repairs);
            }
            Refresh();root.schedule.Execute(Refresh).Every(500);return root;
        }
    }
}
