using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Xml;
using AeroSpot.Core.Logging;

namespace AeroSpot.Worker.Services
{
    /// <summary>
    /// Parser nativo ad alte prestazioni per file 3D Manufacturing Format (.3mf).
    /// I file 3MF sono archivi ZIP contenenti la geometria triangolare in formato XML (3D/3dmodel.model).
    /// </summary>
    public static class ThreeMfLoader
    {
        private const int MaxTrianglesToLoad = 300_000;

        public static StlModel? Load(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                using var archive = ZipFile.OpenRead(filePath);

                // 1. Cerca il file modello principale standard "3D/3dmodel.model"
                ZipArchiveEntry? modelEntry = null;
                foreach (var entry in archive.Entries)
                {
                    if (string.Equals(entry.FullName, "3D/3dmodel.model", StringComparison.OrdinalIgnoreCase) ||
                        entry.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
                    {
                        modelEntry = entry;
                        break;
                    }
                }

                if (modelEntry == null)
                {
                    AppLogger.Warn($"ThreeMfLoader: Nessun file .model trovato nell'archivio 3MF '{filePath}'.");
                    return null;
                }

                using var modelStream = modelEntry.Open();
                return ParseModelXml(modelStream);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"ThreeMfLoader: Errore caricamento 3MF '{filePath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Tenta di estrarre l'anteprima thumbnail incorporata generata dagli slicer (es. Bambu Studio, PrusaSlicer, OrcaSlicer).
        /// </summary>
        public static BitmapSource? TryExtractThumbnail(string filePath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(filePath);
                ZipArchiveEntry? thumbEntry = null;
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith("thumbnail.png", StringComparison.OrdinalIgnoreCase) ||
                        entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        thumbEntry = entry;
                        break;
                    }
                }

                if (thumbEntry != null)
                {
                    using var stream = thumbEntry.Open();
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    ms.Position = 0;

                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch { }

            return null;
        }

        private static StlModel? ParseModelXml(Stream stream)
        {
            var positions = new Point3DCollection();
            var indices = new System.Windows.Media.Int32Collection();

            var currentObjectVertices = new List<Point3D>();
            int globalVertexOffset = 0;

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;

            var xmlSettings = new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                IgnoreComments = true,
                DtdProcessing = DtdProcessing.Ignore
            };

            using var reader = XmlReader.Create(stream, xmlSettings);

            bool inMesh = false;
            bool inVertices = false;
            bool inTriangles = false;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    string name = reader.LocalName.ToLowerInvariant();

                    if (name == "object")
                    {
                        currentObjectVertices.Clear();
                    }
                    else if (name == "mesh")
                    {
                        inMesh = true;
                    }
                    else if (name == "vertices" && inMesh)
                    {
                        inVertices = true;
                    }
                    else if (name == "vertex" && inVertices)
                    {
                        string? xAttr = reader.GetAttribute("x");
                        string? yAttr = reader.GetAttribute("y");
                        string? zAttr = reader.GetAttribute("z");

                        if (double.TryParse(xAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                            double.TryParse(yAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out double y) &&
                            double.TryParse(zAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                        {
                            var pt = new Point3D(x, y, z);
                            currentObjectVertices.Add(pt);
                            positions.Add(pt);

                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                            if (z < minZ) minZ = z;
                            if (z > maxZ) maxZ = z;
                        }
                    }
                    else if (name == "triangles" && inMesh)
                    {
                        inTriangles = true;
                    }
                    else if (name == "triangle" && inTriangles)
                    {
                        if (indices.Count / 3 >= MaxTrianglesToLoad) continue;

                        string? v1Attr = reader.GetAttribute("v1");
                        string? v2Attr = reader.GetAttribute("v2");
                        string? v3Attr = reader.GetAttribute("v3");

                        if (int.TryParse(v1Attr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v1) &&
                            int.TryParse(v2Attr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v2) &&
                            int.TryParse(v3Attr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v3))
                        {
                            indices.Add(globalVertexOffset + v1);
                            indices.Add(globalVertexOffset + v2);
                            indices.Add(globalVertexOffset + v3);
                        }
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    string name = reader.LocalName.ToLowerInvariant();
                    if (name == "vertices")
                    {
                        inVertices = false;
                    }
                    else if (name == "triangles")
                    {
                        inTriangles = false;
                    }
                    else if (name == "mesh")
                    {
                        inMesh = false;
                    }
                    else if (name == "object")
                    {
                        globalVertexOffset += currentObjectVertices.Count;
                        currentObjectVertices.Clear();
                    }
                }
            }

            if (indices.Count == 0 || positions.Count == 0)
            {
                return null;
            }

            int triangleCount = indices.Count / 3;

            // Calcolo normali vettoriali per shading 3D liscio
            var normals = new Vector3DCollection(positions.Count);
            for (int i = 0; i < positions.Count; i++)
            {
                normals.Add(new Vector3D(0, 0, 0));
            }

            for (int i = 0; i < indices.Count; i += 3)
            {
                int i0 = indices[i];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];

                if (i0 < positions.Count && i1 < positions.Count && i2 < positions.Count)
                {
                    Point3D p0 = positions[i0];
                    Point3D p1 = positions[i1];
                    Point3D p2 = positions[i2];

                    Vector3D u = p1 - p0;
                    Vector3D v = p2 - p0;
                    Vector3D norm = Vector3D.CrossProduct(u, v);
                    double len = norm.Length;
                    if (len > 1e-6)
                    {
                        norm = norm / len;
                        normals[i0] += norm;
                        normals[i1] += norm;
                        normals[i2] += norm;
                    }
                }
            }

            // Normalizza i vettori normali cumulati
            for (int i = 0; i < normals.Count; i++)
            {
                Vector3D n = normals[i];
                double len = n.Length;
                if (len > 1e-6)
                {
                    normals[i] = n / len;
                }
                else
                {
                    normals[i] = new Vector3D(0, 1, 0);
                }
            }

            var mesh = new MeshGeometry3D
            {
                Positions = positions,
                TriangleIndices = indices,
                Normals = normals
            };
            mesh.Freeze();

            double sizeX = maxX > minX ? maxX - minX : 0.0;
            double sizeY = maxY > minY ? maxY - minY : 0.0;
            double sizeZ = maxZ > minZ ? maxZ - minZ : 0.0;
            double maxDim = Math.Max(sizeX, Math.Max(sizeY, sizeZ));
            if (maxDim <= 0.001) maxDim = 1.0;

            var center = new Point3D((minX + maxX) / 2.0, (minY + maxY) / 2.0, (minZ + maxZ) / 2.0);

            return new StlModel
            {
                Mesh = mesh,
                TriangleCount = triangleCount,
                Center = center,
                SizeX = sizeX,
                SizeY = sizeY,
                SizeZ = sizeZ,
                MaxDimension = maxDim
            };
        }
    }
}
