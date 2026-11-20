using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace B612
{
    // M/click only queue intent in Update/UI. Threat's final LateUpdate(50) always decides first.
    [DefaultExecutionOrder(300)]
    public sealed class StarSystemFlowController : MonoBehaviour
    {
        public PlanetWorldHost worldHost;
        public PlayerController player;
        public BaobabSurge surge;
        public LambCardHand hand;
        public GamePauseCoordinator pause;
        public StarSystemMapView view;
        public bool randomizeRunSeed=true;
        public int runSeed=612;
        [Min(0f)] public float transitionSeconds=.35f;
        public StarSystemData SystemData { get; private set; }
        public RunProgressData Progress { get; private set; }
        public PlanetData Current=>SystemData?.Find(Progress.currentPlanetId);
        public bool WorldReady { get; private set; }
        public bool IsMapOpen { get; private set; }
        public bool IsTravelling { get; private set; }
        public bool SystemComplete=>WorldReady&&Current.spec.role==PlanetRole.Destination&&surge.IsComplete&&!surge.IsGameOver;
        public bool CanOpenMap=>WorldReady&&surge.IsComplete&&!surge.IsGameOver&&!IsTravelling
            &&!(DeveloperDebugController.Active&&DeveloperDebugController.Active.IsOpen)&&(!GameplayModalInput.IsOpen||IsMapOpen);
        public string Notice { get; private set; }
        public int UnlockNotificationCount { get; private set; }
        public int CompletedTransitions { get; private set; }
        public event Action Changed;
        public bool CampaignEnabled=>worldHost&&worldHost.settings&&worldHost.settings.chapterCampaign;
        readonly System.Collections.Generic.List<StarSystemData> completedSystems=new System.Collections.Generic.List<StarSystemData>();
        public System.Collections.Generic.IReadOnlyList<StarSystemData> CompletedSystems=>completedSystems;
        public CampaignWorldSites Sites { get; private set; }
        public bool CanAdvanceChapter=>CampaignEnabled&&IsMapOpen&&CanOpenMap&&SystemComplete&&Progress.chapter<3
            &&HasCapabilities(worldHost.settings.Chapter(Progress.chapter+1)?worldHost.settings.Chapter(Progress.chapter+1).guaranteedEntry:ChapterTerrainDefinition.Entry(Progress.chapter+1));
        bool nextChapterRequested;
        CampaignWorldSites siteRequester;int requestedSite=-1;
        public bool HasCapabilities(TraversalCapability mask)=>(Progress.unlocked & mask)==mask;
        public bool RequestNextChapter(){if(!CanAdvanceChapter||nextChapterRequested)return false;nextChapterRequested=true;return true;}
        public bool RequestSiteInteraction(CampaignWorldSites sites,int index)
        {
            if(!CampaignEnabled||!WorldReady||IsTravelling||surge.IsGameOver||GameplayModalInput.BlocksGameplayInput||requestedSite>=0||sites!=Sites||!sites.CanInteract(index))return false;
            siteRequester=sites;requestedSite=index;return true;
        }
        void ApplySiteInteraction()
        {
            int index=requestedSite;var sites=siteRequester;requestedSite=-1;siteRequester=null;
            if(index<0||!sites||sites!=Sites||IsTravelling||GameplayModalInput.BlocksGameplayInput||!sites.CanInteract(index))return;
            Current.state.visitedSites|=1<<index;
            if(Current.state.siteVisitSeconds==null||Current.state.siteVisitSeconds.Length!=3)Current.state.siteVisitSeconds=new float[3];
            Current.state.siteVisitSeconds[index]=sites.ElapsedSeconds;
            if(Current.state.siteTravelMetres==null||Current.state.siteTravelMetres.Length!=3)Current.state.siteTravelMetres=new float[3];Current.state.siteTravelMetres[index]=sites.TravelMetres;
            if(index==0&&Current.spec.unlock!=TraversalCapability.None)
            {
                Progress.unlocked|=Current.spec.unlock;player.GetComponent<PlayerTraversalAbilities>().SetUnlocked(Progress.unlocked);
                ShowNotice(ChapterTerrainDefinition.AbilityName(Current.spec.unlock)+" 해금! 같은 지형의 지름길을 사용해 보세요.");
            }
            else ShowNotice(index==1?"작물 지점 조사 완료 · 이동 시간을 기록했습니다.":"길 안내: 민트색 통로는 기본 이동으로 이어집니다.");
            Changed?.Invoke();
        }
        bool mapRequested,notified;
        string requestedDestination;
        float noticeUntil;
        Coroutine transition;
        PlanetWorldInstance prepared;
        public enum DebugSkipState { Unavailable, Preparing, Busy, GameOver, Pending, Skipped, Resolved, Ready }
        DeveloperDebugController skipRequester;
        string skipSystemId, skipPlanetId;
        PlanetWorldInstance skipWorld;
        public bool DebugSkipPending => skipRequester;

        public DebugSkipState GetDebugSkipState(DeveloperDebugController requester)
        {
            if(!DeveloperDebugController.Available || !isActiveAndEnabled || !requester || requester.flow!=this
                || !worldHost || !surge || !surge.threat || surge.threat.surge!=surge || !hand || hand.surge!=surge)
                return DebugSkipState.Unavailable;
            if(!WorldReady || Current==null || !worldHost.ActiveWorld || !surge.IsRunning
                || surge.surface!=worldHost.ActiveWorld.surface) return DebugSkipState.Preparing;
            if(surge.IsGameOver) return DebugSkipState.GameOver;
            if(IsTravelling || IsMapOpen || !requester.isActiveAndEnabled || !requester.IsOpen
                || DeveloperDebugController.Active!=requester) return DebugSkipState.Busy;
            if(DebugSkipPending) return DebugSkipState.Pending;
            if(surge.WasSkippedByDebug) return DebugSkipState.Skipped;
            return surge.IsComplete && surge.ActiveSproutCount==0 ? DebugSkipState.Resolved : DebugSkipState.Ready;
        }
        public bool RequestDebugSkipBaobab(DeveloperDebugController requester)
        {
            if(GetDebugSkipState(requester)!=DebugSkipState.Ready) return false;
            skipRequester=requester;skipSystemId=SystemData.systemId;skipPlanetId=Current.spec.planetId;skipWorld=worldHost.ActiveWorld;
            return true;
        }
        public void CancelDebugSkipBaobab(DeveloperDebugController requester)
        { if(ReferenceEquals(skipRequester,requester)) ClearDebugSkip(); }
        void ClearDebugSkip(){skipRequester=null;skipSystemId=skipPlanetId=null;skipWorld=null;}
        void ApplyDebugSkip()
        {
            if(ReferenceEquals(skipRequester,null)) return;
            var requester=skipRequester;
            bool targetMatches=skipSystemId==SystemData.systemId && skipPlanetId==Current.spec.planetId && skipWorld==worldHost.ActiveWorld;
            ClearDebugSkip();
            if(targetMatches && GetDebugSkipState(requester)==DebugSkipState.Ready) surge.CompleteForDebug(hand);
        }

        void Awake(){if(surge)surge.automaticStart=false;}
        void Start()
        {
            try
            {
                ValidateBindings();
                int seed=randomizeRunSeed?Guid.NewGuid().GetHashCode():runSeed;
                SystemData=StarSystemFactory.Create(seed,worldHost.settings);
                Progress=new RunProgressData{currentSystemId=SystemData.systemId,currentPlanetId=SystemData.startPlanetId,mode=RunMode.PlayingPlanet,journeySeed=seed,unlocked=CampaignEnabled?TraversalCapability.None:TraversalCapability.All};
                player.GetComponent<PlayerTraversalAbilities>().SetUnlocked(Progress.unlocked);
                Progress.chosenPath.Add(new VisitedPlanet{systemId=SystemData.systemId,planetId=Progress.currentPlanetId});
                var world=worldHost.Initialize(Current,player.GetComponent<CapsuleCollider>());worldHost.Activate(world);BindWorld(world);
                Current.state.worldInitialized=true;Current.state.phase=PlanetPhase.Surge;
                WorldReady=true;surge.Begin(true);Changed?.Invoke();
            }
            catch(Exception ex){ShowNotice("행성을 준비하지 못했습니다. 다시 선택해 주세요.");Debug.LogError("Star-system initialization: "+ex.Message,this);}
        }
        void ValidateBindings()
        {
            if(!worldHost||!worldHost.settings||!player||!surge||!surge.threat||!surge.sproutPrefab||!hand||!hand.lambPrefab||!pause||!view)
                throw new InvalidOperationException("Missing star-system scene references.");
        }
        void Update()
        {
            if(IsMapOpen||IsTravelling)GameplayModalInput.Observe(this);
            if(Notice!=null&&Time.unscaledTime>noticeUntil){Notice=null;Changed?.Invoke();}
            var keyboard=Keyboard.current;if(keyboard==null||IsTravelling)return;
            if(IsMapOpen)
            { if(keyboard.mKey.wasPressedThisFrame||keyboard.escapeKey.wasPressedThisFrame)CloseMap(); }
            else if(keyboard.mKey.wasPressedThisFrame&&!(DeveloperDebugController.Active&&DeveloperDebugController.Active.IsOpen))mapRequested=true;
        }
        void LateUpdate()
        {
            if(!WorldReady){ClearDebugSkip();return;}
            if(surge.IsGameOver)
            {
                ClearDebugSkip();requestedSite=-1;siteRequester=null;nextChapterRequested=false;SnapshotCurrent(false);
                Progress.mode=RunMode.GameOver;mapRequested=false;requestedDestination=null;
                if(IsMapOpen)CloseMap();
                if(IsTravelling)CancelTransition();
                return;
            }
            ApplyDebugSkip();
            ApplySiteInteraction();
            SnapshotCurrent(false);
            if(CampaignEnabled&&Progress.chapter==3&&SystemComplete&&HasCapabilities(TraversalCapability.All))Progress.journeyComplete=true;
            if(surge.IsComplete&&!notified)
            {
                notified=true;UnlockNotificationCount++;
                ShowNotice(SystemComplete?(CampaignEnabled?(Progress.chapter==3?"3챕터 여정 완료":"챕터 완료 · 지도에서 다음 챕터로 이동하세요."):"테스트 항성계 완료"):"행성 지도 활성화 · M 키로 다음 행성 선택");
            }
            if(mapRequested)
            {
                mapRequested=false;
                if(!OpenMap()&&!IsTravelling&&!GameplayModalInput.IsOpen)ShowNotice("바오밥 생성이 끝나면 지도를 열 수 있습니다.");
            }
            if(!string.IsNullOrEmpty(requestedDestination))
            {
                string id=requestedDestination;requestedDestination=null;
                if(CanTravel(id))BeginTransition(id);
            }
            if(nextChapterRequested)
            {
                nextChapterRequested=false;
                if(CanAdvanceChapter)
                {
                    int chapter=Progress.chapter+1;
                    var next=StarSystemFactory.Create(StarSystemFactory.DeriveSeed(Progress.journeySeed,50,chapter),worldHost.settings,chapter);
                    BeginTransition(next.startPlanetId,next);
                }
            }
        }
        public bool OpenMap()
        {
            if(IsMapOpen||!CanOpenMap||!GameplayModalInput.TryBegin(this,player))return false;
            pause.AcquirePause(this,GamePauseReason.MapOverlay);IsMapOpen=true;Progress.mode=RunMode.SelectingRoute;
            if(UnityEngine.EventSystems.EventSystem.current)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            view.ShowMap(true);Changed?.Invoke();return true;
        }
        public void RequestMap(){mapRequested=true;}
        public void CloseMap()
        {
            if(!IsMapOpen||IsTravelling)return;
            IsMapOpen=false;requestedDestination=null;nextChapterRequested=false;if(view)view.ShowMap(false);
            if(UnityEngine.EventSystems.EventSystem.current)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            if(pause)pause.ReleasePause(this);GameplayModalInput.End(this);
            if(Progress!=null)Progress.mode=surge&&surge.IsGameOver?RunMode.GameOver:RunMode.PlayingPlanet;
            Changed?.Invoke();
        }
        public bool CanTravel(string id)
        {
            if(!IsMapOpen||!CanOpenMap||string.IsNullOrEmpty(id)||Current.spec.role==PlanetRole.Destination||id==Progress.currentPlanetId)return false;
            var target=SystemData.Find(id);
            return target!=null&&target.state.phase==PlanetPhase.Unvisited&&SystemData.HasLink(Progress.currentPlanetId,id)&&(!CampaignEnabled||HasCapabilities(target.spec.requiredCapabilities));
        }
        public bool RequestTravel(string id)
        {
            if(!string.IsNullOrEmpty(requestedDestination)||!CanTravel(id))return false;
            requestedDestination=id;return true;
        }
        void BeginTransition(string id,StarSystemData nextSystem=null)
        {
            requestedSite=-1;siteRequester=null;
            IsTravelling=true;Progress.pendingDestinationId=id;Progress.mode=RunMode.Travelling;
            pause.AcquirePause(worldHost,GamePauseReason.PlanetTransition);pause.ReleasePause(this);
            IsMapOpen=false;view.ShowMap(false);view.ShowTransition(true);Changed?.Invoke();
            transition=StartCoroutine(Travel(id,nextSystem));
        }
        IEnumerator Travel(string id,StarSystemData nextSystem)
        {
            yield return new WaitForSecondsRealtime(transitionSeconds);
            Exception failure=null;
            try {ValidateBindings();prepared=worldHost.Prepare((nextSystem??SystemData).Find(id),player.GetComponent<CapsuleCollider>());}
            catch(Exception ex){failure=ex;}
            if(failure!=null)
            {
                // No current world/data/inventory has been changed before this point.
                Progress.pendingDestinationId=null;IsTravelling=false;IsMapOpen=true;Progress.mode=RunMode.SelectingRoute;
                pause.AcquirePause(this,GamePauseReason.MapOverlay);pause.ReleasePause(worldHost);
                Physics.SyncTransforms();view.ShowTransition(false);view.ShowMap(true);
                ShowNotice("행성을 준비하지 못했습니다. 다시 선택해 주세요.");transition=null;yield break;
            }
            var previous=worldHost.ActiveWorld;
            SnapshotCurrent(true);hand.RecallForTravel();surge.StopAndClear();
            if(nextSystem!=null){completedSystems.Add(SystemData);SystemData=nextSystem;Progress.chapter=nextSystem.systemIndex;Progress.currentSystemId=nextSystem.systemId;}
            worldHost.Activate(prepared);BindWorld(prepared);
            Progress.currentPlanetId=id;Progress.chosenPath.Add(new VisitedPlanet{systemId=SystemData.systemId,planetId=id});
            Current.state.worldInitialized=true;Current.state.phase=PlanetPhase.Surge;
            notified=false;Notice=null;CompletedTransitions++;WorldReady=true;
            surge.Begin(true);hand.BindSurfaceAfterTravel();
            if(previous)previous.DisposeWorld();prepared=null;
            yield return new WaitForSecondsRealtime(transitionSeconds);
            Progress.pendingDestinationId=null;Progress.mode=RunMode.PlayingPlanet;IsTravelling=false;
            view.ShowTransition(false);GameplayModalInput.End(this);pause.ReleasePause(worldHost);transition=null;Changed?.Invoke();
        }
        void BindWorld(PlanetWorldInstance world)
        {
            surge.surface=world.surface;surge.spawnPoint=world.spawn;surge.randomizeSeed=false;surge.seed=world.Data.spec.seed;
            var plan=world.Data.spec.baobab;surge.durationSeconds=plan.durationSeconds;surge.targetSprouts=plan.spawnEventCount;
            surge.maxActiveSprouts=plan.maxActive;surge.minimumSpacing=plan.minimumSpacing;surge.threat.runCapacityOverride=plan.threatCapacity;
            player.spawnPoint=world.spawn;player.NearbyLaunch=null;player.GetComponent<GravityWalker>().Suspend(false);player.Respawn();
            if(player.cameraRig)player.cameraRig.ResetForPlanet(world.gravity,world.ground);
            if(CampaignEnabled){Sites=world.gameObject.AddComponent<CampaignWorldSites>();Sites.Initialize(this,world);}
        }
        void SnapshotCurrent(bool departed)
        {
            if(Current==null)return;
            var state=Current.state;state.simulationSeconds=surge.SimulationSeconds;state.surge.elapsedSpawnSeconds=surge.ElapsedSeconds;state.surge.processedSpawnEvents=surge.Attempts;
            state.surge.skippedByDebug=surge.WasSkippedByDebug;
            if(surge.IsGameOver){state.phase=PlanetPhase.GameOver;state.surge.result=SurgeResult.Failed;return;}
            if(surge.IsComplete&&state.phase==PlanetPhase.Surge)state.phase=PlanetPhase.Clearing;
            state.surge.result=surge.IsComplete&&surge.ActiveSproutCount==0?SurgeResult.Resolved:SurgeResult.InProgress;
            if(departed){state.phase=PlanetPhase.Departed;state.surge.result=surge.ActiveSproutCount>0?SurgeResult.LeftWithRemaining:SurgeResult.Resolved;}
        }
        void ShowNotice(string message){Notice=message;noticeUntil=Time.unscaledTime+4f;Changed?.Invoke();}
        void CancelTransition()
        {
            if(transition!=null)StopCoroutine(transition);transition=null;
            if(prepared){prepared.DisposeWorld();prepared=null;}
            IsTravelling=false;if(Progress!=null){Progress.pendingDestinationId=null;Progress.mode=surge&&surge.IsGameOver?RunMode.GameOver:RunMode.PlayingPlanet;}
            if(view)view.ShowTransition(false);if(pause&&worldHost)pause.ReleasePause(worldHost);GameplayModalInput.End(this);
        }
        public void CancelMapAndTravel(){if(IsTravelling)CancelTransition();CloseMap();}
        void OnDisable(){ClearDebugSkip();requestedSite=-1;siteRequester=null;nextChapterRequested=false;mapRequested=false;requestedDestination=null;if(IsTravelling)CancelTransition();CloseMap();}
    }
}
