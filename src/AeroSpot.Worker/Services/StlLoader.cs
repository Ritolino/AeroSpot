using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AeroSpot.Core.Logging;

namespace AeroSpot.Worker.Services
{
    public sealed class StlModel
    {
        public MeshGeometry3D Mesh { get; set; } = new MeshGeometry3D();
        public int TriangleCount { get; set; }
        public Point3D Center { get; set; }
        public double SizeX { get; set; }
        public double SizeY { get; set; }
        public double SizeZ { get; set; }
        public double MaxDimension { get; set; }
    }

    public static class StlLoader
    {
        private const int MaxTrianglesToLoad = 300_000;

        public static StlModel? Load(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length < 84) return null;

                bool isBinary = IsBinaryStl(stream);
                stream.Position = 0;

                return isBinary ? LoadBinary(stream) : LoadAscii(stream);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Caricamento STL fallito per '{filePath}': {ex.Message}");
                return null;
            }
        }

        private static bool IsBinaryStl(Stream stream)
        {
            if (stream.Length < 84) return false;

            byte[] header = new byte[84];
            int read = stream.Read(header, 0, 84);
            if (read < 84) return false;

            uint numTriangles = BitConverter.ToUInt32(header, 80);
            long expectedBinaryLength = 84L + (long)numTriangles * 50L;

            // Se la lunghezza coincide esattamente con la specifica binaria STL
            if (stream.Length == expectedBinaryLength) return true;

            // Se inizia con "solid", controlliamo se i primi caratteri contengono parole ASCII
            string start = Encoding.ASCII.GetString(header, 0, Math.Min(80, header.Length));
            if (start.StartsWith("solid", StringComparison.OrdinalIgnoreCase))
            {
                // Se la lunghezza del file non corrisponde alla formula binaria e inizia con "solid", è ASCII
                return false;
            }

            // Altrimenti, trattalo come binario
            return true;
        }

        private static StlModel? LoadBinary(Stream stream)
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            stream.Seek(80, SeekOrigin.Begin);

            uint triangleCount = reader.ReadUInt32();
            if (triangleCount == 0) return null;

            int loadCount = (int)Math.Min(triangleCount, MaxTrianglesToLoad);

            var positions = new Point3DCollection(loadCount * 3);
            var indices = new Int32Collection(loadCount * 3);
            var normals = new Vector3DCollection(loadCount * 3);

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;

            int currentIndex = 0;

            for (int i = 0; i < loadCount; i++)
            {
                // Normale (3x float32)
                float nx = reader.ReadSingle();
                float ny = reader.ReadSingle();
                float nz = reader.ReadSingle();
                var normal = new Vector3D(nx, ny, nz);

                // Vertice 1 (3x float32)
                float v1x = reader.ReadSingle();
                float v1y = reader.ReadSingle();
                float v1z = reader.ReadSingle();

                // Vertice 2 (3x float32)
                float v2x = reader.ReadSingle();
                float v2y = reader.ReadSingle();
                float v2z = reader.ReadSingle();

                // Vertice 3 (3x float32)
                float v3x = reader.ReadSingle();
                float v3y = reader.ReadSingle();
                float v3z = reader.ReadSingle();

                // 2 byte attributi
                reader.ReadUInt16();

                var p1 = new Point3D(v1x, v1y, v1z);
                var p2 = new Point3D(v2x, v2y, v2z);
                var p3 = new Point3D(v3x, v3y, v3z);

                // Aggiorna Bounding Box
                UpdateBounds(p1, ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
                UpdateBounds(p2, ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
                UpdateBounds(p3, ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);

                positions.Add(p1);
                positions.Add(p2);
                positions.Add(p3);

                normals.Add(normal);
                normals.Add(normal);
                normals.Add(normal);

                indices.Add(currentIndex++);
                indices.Add(currentIndex++);
                indices.Add(currentIndex++);
            }

            positions.Freeze();
            indices.Freeze();
            normals.Freeze();

            var mesh = new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = indices,
                Normals = normals
            };
            mesh.Freeze();

            double sx = Math.Max(0.001, maxX - minX);
            double sy = Math.Max(0.001, maxY - minY);
            double sz = Math.Max(0.001, maxZ - minZ);
            double maxDim = Math.Max(sx, Math.Max(sy, sz));

            return new StlModel
            {
                Mesh = mesh,
                TriangleCount = (int)triangleCount,
                Center = new Point3D((minX + maxX) / 2.0, (minY + maxY) / 2.0, (minZ + maxZ) / 2.0),
                SizeX = sx,
                SizeY = sy,
                SizeZ = sz,
                MaxDimension = maxDim
            };
        }

        private static StlModel? LoadAscii(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: true, bufferSize: 65536, leaveOpen: true);

            var positions = new Point3DCollection();
            var indices = new Int32Collection();
            var normals = new Vector3DCollection();

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;

            string? line;
            Vector3D currentNormal = new Vector3D(0, 0, 1);
            int triangleCount = 0;
            int currentIndex = 0;

            var currentVertices = new List<Point3D>(3);

            while ((line = reader.ReadLine()) != null && triangleCount < MaxTrianglesToLoad)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("facet normal", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4 &&
                        float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float nx) &&
                        float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float ny) &&
                        float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float nz))
                    {
                        currentNormal = new Vector3D(nx, ny, nz);
                    }
                    currentVertices.Clear();
                }
                else if (trimmed.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4 &&
                        float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float vx) &&
                        float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float vy) &&
                        float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float vz))
                    {
                        var pt = new Point3D(vx, vy, vz);
                        currentVertices.Add(pt);
                        UpdateBounds(pt, ref minX, ref maxX, ref minY, ref maxY, ref minZ, ref maxZ);
                    }
                }
                else if (trimmed.StartsWith("endfacet", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentVertices.Count == 3)
                    {
                        positions.Add(currentVertices[0]);
                        positions.Add(currentVertices[1]);
                        positions.Add(currentVertices[2]);

                        normals.Add(currentNormal);
                        normals.Add(currentNormal);
                        normals.Add(currentNormal);

                        indices.Add(currentIndex++);
                        indices.Add(currentIndex++);
                        indices.Add(currentIndex++);

                        triangleCount++;
                    }
                    currentVertices.Clear();
                }
            }

            if (triangleCount == 0) return null;

            positions.Freeze();
            indices.Freeze();
            normals.Freeze();

            var mesh = new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = indices,
                Normals = normals
            };
            mesh.Freeze();

            double sx = Math.Max(0.001, maxX - minX);
            double sy = Math.Max(0.001, maxY - minY);
            double sz = Math.Max(0.001, maxZ - minZ);
            double maxDim = Math.Max(sx, Math.Max(sy, sz));

            return new StlModel
            {
                Mesh = mesh,
                TriangleCount = triangleCount,
                Center = new Point3D((minX + maxX) / 2.0, (minY + maxY) / 2.0, (minZ + maxZ) / 2.0),
                SizeX = sx,
                SizeY = sy,
                SizeZ = sz,
                MaxDimension = maxDim
            };
        }

        private static void UpdateBounds(Point3D p, ref double minX, ref double maxX, ref double minY, ref double maxY, ref double minZ, ref double maxZ)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.Z > maxZ) maxZ = p.Z;
        }
    }
}
