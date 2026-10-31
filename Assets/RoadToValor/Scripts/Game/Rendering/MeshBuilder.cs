using System.Collections.Generic;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Procedural low-poly mesh assembler (boxes, cylinders, cones, spheres, wedges...) with palette UVs.
    /// All placeholder models are built through this so they can be swapped for real assets later.
    /// </summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> Verts = new List<Vector3>(512);
        public readonly List<Vector3> Normals = new List<Vector3>(512);
        public readonly List<Vector2> Uvs = new List<Vector2>(512);
        public readonly List<int> Tris = new List<int>(1024);
        private Matrix4x4 _m = Matrix4x4.identity;

        public int VertexCount => Verts.Count;
        public void Clear() { Verts.Clear(); Normals.Clear(); Uvs.Clear(); Tris.Clear(); _m = Matrix4x4.identity; }

        public MeshBuilder SetMatrix(Matrix4x4 m) { _m = m; return this; }
        public MeshBuilder ResetMatrix() { _m = Matrix4x4.identity; return this; }

        private void AddVert(Vector3 p, Vector3 n, Vector2 uv)
        {
            Verts.Add(_m.MultiplyPoint3x4(p));
            Normals.Add(_m.MultiplyVector(n).normalized);
            Uvs.Add(uv);
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 uv)
        {
            int i = Verts.Count;
            AddVert(a, n, uv); AddVert(b, n, uv); AddVert(c, n, uv); AddVert(d, n, uv);
            Tris.Add(i); Tris.Add(i + 1); Tris.Add(i + 2);
            Tris.Add(i); Tris.Add(i + 2); Tris.Add(i + 3);
        }

        private void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 uv)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int i = Verts.Count;
            AddVert(a, n, uv); AddVert(b, n, uv); AddVert(c, n, uv);
            Tris.Add(i); Tris.Add(i + 1); Tris.Add(i + 2);
        }

        /// <summary>Axis-aligned box centred at c with size s, optionally rotated (Euler degrees).</summary>
        public MeshBuilder Box(Vector3 c, Vector3 s, int color, Vector3 euler = default(Vector3))
        {
            var saved = _m;
            _m = _m * Matrix4x4.TRS(c, Quaternion.Euler(euler), s);
            Vector2 uv = Palette.UV(color);
            Vector3 p0 = new Vector3(-0.5f, -0.5f, -0.5f), p1 = new Vector3(0.5f, -0.5f, -0.5f), p2 = new Vector3(0.5f, -0.5f, 0.5f), p3 = new Vector3(-0.5f, -0.5f, 0.5f);
            Vector3 p4 = new Vector3(-0.5f, 0.5f, -0.5f), p5 = new Vector3(0.5f, 0.5f, -0.5f), p6 = new Vector3(0.5f, 0.5f, 0.5f), p7 = new Vector3(-0.5f, 0.5f, 0.5f);
            Quad(p4, p7, p6, p5, Vector3.up, uv);
            Quad(p0, p1, p2, p3, Vector3.down, uv);
            Quad(p3, p2, p6, p7, Vector3.forward, uv);
            Quad(p1, p0, p4, p5, Vector3.back, uv);
            Quad(p0, p3, p7, p4, Vector3.left, uv);
            Quad(p2, p1, p5, p6, Vector3.right, uv);
            _m = saved;
            return this;
        }

        /// <summary>Box whose top face is shrunk (sloped armour, roofs, glacis).</summary>
        public MeshBuilder Wedge(Vector3 c, Vector3 s, float topScaleX, float topScaleZ, float topOffsetZ, int color, Vector3 euler = default(Vector3))
        {
            var saved = _m;
            _m = _m * Matrix4x4.TRS(c, Quaternion.Euler(euler), s);
            Vector2 uv = Palette.UV(color);
            float tx = 0.5f * topScaleX, tz = 0.5f * topScaleZ;
            Vector3 p0 = new Vector3(-0.5f, -0.5f, -0.5f), p1 = new Vector3(0.5f, -0.5f, -0.5f), p2 = new Vector3(0.5f, -0.5f, 0.5f), p3 = new Vector3(-0.5f, -0.5f, 0.5f);
            Vector3 p4 = new Vector3(-tx, 0.5f, -tz + topOffsetZ), p5 = new Vector3(tx, 0.5f, -tz + topOffsetZ), p6 = new Vector3(tx, 0.5f, tz + topOffsetZ), p7 = new Vector3(-tx, 0.5f, tz + topOffsetZ);
            Quad(p4, p7, p6, p5, Vector3.up, uv);
            Quad(p0, p1, p2, p3, Vector3.down, uv);
            QuadAuto(p3, p2, p6, p7, uv);
            QuadAuto(p1, p0, p4, p5, uv);
            QuadAuto(p0, p3, p7, p4, uv);
            QuadAuto(p2, p1, p5, p6, uv);
            _m = saved;
            return this;
        }

        private void QuadAuto(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uv)
        {
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            Quad(a, b, c, d, n, uv);
        }

        /// <summary>Cylinder along local Y, centred at c. radiusTop allows cones / tapers.</summary>
        public MeshBuilder Cylinder(Vector3 c, float radius, float height, int color, int segments = 10, Vector3 euler = default(Vector3), float radiusTop = -1f, bool caps = true)
        {
            if (radiusTop < 0f) radiusTop = radius;
            var saved = _m;
            _m = _m * Matrix4x4.TRS(c, Quaternion.Euler(euler), Vector3.one);
            Vector2 uv = Palette.UV(color);
            float h = height * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 b0 = d0 * radius + Vector3.down * h, b1 = d1 * radius + Vector3.down * h;
                Vector3 t0 = d0 * radiusTop + Vector3.up * h, t1 = d1 * radiusTop + Vector3.up * h;
                Vector3 n = ((d0 + d1) * 0.5f).normalized;
                int idx = Verts.Count;
                AddVert(b0, n, uv); AddVert(t0, n, uv); AddVert(t1, n, uv); AddVert(b1, n, uv);
                Tris.Add(idx); Tris.Add(idx + 1); Tris.Add(idx + 2);
                Tris.Add(idx); Tris.Add(idx + 2); Tris.Add(idx + 3);
                if (caps)
                {
                    if (radiusTop > 0.0001f) Tri(Vector3.up * h, t1, t0, uv);
                    if (radius > 0.0001f) Tri(Vector3.down * h, b0, b1, uv);
                }
            }
            _m = saved;
            return this;
        }

        /// <summary>Cylinder lying along local Z (barrels, logs, pipes).</summary>
        public MeshBuilder Tube(Vector3 c, float radius, float length, int color, int segments = 8, Vector3 extraEuler = default(Vector3), float radiusEnd = -1f)
        {
            return Cylinder(c, radius, length, color, segments, new Vector3(90f, 0f, 0f) + extraEuler, radiusEnd);
        }

        public MeshBuilder Cone(Vector3 baseCenter, float radius, float height, int color, int segments = 8)
            => Cylinder(baseCenter + Vector3.up * height * 0.5f, radius, height, color, segments, default(Vector3), 0f);

        /// <summary>Low-poly UV sphere (or ellipsoid via scale).</summary>
        public MeshBuilder Sphere(Vector3 c, Vector3 scale, int color, int rings = 6, int segments = 8, bool hemisphere = false)
        {
            var saved = _m;
            _m = _m * Matrix4x4.TRS(c, Quaternion.identity, scale);
            Vector2 uv = Palette.UV(color);
            int r0 = hemisphere ? rings / 2 : 0;
            for (int r = r0; r < rings; r++)
            {
                float v0 = r / (float)rings * Mathf.PI, v1 = (r + 1) / (float)rings * Mathf.PI;
                for (int s = 0; s < segments; s++)
                {
                    float u0 = s / (float)segments * Mathf.PI * 2f, u1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                    Vector3 a = SpherePt(v0, u0), b = SpherePt(v0, u1), cc = SpherePt(v1, u1), d = SpherePt(v1, u0);
                    int idx = Verts.Count;
                    AddVert(a, a, uv); AddVert(b, b, uv); AddVert(cc, cc, uv); AddVert(d, d, uv);
                    Tris.Add(idx); Tris.Add(idx + 2); Tris.Add(idx + 1);
                    Tris.Add(idx); Tris.Add(idx + 3); Tris.Add(idx + 2);
                }
            }
            _m = saved;
            return this;
        }

        // y = cos(v) so v=0 is the top; hemisphere uses the upper half (v in [0, pi/2]) when r0 = 0... flip below.
        private static Vector3 SpherePt(float v, float u) => new Vector3(Mathf.Sin(v) * Mathf.Cos(u), -Mathf.Cos(v), Mathf.Sin(v) * Mathf.Sin(u)) * 0.5f;

        /// <summary>Flat ring on the XZ plane (selection circles, markers).</summary>
        public MeshBuilder Ring(Vector3 c, float inner, float outer, int color, int segments = 32)
        {
            Vector2 uv = Palette.UV(color);
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Quad(c + d0 * inner, c + d1 * inner, c + d1 * outer, c + d0 * outer, Vector3.up, uv);
            }
            return this;
        }

        /// <summary>Flat quad facing up (flags use euler to stand up).</summary>
        public MeshBuilder Plane(Vector3 c, Vector2 size, int color, Vector3 euler = default(Vector3))
        {
            var saved = _m;
            _m = _m * Matrix4x4.TRS(c, Quaternion.Euler(euler), new Vector3(size.x, 1f, size.y));
            Vector2 uv = Palette.UV(color);
            Quad(new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f), Vector3.up, uv);
            Quad(new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f), Vector3.down, uv);
            _m = saved;
            return this;
        }

        /// <summary>Append another builder's geometry transformed by m (used to combine props into chunks).</summary>
        public void Append(MeshBuilder other, Matrix4x4 m)
        {
            int baseIdx = Verts.Count;
            for (int i = 0; i < other.Verts.Count; i++)
            {
                Verts.Add(m.MultiplyPoint3x4(other.Verts[i]));
                Normals.Add(m.MultiplyVector(other.Normals[i]).normalized);
                Uvs.Add(other.Uvs[i]);
            }
            for (int i = 0; i < other.Tris.Count; i++) Tris.Add(baseIdx + other.Tris[i]);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (Verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(Verts);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Tris, 0, true);
            return mesh;
        }
    }
}
