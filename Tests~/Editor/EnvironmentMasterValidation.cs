using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EnvironmentMaster;
using EnvironmentMaster.Editor;
using OneClickSceneBake;

[InitializeOnLoad]
public static class EnvironmentMasterValidation
{
 const string Key="EnvironmentMasterValidation.Stage";
 static double started;
 static EnvironmentMasterValidation() { EditorApplication.update += Poll; }
 static void Check(bool yes,string label) { if(!yes)throw new Exception(label); File.AppendAllText("../checks.txt","PASS "+label+"\n"); Debug.Log("ENV_CHECK PASS "+label); }
 public static void Run()
 {
  try {
   File.WriteAllText("../checks.txt","");
   var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var front=GameObject.CreatePrimitive(PrimitiveType.Cube);front.name="Visible";front.transform.position=Vector3.zero;
   var behind=GameObject.CreatePrimitive(PrimitiveType.Cube);behind.name="Behind";behind.transform.position=new Vector3(0,0,-30);
   var cam=new GameObject("Main Camera").AddComponent<Camera>();cam.tag="MainCamera";cam.transform.position=new Vector3(0,0,-10);cam.farClipPlane=200;
   var prior=cam.layerCullDistances;prior[9]=77;cam.layerCullDistances=prior;
   EnvironmentOptimization.ApplyCamera(cam,8,30,true,true);
   Check(cam.layerCullDistances[8]==30 && cam.layerCullDistances[9]==77 && cam.useOcclusionCulling,"Per-layer distance preserves other layers");
   Check(front.activeSelf && front.GetComponent<Renderer>().enabled && behind.GetComponent<Renderer>().enabled,"Culling does not disable objects");
   using(var sampler=new EnvironmentSampler()) {
    var s=sampler.Sample(cam,new[]{front.GetComponent<Renderer>(),behind.GetComponent<Renderer>()},scene.path,false);
    Check(s.activeRenderers==2 && s.frustumCandidates==1 && s.outsideFrustum==1,"Frustum candidate calculation");
    Check(s.fps<0,"Editor FPS is unavailable rather than fabricated");
   }
   var tex=new Texture2D(8,8);File.WriteAllBytes("Assets/ProbeTexture.png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
   var sprite=new Texture2D(8,8);File.WriteAllBytes("Assets/UISprite.png",sprite.EncodeToPNG());UnityEngine.Object.DestroyImmediate(sprite);
   AssetDatabase.Refresh();
   var importer=(TextureImporter)AssetImporter.GetAtPath("Assets/ProbeTexture.png");importer.mipmapEnabled=false;importer.streamingMipmaps=false;importer.isReadable=true;importer.SaveAndReimport();
   var spriteImporter=(TextureImporter)AssetImporter.GetAtPath("Assets/UISprite.png");spriteImporter.textureType=TextureImporterType.Sprite;spriteImporter.SaveAndReimport();
   var mat=new Material(Shader.Find("Standard"));mat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ProbeTexture.png");AssetDatabase.CreateAsset(mat,"Assets/ProbeMaterial.mat");front.GetComponent<Renderer>().sharedMaterial=mat;
   var uiMat=new Material(Shader.Find("Standard"));uiMat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UISprite.png");AssetDatabase.CreateAsset(uiMat,"Assets/UIMaterial.mat");behind.GetComponent<Renderer>().sharedMaterial=uiMat;
   EditorSceneManager.SaveScene(scene,"Assets/EnvironmentValidation.unity");
   Check(EnvironmentOptimization.SceneTexturePaths().Contains("Assets/ProbeTexture.png")&&!EnvironmentOptimization.SceneTexturePaths().Contains("Assets/UISprite.png"),"Scene texture scope excludes UI Sprite");
   bool oldActive=QualitySettings.streamingMipmapsActive;float oldBudget=QualitySettings.streamingMipmapsMemoryBudget;
   EnvironmentOptimization.ConfigureTextures(true,256,true,true,true,1024);
   importer=(TextureImporter)AssetImporter.GetAtPath("Assets/ProbeTexture.png");
   Check(importer.mipmapEnabled&&importer.streamingMipmaps&&!importer.isReadable&&importer.maxTextureSize<=1024,"Texture settings actually imported");
   Check(QualitySettings.streamingMipmapsActive&&QualitySettings.streamingMipmapsMemoryBudget==256,"Quality streaming budget applied");
   Check(File.ReadAllText("ProjectSettings/QualitySettings.asset").Contains("streamingMipmapsMemoryBudget: 256"),"Streaming quality saved to disk");
   EnvironmentOptimization.RestoreTextures();importer=(TextureImporter)AssetImporter.GetAtPath("Assets/ProbeTexture.png");
   Check(!importer.mipmapEnabled&&!importer.streamingMipmaps&&importer.isReadable,"Texture importer restore");
   Check(QualitySettings.streamingMipmapsActive==oldActive&&QualitySettings.streamingMipmapsMemoryBudget==oldBudget,"Streaming quality restore");
   Selection.activeGameObject=front;EnvironmentOptimization.MarkOcclusion(true);
   var flags=GameObjectUtility.GetStaticEditorFlags(front);
   Check((flags&StaticEditorFlags.OccludeeStatic)!=0&&(flags&StaticEditorFlags.OccluderStatic)!=0,"Selected occlusion flags");
   Check(GameObjectUtility.GetStaticEditorFlags(behind)==0,"Unselected static flags unchanged");
   EnvironmentOptimization.CreateOcclusionArea();Check(UnityEngine.Object.FindObjectOfType<OcclusionArea>()!=null,"Occlusion area creation");
   var lodRoot=new GameObject("LOD Root");lodRoot.transform.position=Vector3.right*20;
   for(int i=0;i<3;i++){var child=GameObject.CreatePrimitive(PrimitiveType.Cube);child.name="LOD"+i;child.transform.SetParent(lodRoot.transform,false);}
   EnvironmentOptimization.CreateLODFromChildren(lodRoot);Check(lodRoot.GetComponent<LODGroup>().GetLODs().Length==3,"LOD group uses supplied models");
   Selection.activeGameObject=front;EnvironmentOptimization.EnableSelectedInstancing();Check(mat.enableInstancing,"Selected material instancing flag");
   Check(EnvironmentOptimization.RenderingAudit().Contains("LODGroup"),"Rendering audit produces report");
   var window=ScriptableObject.CreateInstance<SceneBakeWindow>();window.CreateGUI();
   Check(window.rootVisualElement.childCount>=3,"Five-tab window creates successfully");UnityEngine.Object.DestroyImmediate(window);
   EnvironmentMonitoring.Enabled=true;EnvironmentMonitoring.Record=true;EnvironmentMonitoring.Interval=0.25f;
   var monitor=new GameObject("RuntimeMonitor").AddComponent<EnvironmentRuntimeMonitor>();monitor.sampleInterval=0.25f;
   EditorSceneManager.SaveScene(scene);
   SessionState.SetString(Key,"editor-monitor");started=EditorApplication.timeSinceStartup;
  } catch(Exception ex){Finish(false,ex.ToString());}
 }
 static void Poll()
 {
  string stage=SessionState.GetString(Key,"");if(string.IsNullOrEmpty(stage))return;
  try {
   if(stage=="editor-monitor"&&EditorApplication.timeSinceStartup-started>2) {
    Check(EnvironmentMonitoring.SampleCount>=2&&File.Exists(EnvironmentMonitoring.LogPath),"Monitoring continues after window closes and persists JSONL");
    EnvironmentMonitoring.SaveBaseline();Check(EnvironmentMonitoring.Baseline!=null,"Baseline retained");
    EnvironmentMonitoring.ExportCSV("../monitor.csv");Check(File.ReadAllLines("../monitor.csv").Length>2,"CSV history export");
    EnvironmentOptimization.BakeOcclusion();SessionState.SetString(Key,"occlusion");started=EditorApplication.timeSinceStartup;
   } else if(stage=="occlusion"&&!StaticOcclusionCulling.isRunning) {
    Check(File.Exists("Assets/EnvironmentValidation/OcclusionCullingData.asset") && new FileInfo("Assets/EnvironmentValidation/OcclusionCullingData.asset").Length>0,"Occlusion bake creates data asset");
    EditorSceneManager.SaveScene(SceneManager.GetActiveScene());PlayerSettings.runInBackground=true;
    SessionState.SetString(Key,"play");SessionState.SetString(Key+"Time",DateTime.UtcNow.ToString("O"));EditorApplication.isPlaying=true;
   } else if(stage=="play"&&EditorApplication.isPlaying) {
    var monitor=UnityEngine.Object.FindObjectOfType<EnvironmentRuntimeMonitor>();
    if(monitor&&monitor.latest!=null&&monitor.latest.fps>0) {
     Check(Time.frameCount>1&&monitor.latest.frameMs>0,"PlayMode frames advance and runtime FPS sampled");
     Check(File.Exists(monitor.logPath)&&File.ReadAllLines(monitor.logPath).Length>=2,"Player component persists repeated samples");
     SessionState.SetString(Key,"finish");EditorApplication.isPlaying=false;
    } else if((DateTime.UtcNow-DateTime.Parse(SessionState.GetString(Key+"Time",""))).TotalSeconds>45)throw new Exception("PlayMode telemetry timeout");
   } else if(stage=="finish"&&!EditorApplication.isPlayingOrWillChangePlaymode) Finish(true,"All environment optimization and telemetry checks passed");
   if((stage=="editor-monitor"||stage=="occlusion")&&EditorApplication.timeSinceStartup-started>90)throw new Exception("Editor stage timeout: "+stage);
  }catch(Exception ex){Finish(false,ex.ToString());}
 }
 static void Finish(bool pass,string message) {
  SessionState.SetString(Key,"");File.WriteAllText("../result.txt",(pass?"PASS\n":"FAIL\n")+message);Debug.Log("ENV_VALIDATION "+pass+" "+message);EditorApplication.Exit(pass?0:1);
 }
}
