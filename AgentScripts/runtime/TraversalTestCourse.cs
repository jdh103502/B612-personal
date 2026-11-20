using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace B612
{
    /// <summary>Only in the fixed calibration scene. Selecting a station is an explicit practice reset.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class TraversalTestCourse : MonoBehaviour
    {
        public PlayerController player;
        public GravitySource gravity;
        public MeshCollider ground;
        public Transform practiceSpawn;
        public TMP_Text titleLabel,detailLabel;
        public int Station { get; private set; }
        public bool Timing { get; private set; }
        public float LastSeconds { get; private set; }
        public string LastProfile { get; private set; }
        float started,nextRefresh;
        bool armed,needsReset;
        GravityWalker motor;
        static readonly string[] names={"골짜기와 우회로","높은 턱과 경사로","활공 출발대와 낮은 착지대"};
        public const string Glyphs="이동 지형 실험실단계고정 테스트 행성골짜기와 우회로높은 턱과 경사로활공 출발대낮은 착지대민트색 길기본 이동우회금색능력 지름길폭깊이높이간격출발착지대쉬추가 점프활공사용꺼짐켜짐최근기록측정중출발점에서 움직이면시작숫자구간선택다시연습카메라회전줌정지비교화면설정은유지됩니다미측정초능력변경측정취소로다시측정"+DashHint+JumpHint+GlideHint;
        public const string DashHint="점프 후 Shift로 골짜기를 건너세요.";
        public const string JumpHint="Space를 두 번 눌러 높은 턱에 올라가세요.";
        public const string GlideHint="Space를 누른 채 점프하면 하강할 때 활공합니다.";
        void Start()
        {
            motor=player.GetComponent<GravityWalker>();
            motor.Traversal.SetAllowed(TraversalFeature.Dash,false);motor.Traversal.SetAllowed(TraversalFeature.AirJump,false);motor.Traversal.SetAllowed(TraversalFeature.Glide,false);
            motor.Traversal.AllowedChanged+=CapabilitiesChanged;
            SelectStation(0);
        }
        void CapabilitiesChanged()
        {if(Timing){Timing=false;armed=false;needsReset=true;LastSeconds=0;LastProfile=null;}Refresh();}
        void OnDestroy(){if(motor && motor.Traversal)motor.Traversal.AllowedChanged-=CapabilitiesChanged;}
        public bool Project(int station,float x,float z,out Vector3 point,out Vector3 normal)
        {
            var radial=TraversalTestCourseGeometry.Direction(station,x,z);
            if(ground.Raycast(new Ray(gravity.transform.position+radial*25f,-radial),out var hit,30f))
            {point=hit.point;normal=hit.normal;return true;}
            point=normal=Vector3.zero;return false;
        }
        public void SelectStation(int station)
        {
            if(station<0||station>2||GameplayModalInput.BlocksGameplayInput)return;
            if(!motor)motor=player.GetComponent<GravityWalker>();
            Station=station;var start=TraversalTestCourseGeometry.Start(station);
            if(!Project(station,start.x,start.y,out var point,out var normal))return;
            var forward=TraversalTestCourseGeometry.Direction(station,start.x+.1f,start.y)-TraversalTestCourseGeometry.Direction(station,start.x,start.y);
            practiceSpawn.SetPositionAndRotation(point+normal*.04f,Quaternion.LookRotation(Vector3.ProjectOnPlane(forward,normal),normal));
            player.spawnPoint=practiceSpawn;player.transform.rotation=practiceSpawn.rotation;player.Respawn();
            if(player.cameraRig)player.cameraRig.ResetForPlanet(gravity,ground);
            Timing=false;armed=true;needsReset=false;LastSeconds=0;LastProfile=null;Refresh();
        }
        void Update()
        {
            if(!motor)return;
            if(!GameplayModalInput.BlocksGameplayInput && Time.timeScale>0)
            {
                var kb=Keyboard.current;
                if(kb!=null)
                {
                    if(kb.digit1Key.wasPressedThisFrame)SelectStation(0);
                    if(kb.digit2Key.wasPressedThisFrame)SelectStation(1);
                    if(kb.digit3Key.wasPressedThisFrame)SelectStation(2);
                    if(kb.rKey.wasPressedThisFrame){SelectStation(Station);return;}
                }
                var local=TraversalTestCourseGeometry.Coordinates(Station,player.transform.position-gravity.transform.position);
                if(armed && Vector2.Distance(local,TraversalTestCourseGeometry.Start(Station))>.45f)
                {armed=false;Timing=true;started=Time.time;LastProfile=Profile();}
                if(Timing && motor.Grounded && Vector2.Distance(local,TraversalTestCourseGeometry.Goal(Station))<.8f)
                {LastSeconds=Time.time-started;Timing=false;}
            }
            if(Time.unscaledTime>=nextRefresh){nextRefresh=Time.unscaledTime+.1f;Refresh();}
        }
        string Profile()
        {
            var a=motor.Traversal;
            return $"대쉬 {(a.AllowDash?"켜짐":"꺼짐")} · 추가 점프 {(a.AllowAirJump?"켜짐":"꺼짐")} · 활공 {(a.AllowGlide?"켜짐":"꺼짐")}";
        }
        public void Refresh()
        {
            if(titleLabel)titleLabel.text=$"이동 지형 실험실 · 1단계\n{Station+1}. {names[Station]}";
            string size=Station==0?$"폭 {TraversalTestCourseGeometry.ValleyWidth:0.#}m · 깊이 {TraversalTestCourseGeometry.ValleyDepth:0.#}m"
                :Station==1?$"턱 높이 {TraversalTestCourseGeometry.LedgeHeight:0.#}m"
                :$"간격 {TraversalTestCourseGeometry.GlideGap:0.#}m · 출발 {TraversalTestCourseGeometry.LaunchHeight:0.#}m / 착지 {TraversalTestCourseGeometry.LandingHeight:0.#}m";
            string timing=Timing?$"측정 중 {Time.time-started:0.00}초":needsReset?"능력 변경 · 측정 취소 · R로 다시 측정":LastSeconds>0?$"최근 기록 {LastSeconds:0.00}초\n{LastProfile}":"출발점에서 움직이면 측정 시작";
            string hint=Station==0?DashHint:Station==1?JumpHint:GlideHint;
            if(detailLabel && motor)detailLabel.text=$"{size}\n{hint}\n{Profile()}\n{timing}";
        }
    }
}
