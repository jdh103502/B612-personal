using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace B612
{
    // Hand-authored, fixed dimensions in metres. This is a calibration fixture, not a terrain generator.
    public static class TraversalTestCourseGeometry
    {
        public const float Radius=12f, ValleyWidth=4.2f, ValleyDepth=2f, LedgeHeight=2f;
        public const float GlideGap=7f, LaunchHeight=2.6f, LandingHeight=.4f;
        // Exact breakpoints shared by the calibration mesh and the campaign's reviewed mesh.
        internal static readonly float[] LongitudinalCuts =
            {-10,-9,-4.2f,-4,-3.6f,-2.18f,-2.1f,-2.02f,-2,-1.85f,-.1f,.1f,2.02f,2.1f,2.18f,2.6f,3.5f,4,5,5.15f,6.6f,8,9,10};
        internal static readonly float[] LateralCuts = {-7.7f,-7,-6.5f,-6,-5.7f,-5,-1,0,1,5,5.7f,6,6.5f,7,7.7f};
        public static float Centre(int station)=>station*2f*Mathf.PI*Radius/3f;
        public static Vector3 Direction(int station,float x,float z)
        {
            float u=(Centre(station)+x)/Radius,v=z/Radius;
            return new Vector3(Mathf.Sin(u)*Mathf.Cos(v),Mathf.Sin(v),Mathf.Cos(u)*Mathf.Cos(v));
        }
        public static Vector2 Coordinates(int station,Vector3 point)
        {
            var n=point.normalized;
            float x=Mathf.DeltaAngle(Centre(station)/Radius*Mathf.Rad2Deg,Mathf.Atan2(n.x,n.z)*Mathf.Rad2Deg)*Mathf.Deg2Rad*Radius;
            return new Vector2(x,Mathf.Asin(Mathf.Clamp(n.y,-1,1))*Radius);
        }
        static float Ramp(float a,float b,float x)=>Mathf.Clamp01((x-a)/(b-a));
        public static float Height(int station,float x,float z)
        {
            float side=Mathf.Abs(z);
            if(station==0)return -ValleyDepth*(1-Ramp(ValleyWidth*.5f-.08f,ValleyWidth*.5f+.08f,Mathf.Abs(x)))*(1-Ramp(1f,5f,side));
            if(station==1)return LedgeHeight*Ramp(-.10f,.10f,x)*(1-Ramp(4f,9f,x))*(1-Ramp(1f,6f,side));
            float h;
            if(x< -2f)h=LaunchHeight*Ramp(-9f,-4f,x);
            else if(x< -1.85f)h=Mathf.Lerp(LaunchHeight,-1.4f,Ramp(-2f,-1.85f,x));
            else if(x<5f)h=-1.4f;
            else if(x<5.15f)h=Mathf.Lerp(-1.4f,LandingHeight,Ramp(5f,5.15f,x));
            else h=LandingHeight*(1-Ramp(8f,10f,x));
            return h*(1-Ramp(1f,7f,side));
        }
        public static Vector2 Start(int station)=>new Vector2(-3.6f,0);
        public static Vector2 Goal(int station)=>new Vector2(station==0?3.5f:station==1?2.6f:6.6f,0);
        public static Vector2[] BasicRoute(int station)
        {
            var start=Start(station);var goal=Goal(station);
            float side=station==0?5.7f:station==1?6.5f:7.7f;
            return new[]{start,new Vector2(start.x,side),new Vector2(goal.x,side),goal};
        }
        static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
        {var d=b-a;return Vector2.Distance(p,a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude));}
        static Color GroundColor(int station,float x,float z,float h)
        {
            var p=new Vector2(x,z);var start=Start(station);var goal=Goal(station);
            if(Vector2.Distance(p,start)<.7f)return new Color(.25f,.6f,.67f);
            if(Vector2.Distance(p,goal)<.85f)return new Color(.98f,.82f,.45f);
            var route=BasicRoute(station);
            for(int i=0;i<route.Length-1;i++)if(SegmentDistance(p,route[i],route[i+1])<.38f)return new Color(.43f,.77f,.63f);
            if(x>start.x && x<goal.x && Mathf.Abs(z)<.18f)return new Color(.93f,.68f,.32f);
            var low=new Color(.45f,.38f,.52f);var high=new Color(.77f,.73f,.61f);
            return Color.Lerp(low,high,Mathf.Clamp01((h+2f)/3f));
        }
        public static Mesh CreateMesh()
        {
            var us=new SortedSet<float>();var vs=new SortedSet<float>();float circumference=2f*Mathf.PI*Radius;
            for(int i=0;i<=384;i++)us.Add(i*circumference/384f);
            for(int i=0;i<=192;i++)vs.Add(-Mathf.PI*Radius/2+i*Mathf.PI*Radius/192f);
            // Exact breakpoints keep narrow cliff faces at their authored width, independently of tessellation.
            for(int s=0;s<3;s++)foreach(float cut in LongitudinalCuts)us.Add(Mathf.Repeat(Centre(s)+cut,circumference));
            foreach(float cut in LateralCuts)vs.Add(cut);
            var u=us.ToArray();var v=vs.ToArray();int columns=u.Length,rows=v.Length;
            var points=new Vector3[columns*rows];var colors=new Color[points.Length];var normals=new Vector3[points.Length];
            for(int j=0;j<rows;j++)for(int i=0;i<columns;i++)
            {
                float longitude=u[i]/Radius,latitude=v[j]/Radius;
                var radial=new Vector3(Mathf.Sin(longitude)*Mathf.Cos(latitude),Mathf.Sin(latitude),Mathf.Cos(longitude)*Mathf.Cos(latitude));
                if(j==0)radial=Vector3.down;if(j==rows-1)radial=Vector3.up;
                int station=Mathf.RoundToInt(u[i]/(circumference/3f))%3;var local=Coordinates(station,radial);
                float h=Height(station,local.x,local.y);int id=j*columns+i;
                points[id]=radial*(Radius+h);colors[id]=GroundColor(station,local.x,local.y,h);
                // Analytic radial-field derivative gives matching seam normals, including shared poles.
                var east=new Vector3(Mathf.Cos(longitude),0,-Mathf.Sin(longitude));var north=Vector3.Cross(radial,east);
                float dx=(Height(station,local.x+.015f,local.y)-Height(station,local.x-.015f,local.y))/.03f;
                float dz=(Height(station,local.x,local.y+.015f)-Height(station,local.x,local.y-.015f))/.03f;
                normals[id]=(radial-east*dx*Radius/((Radius+h)*Mathf.Max(.05f,Mathf.Cos(latitude)))-north*dz*Radius/(Radius+h)).normalized;
            }
            var indices=new List<int>(columns*rows*6);
            for(int j=0;j<rows-1;j++)for(int i=0;i<columns-1;i++)
            {int a=j*columns+i,b=a+1,c=a+columns,d=c+1;if(j>0){indices.Add(a);indices.Add(b);indices.Add(d);}if(j<rows-2){indices.Add(a);indices.Add(d);indices.Add(c);}}
            var mesh=new Mesh{name="Fixed traversal planet",indexFormat=IndexFormat.UInt32};
            mesh.vertices=points;mesh.normals=normals;mesh.colors=colors;mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;
        }
    }
}
