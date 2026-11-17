// The kit's construction against the instrument data of Docs/05-instruments.md (`drums.pieces`, `drums.stands`,
// `drums.kickExtras_unity`, `drums.kitBox`, `sticks`), measured from the reference kit: piece poses, stand tops and
// tripod feet, the kick's spurs and pedal, the whole kit's box, the sticks' rest X; plus mesh sanity (every front face
// points out), the played-part / grab-handle split, and the layout promise that the cymbals sit clear above
// every drum.
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Drums;
using NUnit.Framework;
using UnityEngine;

namespace MusicRoom.Tests.Drums
{
    public class DrumGeometryTests
    {
        // drums.pieces: quat_unity and headNormal_unity.
        static readonly Vector4[] PieceQuat =
        {
            new Vector4(-0.04998f, 0f, 0f, 0.99875f), new Vector4(-0.22311f, 0f, 0f, 0.97479f), new Vector4(-0.22311f, 0f, 0f, 0.97479f),
            new Vector4(-0.025f, 0f, 0f, 0.99969f), new Vector4(-0.70711f, 0f, 0f, 0.70711f), new Vector4(-0.12467f, 0f, 0f, 0.9922f),
            new Vector4(-0.05996f, 0f, 0f, 0.9982f), new Vector4(-0.17411f, 0f, 0f, 0.98473f), new Vector4(-0.14944f, 0f, 0f, 0.98877f),
        };
        static readonly Vector3[] HeadNormal =
        {
            new Vector3(0f, 0.995f, -0.0998f), new Vector3(0f, 0.9004f, -0.435f), new Vector3(0f, 0.9004f, -0.435f),
            new Vector3(0f, 0.9988f, -0.05f), new Vector3(0f, 0f, -1f), new Vector3(0f, 0.9689f, -0.2474f),
            new Vector3(0f, 0.9928f, -0.1197f), new Vector3(0f, 0.9394f, -0.3429f), new Vector3(0f, 0.9553f, -0.2955f),
        };
        // Rack toms and the pad as spaced on 2026-11-14 (DrumSpec.Pieces; the reference had tom1 (-.02, .86, .54), tom2
        // (.29, .85, .42), the pad (-.27, .6, .2), and their stands' tops and feet moved with them).
        static readonly Vector3[] PiecePos =
        {
            new Vector3(0f, 0.72f, 0.3f), new Vector3(-0.04f, 0.86f, 0.625f), new Vector3(0.32f, 0.85f, 0.482f), new Vector3(0.38f, 0.62f, 0.02f),
            new Vector3(0.12f, 0.29f, 0.7f), new Vector3(-0.29f, 0.6f, 0.19f), new Vector3(-0.42f, 0.8f, 0.36f), new Vector3(-0.39f, 1.02f, 0.58f),
            new Vector3(0.48f, 1.04f, 0.27f),
        };
        static readonly float[] PieceR = { 0.18f, 0.15f, 0.165f, 0.2f, 0.28f, 0.1f, 0.17f, 0.21f, 0.22f };

        // drums.stands (by piece id; the kick shell has none): poleTo_unity and the three legs_unity feet.
        static readonly int[] StandPiece = { 0, 1, 2, 3, 5, 6, 7, 8 };
        static readonly Vector3[] PoleTop =
        {
            new Vector3(0f, 0.636f, 0.3f), new Vector3(-0.04f, 0.74f, 0.625f), new Vector3(0.32f, 0.718f, 0.482f), new Vector3(0.38f, 0.404f, 0.02f),
            new Vector3(-0.29f, 0.56f, 0.19f), new Vector3(-0.42f, 0.77f, 0.36f), new Vector3(-0.39f, 0.99f, 0.58f), new Vector3(0.48f, 1.01f, 0.27f),
        };
        static readonly Vector3[,] Feet =
        {
            { new Vector3(0.2763f, 0.01f, 0.1832f), new Vector3(-0.2393f, 0.01f, 0.1191f), new Vector3(-0.037f, 0.01f, 0.5977f) },
            { new Vector3(0.2363f, 0.01f, 0.5082f), new Vector3(-0.2793f, 0.01f, 0.4441f), new Vector3(-0.077f, 0.01f, 0.9227f) },
            { new Vector3(0.5963f, 0.01f, 0.3652f), new Vector3(0.0807f, 0.01f, 0.3011f), new Vector3(0.283f, 0.01f, 0.7797f) },
            { new Vector3(0.6563f, 0.01f, -0.0968f), new Vector3(0.1407f, 0.01f, -0.1609f), new Vector3(0.343f, 0.01f, 0.3177f) },
            { new Vector3(-0.0137f, 0.01f, 0.0732f), new Vector3(-0.5293f, 0.01f, 0.0091f), new Vector3(-0.327f, 0.01f, 0.4877f) },
            { new Vector3(-0.1437f, 0.01f, 0.2432f), new Vector3(-0.6593f, 0.01f, 0.1791f), new Vector3(-0.457f, 0.01f, 0.6577f) },
            { new Vector3(-0.1137f, 0.01f, 0.4632f), new Vector3(-0.6293f, 0.01f, 0.3991f), new Vector3(-0.427f, 0.01f, 0.8777f) },
            { new Vector3(0.6641f, 0.01f, 0.0331f), new Vector3(0.1828f, 0.01f, 0.229f), new Vector3(0.5931f, 0.01f, 0.5479f) },
        };

        [Test]
        public void PiecesSitWhereTheReferencePutsThem()
        {
            using var k = new KitFixture();
            Assert.That(k.Kit.Pieces.Count, Is.EqualTo(9));
            for (int i = 0; i < 9; i++)
            {
                DrumPieceInfo info = k.Kit.Pieces[i];
                Assert.That(info.Id, Is.EqualTo(i));
                Assert.That(info.Radius, Is.EqualTo(PieceR[i]));
                Assert.That(Vector3.Distance(info.Transform.localPosition, PiecePos[i]), Is.LessThan(1e-6f), $"piece {i} position");
                // The JSON rounds to 5 digits: normalise before Quaternion.Angle (it reads 0.3 degrees off otherwise).
                var q = Quaternion.Normalize(new Quaternion(PieceQuat[i].x, PieceQuat[i].y, PieceQuat[i].z, PieceQuat[i].w));
                Assert.That(Quaternion.Angle(info.Transform.localRotation, q), Is.LessThan(0.01f), $"piece {i} rotation");
                Assert.That(Vector3.Distance(info.Transform.up, HeadNormal[i].normalized), Is.LessThan(2e-4f), $"piece {i} head normal");
            }
            Assert.That(k.Kit.Pieces[5].Kind, Is.EqualTo("kick"), "the pad plays and records as the kick");
            Assert.IsTrue(k.Kit.Pieces[5].Pad);
            Assert.That(k.Kit.Pieces[6].Type, Is.EqualTo(DrumPieceType.Cymbal));
            Assert.That(k.Kit.KickId, Is.EqualTo(4));
            Assert.That(k.Kit.KickPadId, Is.EqualTo(5));
            Assert.That(k.Kit.HatId, Is.EqualTo(6));
            Assert.IsFalse(k.Kit.HatOpen, "closed, the way a foot on the pedal keeps it");
        }

        [Test]
        public void StandsAndTheKickExtrasMatchTheJson()
        {
            for (int s = 0; s < StandPiece.Length; s++)
            {
                PieceSpec spec = DrumSpec.Pieces[StandPiece[s]];
                Assert.IsTrue(DrumSpec.HasStand(spec));
                Vector3 top = DrumSpec.StandTop(spec);
                Assert.That(Vector3.Distance(top, PoleTop[s]), Is.LessThan(1e-5f), $"{spec.Kind} pole top");
                Assert.That(Vector3.Distance(DrumSpec.LegHip(top), new Vector3(top.x, 0.32f, top.z)), Is.LessThan(1e-6f));
                for (int leg = 0; leg < 3; leg++)
                    Assert.That(Vector3.Distance(DrumSpec.LegFoot(top, leg, spec.LegTurnRad), Feet[s, leg]), Is.LessThan(2e-4f), $"{spec.Kind} foot {leg}");
            }
            Assert.IsFalse(DrumSpec.HasStand(DrumSpec.Pieces[DrumSpec.KickId]), "the kick stands on its spurs");
            // kickExtras_unity.
            Assert.That(DrumSpec.SpurA0, Is.EqualTo(new Vector3(0.37f, 0.06f, 0.9f)));
            Assert.That(DrumSpec.SpurB0, Is.EqualTo(new Vector3(0.52f, 0f, 0.95f)));
            Assert.That(DrumSpec.SpurA1, Is.EqualTo(new Vector3(-0.13f, 0.06f, 0.9f)));
            Assert.That(DrumSpec.SpurB1, Is.EqualTo(new Vector3(-0.28f, 0f, 0.95f)));
            Assert.That(DrumSpec.PedalPos, Is.EqualTo(new Vector3(0.12f, 0.02f, 0.54f)));
            Assert.That(DrumSpec.PedalPitchDeg, Is.EqualTo(0.25f * Mathf.Rad2Deg).Within(1e-3f));

            using var k = new KitFixture();
            // Each stand mesh reaches from the floor to its pole top and out to its feet.
            for (int s = 0; s < StandPiece.Length; s++)
            {
                PieceSpec spec = DrumSpec.Pieces[StandPiece[s]];
                Renderer r = Find(k, "stand-" + (spec.Pad ? "kick-pad" : spec.Kind));
                Bounds b = r.bounds;
                Assert.That(b.max.y, Is.EqualTo(PoleTop[s].y).Within(0.012f), $"{spec.Kind} stand height");
                Assert.That(b.min.y, Is.LessThan(0.01f));
                for (int leg = 0; leg < 3; leg++)
                {
                    Vector3 f = Feet[s, leg];
                    Assert.That(b.Contains(new Vector3(f.x, Mathf.Max(f.y, b.min.y + 1e-4f), f.z)) || b.SqrDistance(f) < 1e-4f, $"{spec.Kind} foot {leg} inside its stand");
                }
            }
        }

        static Renderer Find(KitFixture k, string name)
        {
            foreach (var r in k.Root.GetComponentsInChildren<Renderer>(true)) if (r.name == name) return r;
            Assert.Fail("no renderer " + name);
            return null;
        }

        [Test]
        public void TheWholeKitFillsTheJsonKitBox()
        {
            using var k = new KitFixture();
            bool any = false;
            var box = new Bounds();
            foreach (var r in k.Root.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { box = r.bounds; any = true; } else box.Encapsulate(r.bounds);
            }
            // drums.kitBox.unity: min (-.6651, -.013, -.2011), max (.7, 1.1203, 1.1067). The kick's shell ends 1.11 m out.
            Assert.That(box.min.x, Is.EqualTo(-0.6651f).Within(0.006f), "min x (the hi-hat's leg)");
            Assert.That(box.min.y, Is.EqualTo(-0.013f).Within(0.004f), "min y (the pedal's tilt)");
            Assert.That(box.min.z, Is.EqualTo(-0.2011f).Within(0.006f), "min z (the floor tom's lugs)");
            Assert.That(box.max.x, Is.EqualTo(0.7f).Within(0.006f), "max x (the ride)");
            Assert.That(box.max.y, Is.EqualTo(1.1203f).Within(0.006f), "max y (the ride's far edge)");
            Assert.That(box.max.z, Is.EqualTo(1.1067f).Within(0.006f), "max z (the kick's back rim)");
        }

        [Test]
        public void SticksRestInAnXAcrossTheSnare()
        {
            // 05 JSON sticks: restPos_unity / restQuat_unity / tip_unity / grip_unity.
            var pos = new[] { new Vector3(-0.068f, 0.7102f, 0.112f), new Vector3(0.068f, 0.7102f, 0.112f) };
            var quat = new[] { new Quaternion(0.66064f, 0.11681f, -0.12911f, 0.73024f), new Quaternion(0.66064f, -0.11681f, 0.12911f, 0.73024f) };
            var tip = new[] { new Vector3(0.0692f, 0.7477f, 0.4859f), new Vector3(-0.0692f, 0.7477f, 0.4859f) };
            var grip = new[] { new Vector3(-0.0234f, 0.7224f, 0.2335f), new Vector3(0.0234f, 0.7224f, 0.2335f) };
            using var k = new KitFixture();
            for (int i = 0; i < 2; i++)
            {
                Drumstick s = k.Sticks[i];
                Assert.That(Vector3.Distance(s.RestPos, pos[i]), Is.LessThan(2e-4f), $"stick {i} butt");
                Assert.That(Quaternion.Angle(s.RestRot, Quaternion.Normalize(quat[i])), Is.LessThan(0.01f), $"stick {i} rotation");
                Assert.That(Vector3.Distance(s.TipWorld, tip[i]), Is.LessThan(2e-4f), $"stick {i} tip");
                Assert.That(Vector3.Distance(s.GripWorld, grip[i]), Is.LessThan(2e-4f), $"stick {i} grip");
                Assert.That(s.Root.parent, Is.SameAs(k.Root.transform), "parented to the kit");
                Assert.IsFalse(s.TipPointer.Down || s.ShoulderPointer.Down, "resting sticks are silent");
                Assert.IsTrue(s.TipPointer.External && s.TipPointer.Stick && !s.TipPointer.Shoulder);
                Assert.IsTrue(s.ShoulderPointer.External && s.ShoulderPointer.Stick && s.ShoulderPointer.Shoulder);
                Assert.That(s.TipPointer.Owner, Is.SameAs(s));
                Assert.That(s.TipPointer.Id, Is.EqualTo("stick" + i));
                Assert.That(s.ShoulderPointer.Id, Is.EqualTo("stick" + i + "-shoulder"));
            }
            // The butts sit just over the near rim, the shafts cross over the head's centre.
            DrumPiece snare = k.Piece(DrumSpec.SnareId);
            for (int i = 0; i < 2; i++)
            {
                Vector3 butt = snare.Group.InverseTransformPoint(k.Sticks[i].ButtWorld);
                Assert.That(Mathf.Sqrt(butt.x * butt.x + butt.z * butt.z), Is.GreaterThan(0.18f).And.LessThan(0.21f), "butt over the rim");
                Assert.That(butt.y, Is.EqualTo(0.009f).Within(1e-4f), "9 mm over the head");
            }
            Vector3 c = snare.Group.position;
            float d = DrumMath.SegmentDistance(k.Sticks[0].ButtWorld, k.Sticks[0].TipWorld, c);
            Assert.That(d, Is.LessThan(0.02f), "the chord passes near the head's centre");
        }

        [Test]
        public void EveryMeshFacesOutward()
        {
            using var k = new KitFixture();
            var meshes = new List<Mesh>(k.Kit.Parts.Meshes);
            meshes.AddRange(k.Sticks.Meshes);
            Assert.That(meshes.Count, Is.GreaterThan(40), "per-part, static, click and stick meshes");
            foreach (var m in meshes)
            {
                var v = m.vertices; var n = m.normals; var t = m.triangles;
                Assert.That(n.Length, Is.EqualTo(v.Length), m.name);
                int bad = 0;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    if (Vector3.Dot(face, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]) < -1e-12f) bad++;
                }
                Assert.That(bad, Is.EqualTo(0), $"{m.name}: triangles whose front face points into the part");
            }
            // The cymbal's top surface faces up (a lathe facing into the plate would show only its underside).
            Mesh plate = Find(k, "plate").GetComponent<MeshFilter>().sharedMesh;
            var pv = plate.vertices; var pn = plate.normals;
            int upward = 0, onTop = 0;
            for (int i = 0; i < pv.Length; i++) if (pv[i].y > 0.005f) { onTop++; if (pn[i].y > 0f) upward++; }
            Assert.That(onTop, Is.GreaterThan(0));
            Assert.That(upward, Is.EqualTo(onTop), "the bell and bow face up");
        }

        [Test]
        public void HeadsCymbalsAndSticksArePlayedShellsAndStandsAreHandles()
        {
            using var k = new KitFixture();
            var played = new HashSet<Renderer>(k.Kit.PlayedParts);
            var handles = new HashSet<Renderer>(k.Kit.Parts.Handles);
            var statics = new HashSet<Renderer>(k.Kit.Parts.Static);
            var beater = new HashSet<Renderer>(k.Kit.Parts.BeaterRenderers);   // E7: decorative motion, neither played nor a handle
            Assert.That(beater.Count, Is.EqualTo(2), "the beater's chrome (shaft + footboard) and its felt ball");
            Assert.That(played.Count, Is.EqualTo(5 + 1 + 3 + 3 + 1 + 4), "5 batter heads, the pad's top, 3 plates, 3 nuts, the lower hi-hat, 4 stick parts");
            Assert.That(handles.Count, Is.EqualTo(5 * 3 + 2 + 8 + 3), "shell + resonant + chrome x 5, pad body + rim, 8 stands, 2 spurs + pedal");
            Assert.IsFalse(played.Overlaps(handles), "a played part is never a grab handle");
            Assert.IsFalse(statics.Overlaps(handles) || statics.Overlaps(played), "the kit-frame static meshes are neither");
            foreach (var r in k.Root.GetComponentsInChildren<Renderer>(true))
            {
                Assert.IsTrue(played.Contains(r) || handles.Contains(r) || statics.Contains(r) || beater.Contains(r), r.name + " is classified");
                // The Room module draws the kit's floor shadow (RigShadows): the kit builds none, so the floor is not
                // darkened twice.
                Assert.IsFalse(r.name.Contains("shadow"), r.name + ": no contact shadow under the kit");
            }
            var playedNames = new HashSet<string> { "head", "top", "plate", "nut", "lower", "wood", "tape" };
            var handleNames = new HashSet<string> { "shell", "resonant", "chrome", "body", "rim" };
            foreach (var r in played) Assert.IsTrue(playedNames.Contains(r.name), r.name + " is a played part");
            foreach (var r in handles) Assert.IsTrue(handleNames.Contains(r.name) || r.name.StartsWith("stand-") || r.name.StartsWith("kick-"), r.name + " is a handle");

            Rig rig = k.Kit.Rig;
            Assert.That(rig.Id, Is.EqualTo(RigId.Drums));
            Assert.That(rig.Priority, Is.EqualTo(1));
            Assert.That(rig.Group, Is.SameAs(k.Root.transform));
            Assert.That(rig.HomeAll, Is.EqualTo(new Vector3(0.2f, 0f, -0.26f)));
            Assert.That(rig.HomeYawAll, Is.EqualTo(90f));
            Assert.That(rig.HomeSingle, Is.EqualTo(new Vector3(0f, 0f, 0.1f)));
            Assert.That(rig.HomeYawSingle, Is.EqualTo(0f));
            Assert.That(rig.Instruments, Has.Member(k.Kit));
            // ARCHITECTURE.md section 8: Rig.Handles is filled once, the same list every read (Layout reads it per frame).
            Assert.That(rig.Handles, Is.SameAs(k.Kit.Parts.Handles));
            Assert.That(rig.Handles.Count, Is.EqualTo(handles.Count));
            foreach (var r in rig.Handles)
            {
                Assert.IsTrue(r.enabled && r.gameObject.activeInHierarchy, r.name + ": an enabled handle with live bounds");
                Assert.That(r.bounds.size.sqrMagnitude, Is.GreaterThan(0f), r.name + " bounds");
            }
        }

        [Test]
        public void StaticPartsDrawAsOneMeshPerMaterialInsideTheDrawBudget()
        {
            using var k = new KitFixture();
            // Every part that only moves with the kit is drawn by a kit-frame mesh; its own renderer keeps its bounds but
            // draws nothing (forceRenderingOff), so the kit's draws are the static meshes plus the moving parts.
            var drawing = new List<Renderer>();
            foreach (var r in k.Root.GetComponentsInChildren<Renderer>(true)) if (r.enabled && !r.forceRenderingOff) drawing.Add(r);
            Assert.That(k.Kit.Parts.Static.Count, Is.EqualTo(5), "chrome, red shell, pearl, resonant heads, rubber");
            Assert.That(drawing.Count, Is.EqualTo(5 + 6 + 4 + 4 + 2), "5 static meshes, 5 heads + pad top, 3 plates + lower hat, 4 stick parts, the E7 kick beater (chrome + felt)");
            Assert.That(drawing.Count, Is.LessThanOrEqualTo(25), "ARCHITECTURE.md section 17: drums <= 25 draws");
            foreach (var r in k.Kit.Parts.Handles) Assert.IsTrue(r.forceRenderingOff, r.name + " is drawn by its static mesh");
            foreach (var r in k.Kit.Parts.Played)
            {
                bool moves = r.name == "head" || r.name == "top" || r.name == "plate" || r.name == "lower";
                Assert.That(r.forceRenderingOff, Is.EqualTo(!moves), r.name + (moves ? " draws itself (flash / wobble / pedal)" : " is static"));
            }

            // The static meshes hold exactly the hidden parts' geometry, in the kit frame. Compare the tight boxes of the
            // vertices themselves: a tilted part's Renderer.bounds is the loose box of its rotated local box (up to 8 mm
            // bigger on the toms), while a combined mesh's box is tight.
            var hidden = new List<Renderer>();
            // E7: the kick pedal's footboard moves, so its handle is drawn by the beater's dynamic chrome mesh, not the static one.
            foreach (var r in k.Root.GetComponentsInChildren<Renderer>(true)) if (r.forceRenderingOff && r.name != "kick-pedal") hidden.Add(r);
            int hiddenVerts = 0, staticVerts = 0;
            Bounds hb = new Bounds(), sb = new Bounds();
            bool anyH = false, anyS = false;
            foreach (var r in hidden)
            {
                Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
                hiddenVerts += mesh.vertexCount;
                Grow(ref hb, ref anyH, mesh, r.transform.localToWorldMatrix);
            }
            foreach (var r in k.Kit.Parts.Static)
            {
                Assert.That(r.transform.parent, Is.SameAs(k.Root.transform));
                Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
                staticVerts += mesh.vertexCount;
                Grow(ref sb, ref anyS, mesh, r.transform.localToWorldMatrix);
            }
            Assert.That(staticVerts, Is.EqualTo(hiddenVerts));
            Assert.That(Vector3.Distance(sb.min, hb.min) + Vector3.Distance(sb.max, hb.max), Is.LessThan(1e-4f), "same place as the parts");
        }

        /// The tight world box of a mesh's vertices.
        static void Grow(ref Bounds b, ref bool any, Mesh mesh, Matrix4x4 toWorld)
        {
            foreach (var v in mesh.vertices)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(v);
                if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
            }
        }

        [Test]
        public void EveryHeadPadAndCymbalHasAClickDiscOnThePlaySurfaceLayer()
        {
            using var k = new KitFixture();
            for (int i = 0; i < DrumSpec.PieceCount; i++)
            {
                DrumPiece piece = k.Piece(i);
                Collider c = k.Kit.Parts.Clicks[i];
                Assert.IsNotNull(c, piece.Kind + " has a click disc");
                Assert.That(c.gameObject.layer, Is.EqualTo(Layers.PlaySurface), "section 0: desktop click targets are on layer 29");
                Assert.That(c.transform.parent, Is.SameAs(piece.Group), "follows the piece (not the wobbling plate)");
                Assert.That(k.Kit.PieceOf(c), Is.SameAs(piece));
                Mesh m = ((MeshCollider)c).sharedMesh;
                Assert.That(m.bounds.size.y, Is.EqualTo(DrumSpec.ClickThickness).Within(1e-5f), piece.Kind + " is a thin disc");
                Assert.That(m.bounds.extents.x, Is.EqualTo(piece.R).Within(1e-4f), piece.Kind + " of the piece's radius");
                Vector3 local = piece.Group.InverseTransformPoint(c.bounds.center);
                Assert.That(Mathf.Abs(local.y), Is.LessThan(1e-3f), piece.Kind + " about the strike plane");
            }
        }

        [Test]
        public void TheRigZoneIsTheKitsBoxPlusTwelveCentimetres()
        {
            using var k = new KitFixture();
            Bounds z = k.Kit.ZoneBounds();
            // The zone covers every mesh, the contact shadow included (1.9 x 1.6 m at (.1, .5)).
            Assert.That(z.min.x, Is.EqualTo(0.1f - 0.95f - 0.12f).Within(1e-3f));
            Assert.That(z.max.z, Is.EqualTo(0.5f + 0.8f + 0.12f).Within(1e-3f));
            Assert.That(z.max.y, Is.EqualTo(1.1203f + 0.12f).Within(0.007f));
            Assert.IsTrue(k.Kit.Rig.Zone.Contains(new Vector3(0f, 1.2f, 0.3f)), "over the snare");
            Assert.IsTrue(k.Kit.Rig.Zone.Contains(new Vector3(-0.9f, 0.5f, 0.3f)), "beside the hi-hat stand, inside the pad");
            Assert.IsFalse(k.Kit.Rig.Zone.Contains(new Vector3(-1.2f, 0.5f, 0.3f)));
            Assert.IsFalse(k.Kit.Rig.Zone.Contains(new Vector3(0f, 1.6f, 0.3f)));
        }

        [Test]
        public void CymbalsSitClearAboveEveryDrum()
        {
            // The kit's layout rule: "The crash and the ride sit above and clear of the drums ... from the seat it hides neither."
            using var k = new KitFixture();
            int[] drums = { DrumSpec.SnareId, DrumSpec.Tom1Id, DrumSpec.Tom2Id, DrumSpec.FloorId, DrumSpec.KickId, DrumSpec.KickPadId };
            int[] cymbals = { DrumSpec.HatId, DrumSpec.CrashId, DrumSpec.RideId };
            float worst = float.PositiveInfinity;
            string worstPair = null;
            foreach (int c in cymbals)
            {
                DrumPiece cym = k.Piece(c);
                var samples = new List<Vector3>();
                Sample(cym.Group, cym.R, 0f, false, samples);
                if (cym.IsHat)
                {
                    Sample(cym.Group, cym.R, DrumSpec.HatClosedY, true, samples);
                    Sample(cym.Group, cym.R, DrumSpec.HatOpenY, true, samples);
                }
                foreach (int d in drums)
                {
                    DrumPiece drum = k.Piece(d);
                    float reach = drum.R + DrumSpec.RimOffset + DrumSpec.RimTube + 0.005f;
                    foreach (var w in samples)
                    {
                        Vector3 l = drum.Group.InverseTransformPoint(w);
                        if (Mathf.Sqrt(l.x * l.x + l.z * l.z) > reach) continue;
                        if (l.y < worst) { worst = l.y; worstPair = $"{cym.Kind} over {drum.Kind}{(drum.Pad ? " pad" : "")}"; }
                    }
                }
            }
            Assert.That(worst, Is.GreaterThan(0.05f), $"a cymbal overhanging a drum stays 5 cm above its head plane (closest: {worstPair})");
        }

        /// Points over a cymbal plate (the lathe profile at 64 angles), in world space.
        static void Sample(Transform g, float r, float yOffset, bool flipped, List<Vector3> into)
        {
            Vector2[] profile = DrumSpec.CymbalProfile(r);
            for (int a = 0; a < 64; a++)
            {
                float phi = a / 64f * Mathf.PI * 2f;
                for (int j = 0; j < profile.Length; j++)
                {
                    float y = flipped ? -profile[j].y : profile[j].y;
                    float z = flipped ? -Mathf.Cos(phi) : Mathf.Cos(phi);
                    into.Add(g.TransformPoint(new Vector3(profile[j].x * Mathf.Sin(phi), y + yOffset, profile[j].x * z)));
                }
            }
        }

        [Test]
        public void TheLowerHiHatHangsUnderTheTopPlate()
        {
            using var k = new KitFixture();
            DrumPiece hat = k.Piece(DrumSpec.HatId);
            Mesh m = hat.Lower.GetComponent<MeshFilter>().sharedMesh;
            float highest = float.NegativeInfinity;
            foreach (var v in m.vertices) highest = Mathf.Max(highest, hat.Group.InverseTransformPoint(hat.Lower.TransformPoint(v)).y);
            Assert.That(highest, Is.EqualTo(-0.0145f).Within(1e-4f), "flipped edge at -.016 + .0015");
            Assert.That(Quaternion.Angle(hat.Lower.localRotation, Quaternion.Euler(180f, 0f, 0f)), Is.LessThan(1e-3f));
        }
    }
}
