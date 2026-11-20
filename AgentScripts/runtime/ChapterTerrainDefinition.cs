using System;
using UnityEngine;

namespace B612
{
    [Flags] public enum TraversalCapability { None=0,Dash=1,AirJump=2,Glide=4,All=7 }
    [CreateAssetMenu(menuName="B612/Chapter Terrain Definition")]
    public sealed class ChapterTerrainDefinition:ScriptableObject
    {
        [Range(1,3)] public int chapter=1;
        public string title="첫 걸음";
        public TraversalCapability guaranteedEntry,unlock=TraversalCapability.Dash;
        public Vector3 regionWeights=new Vector3(65,25,10);
        [Range(0,2)] public int featureBudget=2;
        public static Vector3 DefaultWeights(int chapter)=>chapter==1?new Vector3(65,25,10):chapter==2?new Vector3(50,30,20):new Vector3(45,30,25);
        public static TraversalCapability Entry(int chapter)=>chapter<=1?TraversalCapability.None:chapter==2?TraversalCapability.Dash:TraversalCapability.Dash|TraversalCapability.AirJump;
        public static TraversalCapability Reward(int chapter)=>chapter==1?TraversalCapability.Dash:chapter==2?TraversalCapability.AirJump:TraversalCapability.Glide;
        public static string AbilityName(TraversalCapability ability)=>ability==TraversalCapability.Dash?"대쉬":ability==TraversalCapability.AirJump?"추가 점프":ability==TraversalCapability.Glide?"활공":"이동 능력";
    }
}
