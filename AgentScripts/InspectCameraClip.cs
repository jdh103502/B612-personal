using System;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

public static class InspectCameraClip
{
    public static object Start()
    {
        var go=new GameObject("Temporary camera clip inspection");
        go.AddComponent<CameraClipFrames>();
        return "Extracting 12 reference frames to Temp/CameraClip.png";
    }
}
public sealed class CameraClipFrames : MonoBehaviour
{
    VideoPlayer video;
    RenderTexture render;
    Texture2D sheet;
    int index;
    long wanted;
    bool next;
    void Start()
    {
        Directory.CreateDirectory("Temp");
        render=new RenderTexture(480,270,0);
        sheet=new Texture2D(1920,810,TextureFormat.RGB24,false);
        video=gameObject.AddComponent<VideoPlayer>();
        video.playOnAwake=false;video.audioOutputMode=VideoAudioOutputMode.None;
        video.source=VideoSource.Url;video.url=new Uri(@"C:\Users\jdh10\Videos\녹음 2026-10-31 144826.mp4").AbsoluteUri;
        video.renderMode=VideoRenderMode.RenderTexture;video.targetTexture=render;
        video.sendFrameReadyEvents=true;
        video.errorReceived+=(v,error)=>{File.WriteAllText("Temp/CameraClip-error.txt",error);Destroy(gameObject);};
        video.prepareCompleted+=v=>{File.WriteAllText("Temp/CameraClip-info.txt",$"length={v.length}\nframes={v.frameCount}\nfps={v.frameRate}\nsize={v.width}x{v.height}");v.Play();};
        video.frameReady+=Capture;
        video.Prepare();
    }
    void Capture(VideoPlayer v,long frame)
    {
        if(next || frame<wanted || frame>wanted+3)return;
        var previous=RenderTexture.active;RenderTexture.active=render;
        var tile=new Texture2D(480,270,TextureFormat.RGB24,false);
        tile.ReadPixels(new Rect(0,0,480,270),0,0);tile.Apply();RenderTexture.active=previous;
        sheet.SetPixels((index%4)*480,(2-index/4)*270,480,270,tile.GetPixels());
        Destroy(tile);v.Pause();index++;next=true;
    }
    void Update()
    {
        if(!next)return;
        next=false;
        if(index>=12)
        {
            sheet.Apply();File.WriteAllBytes("Temp/CameraClip.png",sheet.EncodeToPNG());
            video.Stop();Destroy(gameObject);return;
        }
        wanted=(long)((video.frameCount-1)*(double)index/11.0);
        video.frame=wanted;video.Play();
    }
    void OnDestroy()
    {
        if(video){video.frameReady-=Capture;video.Stop();}
        if(render){render.Release();Destroy(render);}if(sheet)Destroy(sheet);
    }
}
