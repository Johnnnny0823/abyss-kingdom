using System.Collections.Generic;
using AbyssKingdom.Shared.Map;
using AbyssKingdom.Shared.Protocol;
using AbyssKingdom.Shared.Social;
using UnityEngine;

namespace AbyssKingdom.Client.World
{
    public sealed partial class SurfaceView
    {
        private readonly Dictionary<string, (GameObject root, bool active, bool mine)> _flags =
            new Dictionary<string, (GameObject, bool, bool)>();
        private Material _territoryMaterial;
        private GameObject _flagPreview;
        public void ClearFlagPreview()
        {
            if (_flagPreview != null) DestroyFlag(_flagPreview);
            _flagPreview = null;
        }
        public void PreviewFlag(int x, int y, bool valid)
        {
            ClearFlagPreview();
            var origin = TilePos(x, y);
            _flagPreview = _w.Group("FlagPlacementPreview", Root, origin).gameObject;
            BuildTerritory(_flagPreview.transform, new FlagMapView { X = x, Y = y }, origin,
                valid ? new Color(.95f, .72f, .25f) : new Color(.9f, .25f, .2f));
        }
        public void SetTerritories(List<FlagMapView> flags, string ownAlliance, long nowMs)
        {
            var seen = new HashSet<string>();
            foreach (var flag in flags)
            {
                seen.Add(flag.Id);
                bool active = flag.Paid && nowMs >= flag.CompletesAtMs, mine = flag.AllianceId == ownAlliance;
                if (_flags.TryGetValue(flag.Id, out var old) && old.active == active && old.mine == mine) continue;
                if (old.root != null) DestroyFlag(old.root);
                var origin = TilePos(flag.X, flag.Y);
                var root = _w.Group("AllianceFlag", ChunkOf(flag.X, flag.Y), origin).gameObject;
                Color color = active ? (mine ? new Color(.2f, .75f, .65f) : new Color(.75f, .35f, .3f)) : new Color(.9f, .65f, .25f);
                _w.Prim(PrimitiveType.Cylinder, new Color(.3f, .27f, .22f), Vector3.up * .32f,
                    new Vector3(.025f, .32f, .025f), root.transform);
                _w.Prim(PrimitiveType.Cube, color, new Vector3(.10f, .54f, 0), new Vector3(.20f, .14f, .01f), root.transform);
                _w.Text(root.transform, Vector3.up * .74f, .022f, color).text = "[" + flag.Tag + "]";
                if (active) BuildTerritory(root.transform, flag, origin, color);
                _flags[flag.Id] = (root, active, mine);
            }
            var gone = new List<string>();
            foreach (var id in _flags.Keys) if (!seen.Contains(id)) gone.Add(id);
            foreach (var id in gone) { DestroyFlag(_flags[id].root); _flags.Remove(id); }
        }
        private void BuildTerritory(Transform parent, FlagMapView flag, Vector3 origin, Color color)
        {
            if (_territoryMaterial == null) _territoryMaterial = new Material(Shader.Find("Sprites/Default"));
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            foreach (int tile in AllianceTerritory.Coverage(flag.X, flag.Y))
            {
                var p = SurfaceLogic.FromTile(tile); var c = TilePos(p.x, p.y) - origin;
                c.y = .007f; float half = S * .49f; int i = vertices.Count;
                vertices.Add(c + new Vector3(-half, 0, -half)); vertices.Add(c + new Vector3(-half, 0, half));
                vertices.Add(c + new Vector3(half, 0, half)); vertices.Add(c + new Vector3(half, 0, -half));
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            var go = new GameObject("Territory") { layer = WorldScene.Layer }; go.transform.SetParent(parent, false);
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = _territoryMaterial;
            var block = new MaterialPropertyBlock(); color.a = .12f; block.SetColor("_Color", color); renderer.SetPropertyBlock(block);
        }
        private static void DestroyFlag(GameObject root)
        {
            var territory = root.transform.Find("Territory");
            if (territory != null) Object.Destroy(territory.GetComponent<MeshFilter>().sharedMesh);
            Object.Destroy(root);
        }
    }
}
