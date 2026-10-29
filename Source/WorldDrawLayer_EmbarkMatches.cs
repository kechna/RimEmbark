using System.Collections;
using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimEmbark
{
    public class WorldDrawLayer_EmbarkMatches : WorldDrawLayer
    {
        private static Material material;

        private readonly List<Vector3> verts = new List<Vector3>();

        private int drawnVersion = -1;

        private static Material Material => material ??= MaterialPool.MatFrom(BaseContent.WhiteTex,
            ShaderDatabase.WorldOverlayTransparent, new Color(0.25f, 0.95f, 0.35f, 0.4f), 3555);

        public override bool VisibleWhenLayerNotSelected => false;

        public override bool VisibleInBackground => false;

        public override bool Visible => base.Visible && EmbarkSearch.ShowOverlay && EmbarkSearch.Cache.layer == planetLayer;

        public override bool ShouldRegenerate => base.ShouldRegenerate || drawnVersion != EmbarkSearch.ResultVersion;

        public override IEnumerable Regenerate()
        {
            foreach (object item in base.Regenerate())
            {
                yield return item;
            }
            drawnVersion = EmbarkSearch.ResultVersion;
            foreach (int tileId in EmbarkSearch.Matches)
            {
                LayerSubMesh subMesh = GetSubMesh(Material);
                planetLayer.GetTileVertices(tileId, verts);
                int start = subMesh.verts.Count;
                int count = verts.Count;
                for (int i = 0; i < count; i++)
                {
                    subMesh.verts.Add(verts[i] + verts[i].normalized * 0.02f);
                    subMesh.uvs.Add(Vector2.zero);
                    if (i < count - 2)
                    {
                        subMesh.tris.Add(start + i + 2);
                        subMesh.tris.Add(start + i + 1);
                        subMesh.tris.Add(start);
                    }
                }
            }
            FinalizeMesh(MeshParts.All);
        }
    }
}
