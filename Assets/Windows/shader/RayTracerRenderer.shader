Shader "Custom/RayTracerRenderer"
{
 Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _HorizontColor("Horiznotcolor",vector) = (0.85, 0.92, 1.0)
        _ZenithColor("ZenithColor",vector) = (0.22, 0.50, 1.0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            
            /*
              boilerplate code to pass vertex uv coordinates to fragment shader 
              using Worldpos for future to represent objects that are to be drew in the shader in worldspace 
            */
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 worldPos : TEXCOORD1; };

            /*
              triangle struct that groups data about real 3D FBX Object for rendering
            */
            struct Triangle
            {
                float3 a,b,c; 
                float2 uva,uvb,uvc;
            };
            /*
              optimized bvhnode struct 
              it has index for left child and we can also get 
              right child from adding 1 to left child because they are together 
            */
            struct BvhNode
            {
                float3 min;
                float3 max;

                uint leftfirst;
                uint tricount;
            };
            /*
              triangle data that needed for optimised BVH traversal
            */
            struct Tri
            {
                 float3 v0, v1, v2, centre;
            };
            /*
               Acceleration structure buffers for hierarhcy traversal
            */
            StructuredBuffer<Tri>_BvhTriangles;

            StructuredBuffer<int>_BvhIndices;

            StructuredBuffer<BvhNode>_BvhNodes;


            /*
              buffer for object data(actual geometry/ uv ) to render stuff on screen
              triangle count from cpu side to have number of triangles 
            */
            StructuredBuffer<Triangle> _Triangles;
            
            /*
              Camera postion -> ray origin 
              behindquad space in world space that occupies space behind the quad 
            */
            float3 _CameraPos;  
            float4x4 _BehindTheQuadSpace;
            
            //main texture currently using atlases so this texture more than enough
            sampler2D _MainTex;
            
           
           
           
            // DAY
            // clear sky 0.85, 0.92, 1.0 -> 0.22, 0.50, 1.0
           

            /*
              basically generating color limits from horizont to ZenithColor
              caluclated based on ray direction 
            */
            uniform float3 _HorizontColor;
            uniform float3 _ZenithColor;

            float4 GetSkyColor(float3 dir)
            {
                // here theoretically we making color always the same thing 
                // because it is saturates from [0 1] [1 1] and viewpoert is taking one of the limits not whole but still color is cool 
                float t = saturate(dir.y * 0.5 + 0.5);

                return float4(lerp(_HorizontColor, _ZenithColor, t), 1.0);
            }

            /*
              tranlating data from bridge struct vertex to fragment aswell as populating it with data 
              also here we tranlating vertex data of an object to world space 
            */
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;

                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            
            // Simple ray-triangle intersection (M�ller�Trumbore)
            // took the optimized implmention with <<built in culling off>> rendering object from both sides 
            // fps boost is not that significant in small scenes
            bool RayTriangle(float3 ro, float3 rd, float3 a, float3 b, float3 c,
                out float t, out float u, out float v)
            {
                float3 e1 = b - a;
                float3 e2 = c - a;

                float3 p = cross(rd, e2);
                float det = dot(e1, p);
                
                if (abs(det) < 1e-6) { t = 0; u = 0; v = 0; return false; }
               
                float inv = 1.0 / det;
                float3 s = ro - a;
                
                u = dot(s, p) * inv;
                if (u < 0 || u > 1) { t = 0; v = 0; return false; }
                
                float3 q = cross(s, e1);
                
                v = dot(rd, q) * inv;
                if (v < 0 || u + v > 1) { t = 0; u = 0; return false; }
                
                t = dot(e2, q) * inv;
                
                return t > 0;
            }
       
            float IsRayInBox(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax)
            {
                //protects against infinity and NaN corruption on axis-aligned directions
                float3 invDir = 1.0 / (rayDir + sign(rayDir) * 1e-8);
    
               
                float3 t0 = (boxMin - rayOrigin)* invDir;
                float3 t1 = (boxMax - rayOrigin)* invDir;
    
               
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
    
               
                float tNear = max(max(tmin.x, tmin.y), tmin.z);
                float tFar = min(min(tmax.x, tmax.y), tmax.z);
    
                
                bool Hit = tNear <= tFar && tFar > 0.0;
                return Hit ? tNear : 1e20f;
            }
            /*
              optimized bvh traversal with stacks 
              cutting out long rays if short rays hited triangles before them 
              poping distant nodes basically
            */
            int RayTriangleBVHTest(float3 rayOrigin, float3 rayDir, inout float closestT, inout int hitTriangleID
                ,inout float hitU, inout float hitV)
            {
                BvhNode nodestack[32];
                int stackidx = 0;
                nodestack[stackidx++] = _BvhNodes[0];

                while(stackidx > 0)
                {
                    BvhNode node = nodestack[--stackidx];
                     
                    //if leaf check triangles with moller
                    if(node.tricount > 0)
                    {
                        for(uint i = 0; i < node.tricount; i++)
                        {
                            uint indiceslookupidx = node.leftfirst + i;

                            int realtriangleidx = _BvhIndices[indiceslookupidx];

                            Tri bvhTri = _BvhTriangles[_BvhIndices[node.leftfirst + i]];

                            float t,u,v;
                            if(RayTriangle(rayOrigin,rayDir,bvhTri.v0,bvhTri.v1,bvhTri.v2, t, u, v))
                            {
                                if (t > 0.001f && t < closestT)
                                {
                                    closestT = t;
                                    hitTriangleID = realtriangleidx; 
                                    //passing u and v for fragment shader in future 
                                    hitU = u; 
                                    hitV = v;
                                }
                            }

                        }

                    } 
                    else
                    {
                        BvhNode childA = _BvhNodes[node.leftfirst + 0];
                        BvhNode childB = _BvhNodes[node.leftfirst + 1];

                        float dist1 = IsRayInBox(rayOrigin,rayDir,childA.min,childA.max);
                        float dist2 = IsRayInBox(rayOrigin,rayDir,childB.min,childB.max);

                        //closest child branching if any dist is lower we should stop to nearest
                        if(dist1 > dist2)
                        {
                           if(dist1 < closestT) nodestack[stackidx++] = childA;
                           if(dist2 < closestT) nodestack[stackidx++] = childB;
                        }
                        else
                        {
                           if(dist2 < closestT) nodestack[stackidx++] = childB;
                           if(dist1 < closestT) nodestack[stackidx++] = childA;

                        }
                    }

                       
                    
                }
               return hitTriangleID;

            }
            /*
              main function of this shader 
              calculating direction which is distance from camera to quad

              Generating primary world-space rays, traverses the BVH acceleration structure,
              and handles barycentric texture mapping  sky gradient fallbacks.
            */
            float4 frag(v2f i) : SV_Target
            {
                //tranlsating camera and  ray direction into world space
                float3 ro = mul(_BehindTheQuadSpace, float4(_CameraPos, 1.0)).xyz;
                float3 localWindowPos = mul(_BehindTheQuadSpace, float4(i.worldPos, 1.0)).xyz;

               
                float3 rd = normalize(localWindowPos - ro);

                float tMin = 1e20;
                
                
                float2 hitUV = float2(0,0);
                
                int hittriangle = -1;
                float u = 0.0, v = 0.0;

                if(RayTriangleBVHTest(ro,rd,tMin,hittriangle,u,v) != -1)
                {
                   
                    Triangle tri = _Triangles[hittriangle];

                        
                    float w = 1.0 - u - v; 

                    hitUV = tri.uva.xy * w + tri.uvb.xy * u + tri.uvc.xy * v;
                                               
                   
                    
                    
                    float4 col = tex2D(_MainTex, hitUV);
                    return col;
                }
                     
                float4 sky = GetSkyColor(rd);
                
                
               
                return sky;  
            }
            ENDCG
        }
    }
}
