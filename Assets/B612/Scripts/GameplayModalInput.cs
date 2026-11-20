using UnityEngine;
using UnityEngine.InputSystem;

namespace B612
{
    /// <summary>One modal at a time; pause ownership belongs to the caller, input/cursor boundaries live here.</summary>
    public static class GameplayModalInput
    {
        static Object owner;
        static PlayerController player;
        static int closedFrame=-1;
        static bool waitMouseRelease,cursorVisible;
        static CursorLockMode cursorLock;
        public static bool IsOpen=>owner;
        public static bool BlocksGameplayInput=>IsOpen||Time.frameCount==closedFrame;
        public static bool BlocksMouseInput
        {
            get { if(waitMouseRelease&&!MouseHeld())waitMouseRelease=false;return BlocksGameplayInput||waitMouseRelease; }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){owner=null;player=null;closedFrame=-1;waitMouseRelease=false;}
        public static bool TryBegin(Object requester,PlayerController input)
        {
            if(!requester||owner||Time.frameCount==closedFrame)return false;
            owner=requester;player=input;cursorVisible=Cursor.visible;cursorLock=Cursor.lockState;
            if(player)player.BeginModalPause();Cursor.lockState=CursorLockMode.None;Cursor.visible=true;return true;
        }
        public static void Observe(Object requester)
        { if(owner!=requester)return;if(player)player.ObserveModalPauseInput();Cursor.lockState=CursorLockMode.None;Cursor.visible=true; }
        public static void End(Object requester)
        {
            if(!ReferenceEquals(owner,requester))return;
            if(player)player.EndModalPause();owner=null;player=null;closedFrame=Time.frameCount;waitMouseRelease=MouseHeld();
            bool gameOver=GamePauseCoordinator.Active&&GamePauseCoordinator.Active.HasReason(GamePauseReason.GameOver);
            Cursor.lockState=gameOver?CursorLockMode.None:cursorLock;Cursor.visible=gameOver||cursorVisible;
        }
        static bool MouseHeld()=>Mouse.current!=null&&(Mouse.current.leftButton.isPressed||Mouse.current.rightButton.isPressed||Mouse.current.middleButton.isPressed);
    }
}
