using System;
using System.Collections;
using System.IO;
using System.Reflection;
using AbyssKingdom.Client.Game;
using AbyssKingdom.Client.UI;
using UnityEngine;

namespace AbyssKingdom.Client.Diagnostics
{
    /// <summary>Local Dev asset check: -devlogin -itemcheck output-directory.</summary>
    public sealed class ItemIconCheck : MonoBehaviour
    {
        private string _output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args=Environment.GetCommandLineArgs();
            int output=Array.IndexOf(args,"-itemcheck"),server=Array.IndexOf(args,"-server");
            if(output<0 || output+1>=args.Length || server<0 || server+1>=args.Length ||
                Array.IndexOf(args,"-devlogin")<0 || !Uri.TryCreate(args[server+1],UriKind.Absolute,out var uri) || !uri.IsLoopback) return;
            var go=new GameObject("LocalItemIconCheck");DontDestroyOnLoad(go);
            go.AddComponent<ItemIconCheck>()._output=Path.GetFullPath(args[output+1]);
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_output);
            float deadline=Time.realtimeSinceStartup+120;
            while(GameClient.Instance==null || !GameClient.Instance.IsLoggedIn || FindAnyObjectByType<IntroSequence>()!=null)
            {
                if(Time.realtimeSinceStartup>deadline) { Debug.LogError("ITEM_CHECK login/intro timeout");Application.Quit(2);yield break; }
                yield return null;
            }
            var names=new[]{"Speedup_General","Speedup_Building","Speedup_Research","Speedup_Magic","Speedup_Training",
                "HeroExpSmall","HeroExpMedium","HeroExpLarge","DomainScrollPower","DomainScrollTechnique",
                "DomainScrollSpeed","DomainScrollDurability","HeroShardAny","HeroShardHold","HeroShardVesper","HeroShardKarst"};
            int failed=0;
            foreach(var name in names)
            {
                var tex=Resources.Load<Texture2D>("Art/Items/"+name);
                bool ok=tex!=null && tex.width==256 && tex.height==256;
                if(!ok) failed++;
                Debug.Log("ITEM_CHECK "+name+" "+(ok?"PASS":"FAIL"));
            }
            var ui=FindAnyObjectByType<GameUI>();
            typeof(GameUI).GetMethod("OpenPage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,new object[]{3});
            yield return new WaitForSecondsRealtime(3);
            yield return new WaitForEndOfFrame();
            var image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(_output,"bag.png"),image.EncodeToPNG());Destroy(image);
            File.WriteAllText(Path.Combine(_output,"result.json"),"{\"status\":\""+(failed==0?"PASS":"FAIL")+"\",\"textures\":16,\"failures\":"+failed+"}");
            Debug.Log("ITEM_CHECK COMPLETE failures="+failed);
            Application.Quit(failed==0?0:2);
        }
    }
}
