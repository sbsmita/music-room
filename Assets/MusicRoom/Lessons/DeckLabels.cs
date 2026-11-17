// The deck's printed labels as ONE mesh (one draw): every word is laid out once by a temporary TextMeshPro (bold, letters
// stretched 5/3 wide), then its glyph quads are merged into a single mesh in the deck frame, lying on the ebony top
// (the same technique as Room's switch labels). Words can be hidden and shown (the CLEAR label swaps with "TAP AGAIN")
// by rewriting their vertex alpha: allocation-free, one upload per change, nothing else redrawn.
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MusicRoom.Lessons
{
    public sealed class DeckLabels
    {
        public struct Word
        {
            public string Text;
            /// <summary>Centre on the deck top (deck-local x, z).</summary>
            public float X, Z;
            /// <summary>Em in metres (the transport row 6 mm, the loop strip 4 mm).</summary>
            public float Em;
            /// <summary>Letter width (0 = Stretch, 5/3); the loop strip's small words are narrower to fit.</summary>
            public float Wide;
            public bool Hidden;
        }

        public const float Y = 0.0745f;            // just over the ebony top (0.074)
        public const float Stretch = 5f / 3f;      // a 256 x 128 label texture on a 4 x 1.2 cm quad
        public static readonly Color32 Ink = new Color32(0xe8, 0xe0, 0xd0, 0xff);

        readonly List<int> _start = new List<int>(32), _count = new List<int>(32);
        readonly List<string> _text = new List<string>(32);
        readonly List<Vector2> _centre = new List<Vector2>(32);
        readonly List<Vector2> _size = new List<Vector2>(32);
        Color32[] _colors;
        bool[] _shown;
        bool _dirty;

        public Mesh Mesh { get; private set; }
        public Material Material { get; private set; }
        public int WordCount => _text.Count;
        public string Text(int i) => _text[i];
        /// <summary>The word's laid-out centre (deck x, z) and extent (metres): tests check spacing and fit.</summary>
        public Vector2 Centre(int i) => _centre[i];
        public Vector2 Size(int i) => _size[i];
        public bool Shown(int i) => _shown != null && _shown[i];
        public int IndexOf(string text) { for (int i = 0; i < _text.Count; i++) if (_text[i] == text) return i; return -1; }

        /// <summary>Builds the merged mesh under `deck` (temporary TMP objects are made and destroyed here).</summary>
        public void Build(Transform deck, IList<Word> words)
        {
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            var verts = new List<Vector3>(512); var nrm = new List<Vector3>(512); var tan = new List<Vector4>(512);
            var cols = new List<Color32>(512); var uv0 = new List<Vector4>(512); var uv1 = new List<Vector4>(512);
            var tris = new List<int>(768);
            var tv = new List<Vector3>(); var tn = new List<Vector3>(); var tt = new List<Vector4>(); var tc = new List<Color32>();
            var tu0 = new List<Vector4>(); var tu1 = new List<Vector4>();
            var shown = new List<bool>(words.Count);
            for (int w = 0; w < words.Count; w++)
            {
                Word word = words[w];
                var go = new GameObject("Label " + word.Text);
                int b0 = verts.Count;
                Vector3 min = new Vector3(float.MaxValue, 0f, float.MaxValue), max = new Vector3(float.MinValue, 0f, float.MinValue);
                try
                {
                    go.transform.SetParent(deck, false);
                    go.transform.localPosition = new Vector3(word.X, Y, word.Z);
                    go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // faces up, text top away from the player
                    go.transform.localScale = new Vector3(word.Wide > 0f ? word.Wide : Stretch, 1f, 1f);
                    var tmp = go.AddComponent<TextMeshPro>();
                    if (font != null) tmp.font = font;
                    Material ??= tmp.fontSharedMaterial;
                    tmp.text = word.Text;
                    tmp.fontSize = word.Em * 10f;                   // TMP 3D: em = fontSize x 0.1 (scale 1)
                    tmp.fontStyle = FontStyles.Bold;
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.textWrappingMode = TextWrappingModes.NoWrap;
                    tmp.overflowMode = TextOverflowModes.Overflow;
                    tmp.color = Ink;
                    tmp.rectTransform.sizeDelta = new Vector2(0.1f, word.Em * 2f);
                    tmp.ForceMeshUpdate(true, true);
                    Mesh m = tmp.mesh;
                    if (m != null && m.vertexCount > 0)
                    {
                        Matrix4x4 toDeck = deck.worldToLocalMatrix * go.transform.localToWorldMatrix;
                        Matrix4x4 nTo = toDeck.inverse.transpose;
                        m.GetVertices(tv); m.GetNormals(tn); m.GetTangents(tt); m.GetColors(tc); m.GetUVs(0, tu0); m.GetUVs(1, tu1);
                        for (int v = 0; v < tv.Count; v++)
                        {
                            Vector3 p = toDeck.MultiplyPoint3x4(tv[v]);
                            min = Vector3.Min(min, p);
                            max = Vector3.Max(max, p);
                            verts.Add(p);
                            nrm.Add(v < tn.Count ? nTo.MultiplyVector(tn[v]).normalized : Vector3.up);
                            if (v < tt.Count) { Vector3 d = toDeck.MultiplyVector(tt[v]).normalized; tan.Add(new Vector4(d.x, d.y, d.z, tt[v].w)); }
                            else tan.Add(new Vector4(1f, 0f, 0f, 1f));
                            Color32 c = v < tc.Count ? tc[v] : Ink;
                            if (word.Hidden) c.a = 0;
                            cols.Add(c);
                            uv0.Add(v < tu0.Count ? tu0[v] : Vector4.zero);
                            uv1.Add(v < tu1.Count ? tu1[v] : Vector4.zero);
                        }
                        int[] t = m.triangles;
                        for (int k = 0; k < t.Length; k++) tris.Add(b0 + t[k]);
                    }
                }
                finally
                {
                    go.SetActive(false);
                    LessonUtil.Destroy(go);
                }
                _start.Add(b0);
                _count.Add(verts.Count - b0);
                _text.Add(word.Text);
                bool any = verts.Count > b0;
                _centre.Add(any ? new Vector2((min.x + max.x) * 0.5f, (min.z + max.z) * 0.5f) : new Vector2(word.X, word.Z));
                _size.Add(any ? new Vector2(max.x - min.x, max.z - min.z) : Vector2.zero);
                shown.Add(!word.Hidden);
            }
            _shown = shown.ToArray();
            if (verts.Count == 0) return;
            var mesh = new Mesh { name = "DeckLabels" };
            mesh.SetVertices(verts);
            mesh.SetNormals(nrm);
            mesh.SetTangents(tan);
            mesh.SetColors(cols);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.MarkDynamic();
            _colors = cols.ToArray();
            Mesh = mesh;
        }

        /// <summary>Show or hide a word (its glyphs' alpha). The change reaches the GPU on the next Upload.</summary>
        public void Show(int word, bool on)
        {
            if (_shown == null || word < 0 || word >= _shown.Length || _shown[word] == on) return;
            _shown[word] = on;
            if (_colors == null) return;
            byte a = on ? (byte)255 : (byte)0;
            int s = _start[word], e = s + _count[word];
            for (int i = s; i < e; i++) _colors[i].a = a;
            _dirty = true;
        }

        public void Upload()
        {
            if (!_dirty || Mesh == null) return;
            Mesh.SetColors(_colors, 0, _colors.Length, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds | UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices);
            _dirty = false;
        }
    }
}
