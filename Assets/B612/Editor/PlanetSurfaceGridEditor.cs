using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace B612.EditorTools
{
    [CustomEditor(typeof(PlanetSurfaceGrid))]
    public sealed class PlanetSurfaceGridEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            var diagnostic = new Label { name = "resolutionDiagnostics" }; diagnostic.style.whiteSpace = WhiteSpace.Normal;
            root.Add(diagnostic);
            void Refresh()
            {
                if (!(target is PlanetSurfaceGrid grid) || !grid) return;
                diagnostic.text = $"준비된 격자 (읽기 전용)\nFrequency: {grid.PreparedFrequency}\n구역: {grid.Cells.Count} / 정점: {grid.PreparedVertexCount} / 변: {grid.PreparedEdgeCount}\n후보: {grid.SlotCount} / 빈 구역: {grid.EmptyCellCount}\n설정 변경은 다음 Prepare에만 적용됩니다.";
            }
            Refresh(); root.schedule.Execute(Refresh).Every(250);
            return root;
        }
    }
}
