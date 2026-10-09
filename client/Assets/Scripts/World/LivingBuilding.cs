using System.Collections.Generic;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    /// <summary>
    /// 2D 建築圖的生命感（2.5D）：LivingSprite 著色器（契印呼吸、窗燈閃爍、旗幟擺動）、
    /// 契印附近緩緩上飄的光點、腳下的接觸陰影、點選時的高亮與輕彈。純畫面效果。
    /// 效果遮罩與光點位置由 tools/art/make_fx_masks.py 產生（Art/Buildings/&lt;name&gt;_fx.png / .json）。
    /// </summary>
    public sealed class LivingBuilding : MonoBehaviour
    {
        private static Material _baseMat, _moteMat;
        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        private SpriteRenderer _sprite;
        private MaterialPropertyBlock _mpb;
        private Transform _art;
        private float _highlight, _bounce;
        private bool _selected;

        /// <summary>把動態效果裝到一棟 2D 建築上。art = 面向鏡頭的圖（底部中央錨點），width/height = 世界單位。</summary>
        public static LivingBuilding Attach(WorldScene w, Transform building, Transform art, SpriteRenderer sprite, string artName, float width, float height)
        {
            var lb = building.gameObject.AddComponent<LivingBuilding>();
            lb._sprite = sprite;
            lb._art = art;
            lb._mpb = new MaterialPropertyBlock();

            var fx = Resources.Load<Texture2D>("Art/Buildings/" + artName + "_fx");
            if (_baseMat == null) _baseMat = Resources.Load<Material>("Abyss/LivingSprite");
            if (fx != null && _baseMat != null)
            {
                if (!Mats.TryGetValue(artName, out var m))
                {
                    m = new Material(_baseMat);
                    m.SetTexture("_FxTex", fx);
                    m.SetFloat("_Phase", (artName.GetHashCode() & 0xff) / 25f);
                    Mats[artName] = m;
                }
                sprite.sharedMaterial = m;
            }

            // 接觸陰影：貼地的深色橢圓，讓圖「站」在地上
            var shadow = w.Prim(PrimitiveType.Cylinder, new Color32(40, 34, 28, 255), new Vector3(0, .004f, -width * .05f), new Vector3(width * .82f, .002f, width * .5f), building);
            shadow.name = "ContactShadow";

            lb.SpawnMotes(w, artName, width, height);
            return lb;
        }

        private void SpawnMotes(WorldScene w, string artName, float width, float height)
        {
            var json = Resources.Load<TextAsset>("Art/Buildings/" + artName + "_fx");
            if (json == null) return;
            var spots = ParseRunes(json.text);
            if (spots.Count == 0) return;
            if (_moteMat == null) _moteMat = Resources.Load<Material>("Abyss/Mote");
            if (_moteMat == null) return;

            var go = new GameObject("RuneMotes") { layer = WorldScene.Layer };
            go.transform.SetParent(_art, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(.06f, .11f);
            main.startColor = new Color(.55f, 1f, .92f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 發射點會跳動，光點要留在原地往上飄
            main.maxParticles = 40;
            var emission = ps.emission;
            emission.rateOverTime = Mathf.Min(10f, 2f + spots.Count * 1.2f);
            var shape = ps.shape;
            shape.enabled = false;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-.03f, .03f);
            vel.y = new ParticleSystem.MinMaxCurve(.06f, .14f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .25f), new GradientAlphaKey(0, 1) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _moteMat;

            // 從契印位置發射：每個位置一個子發射點（同一個系統用 Emit 也行，這裡用簡單的循環移動發射點）
            var mover = go.AddComponent<MoteSpots>();
            mover.Spots = new List<Vector3>();
            foreach (var s in spots) mover.Spots.Add(new Vector3((s.x - .5f) * width, s.y * height, -.01f));
            ps.Play();
        }

        private static List<Vector2> ParseRunes(string text)
        {
            // {"runes": [[u, v], ...]}，JsonUtility 不支援巢狀陣列，自己解析
            var list = new List<Vector2>();
            int i = text.IndexOf('[');
            if (i < 0) return list;
            var body = text.Substring(i + 1);
            foreach (var part in body.Split(new[] { '[' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var nums = part.Split(new[] { ',', ']', ' ', '}' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (nums.Length >= 2 && float.TryParse(nums[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var u)
                    && float.TryParse(nums[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    list.Add(new Vector2(u, v));
            }
            return list;
        }

        public void SetSelected(bool on)
        {
            if (on && !_selected) _bounce = 1f;
            _selected = on;
        }

        private void Update()
        {
            float target = _selected ? .55f + .2f * Mathf.Sin(Time.time * 4f) : 0f;
            _highlight = Mathf.MoveTowards(_highlight, target, Time.deltaTime * 3f);
            _bounce = Mathf.MoveTowards(_bounce, 0, Time.deltaTime * 3.5f);
            if (_sprite != null)
            {
                _sprite.GetPropertyBlock(_mpb);
                _mpb.SetFloat("_Highlight", _highlight);
                _sprite.SetPropertyBlock(_mpb);
            }
            if (_art != null)
            {
                // 點選時輕輕彈一下：先壓扁再彈回
                float b = Mathf.Sin(_bounce * Mathf.PI) * .08f;
                _art.localScale = new Vector3(1f + b * .5f, 1f - b + b * 1.6f * _bounce, 1f);
            }
        }
    }

    /// <summary>粒子發射點在幾個契印位置之間輪流跳，讓光點從各個契印冒出來。</summary>
    public sealed class MoteSpots : MonoBehaviour
    {
        public List<Vector3> Spots;
        private int _i;
        private float _next;

        private void Update()
        {
            if (Spots == null || Spots.Count == 0 || Time.time < _next) return;
            _next = Time.time + .12f;
            _i = (_i + 1) % Spots.Count;
            var p = Spots[_i];
            transform.localPosition = p + new Vector3(Random.Range(-.03f, .03f), Random.Range(-.02f, .02f), 0);
        }
    }
}
