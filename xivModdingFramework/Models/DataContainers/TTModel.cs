﻿using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Model.Scene2D;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using xivModdingFramework.Cache;
using xivModdingFramework.Helpers;
using xivModdingFramework.Models.FileTypes;
using xivModdingFramework.Textures.Enums;
using static xivModdingFramework.Cache.XivCache;

namespace xivModdingFramework.Models.DataContainers
{

    /// <summary>
    /// Class representing a fully qualified, Square-Enix style Vertex.
    /// In SE's system, these values are all keyed to the same index value, 
    /// so none of them can be separated from the others without creating
    /// an entirely new vertex.
    /// </summary>
    public class TTVertex {
        public Vector3 Position = new Vector3(0,0,0);

        public Vector3 Normal = new Vector3(0, 0, 0);
        public Vector3 Binormal = new Vector3(0, 0, 0);
        public Vector3 Tangent = new Vector3(0, 0, 0);

        // This is Technically BINORMAL handedness in FFXIV.
        // A values of TRUE indicates we need to flip the Tangent when generated. (-1)
        public bool Handedness = false;

        public Vector2 UV1 = new Vector2(0, 0);
        public Vector2 UV2 = new Vector2(0, 0);

        // RGBA
        public byte[] VertexColor = new byte[] { 255, 255, 255, 255 };

        // BoneIds and Weights.  FFXIV Vertices can only be affected by a maximum of 4 bones.
        public byte[] BoneIds = new byte[4];
        public byte[] Weights = new byte[4];

        public static bool operator ==(TTVertex a, TTVertex b)
        {
            // Memberwise equality.
            if (a.Position != b.Position) return false;
            if (a.Normal != b.Normal) return false;
            if (a.Binormal != b.Binormal) return false;
            if (a.Handedness != b.Handedness) return false;
            if (a.UV1 != b.UV1) return false;
            if (a.UV2 != b.UV2) return false;

            for(var ci = 0; ci < 4; ci++)
            {
                if (a.VertexColor[ci] != b.VertexColor[ci]) return false;
                if (a.BoneIds[ci] != b.BoneIds[ci]) return false;
                if (a.Weights[ci] != b.Weights[ci]) return false;

            }

            return true;
        }

        public static bool operator !=(TTVertex a, TTVertex b)
        {
            return !(a == b);
        }

        public override bool Equals(object obj)
        {
            if (obj.GetType() != typeof(TTVertex)) return false;
            var b = (TTVertex)obj;
            return b == this;
        }
    }


    /// <summary>
    /// Class representing the base infromation for a Mesh Part, unrelated
    /// to the Item or anything else above the level of the base 3D model.
    /// </summary>
    public class TTMeshPart
    {
        // Purely semantic/not guaranteed to be unique.
        public string Name = null;

        // List of fully qualified TT/SE style vertices.
        public List<TTVertex> Vertices = new List<TTVertex>();

        // List of Vertex IDs that make up the triangles of the mesh.
        public List<int> TriangleIndices = new List<int>();

        // List of Attributes attached to this part.
        public HashSet<string> Attributes = new HashSet<string>();

    }

    /// <summary>
    /// Class representing a shape data part.
    /// A MeshGroup may have any amount of these, including
    /// multiple that have the same shape name.
    /// </summary>
    public class TTShapePart
    {
        /// <summary>
        /// The raw shp_ identifier.
        /// </summary>
        public string Name;

        /// <summary>
        /// The list of vertices this Shape introduces.
        /// </summary>
        public List<TTVertex> Vertices = new List<TTVertex>();

        /// <summary>
        /// Dictionary of [Mesh Level Index #] => [Shape Part Vertex # to replace that Index's Value with] 
        /// </summary>
        public Dictionary<int, int> Replacements = new Dictionary<int, int>();
    }

    /// <summary>
    /// Class representing a mesh group in TexTools
    /// At the FFXIV level, all the parts are crushed down together into one
    /// Singular 'Mesh'.
    /// </summary>
    public class TTMeshGroup
    {
        public List<TTMeshPart> Parts = new List<TTMeshPart>();

        /// <summary>
        /// Material used by this Mesh Group.
        /// </summary>
        public string Material;


        /// <summary>
        /// List of bones used by this mesh group's vertices.
        /// </summary>
        public List<string> Bones = new List<string>();


        public List<TTShapePart> ShapeParts = new List<TTShapePart>();

        /// <summary>
        /// Accessor for the full unified MeshGroup level Vertex list.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public TTVertex GetVertexAt(int id)
        {
            if (Parts.Count == 0)
                return null;

            var startingOffset = 0;
            TTMeshPart part = Parts[0];
            foreach(var p in Parts)
            {
                if(startingOffset + p.Vertices.Count < id)
                {
                    startingOffset += p.Vertices.Count;
                } else
                {
                    part = p;
                    break;
                }
            }

            var realId = id - startingOffset;
            return part.Vertices[realId];
        }

        /// <summary>
        /// Accessor for the full unified MeshGroup level Index list
        /// Also corrects the resultant Index to point to the MeshGroup level Vertex list.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public int GetIndexAt(int id)
        {
            if (Parts.Count == 0)
                return -1;

            var startingOffset = 0;
            var partId = 0;
            for(var i = 0; i < Parts.Count; i++)
            {
                var p = Parts[i];
                if (startingOffset + p.TriangleIndices.Count <= id)
                {
                    startingOffset += p.TriangleIndices.Count;
                }
                else
                {
                    partId = i;
                    break;
                }
            }
            var part = Parts[partId];
            var realId = id - startingOffset;
            var realVertexId = part.TriangleIndices[realId];

            var offsets = PartVertexOffsets;
            var modifiedVertexId = realVertexId + offsets[partId];
            return modifiedVertexId;

        }

        /// <summary>
        /// When stacked together, this is the list of points which the Triangle Index pointer would start for each part.
        /// </summary>
        public List<int> PartIndexOffsets
        {
            get
            {
                var list = new List<int>();
                var offset = 0;
                foreach (var p in Parts)
                {
                    list.Add(offset);
                    offset += p.TriangleIndices.Count;
                }
                return list;
            }
        }

        /// <summary>
        /// When stacked together, this is the list of points which the Vertex pointer would start for each part.
        /// </summary>
        public List<int> PartVertexOffsets
        {
            get
            {
                var list = new List<int>();
                var offset = 0;
                foreach (var p in Parts)
                {
                    list.Add(offset);
                    offset += p.Vertices.Count;
                }
                return list;
            }
        }

        public uint VertexCount
        {
            get
            {
                uint count = 0;
                foreach (var p in Parts)
                {
                    count += (uint)p.Vertices.Count;
                }
                return count;
            }
        }
        public uint IndexCount
        {
            get
            {
                uint count = 0;
                foreach (var p in Parts)
                {
                    count += (uint)p.TriangleIndices.Count;
                }
                return count;
            }
        }
    }


    /// <summary>
    /// Class representing the base information for a 3D Model, unrelated to the 
    /// item or anything else that it's associated with.  This should be writeable
    /// into the FFXIV file system with some calculation, but is primarly a class
    /// for I/O with importers/exporters, and should not contain information like
    /// padding bytes or unknown bytes unless this is data the end user can 
    /// manipulate to some effect.
    /// </summary>
    public class TTModel
    {
        /// <summary>
        /// The Mesh groups and parts of this mesh.
        /// </summary>
        public List<TTMeshGroup> MeshGroups = new List<TTMeshGroup>();

        /// <summary>
        /// Readonly list of bones that are used in this model.
        /// </summary>
        public List<string> Bones
        {
            get
            {
                var ret = new SortedSet<string>();
                foreach (var m in MeshGroups)
                {
                    foreach(var b in m.Bones)
                    {
                        ret.Add(b);
                    }
                }
                return ret.ToList();
            }
        }

        /// <summary>
        /// Readonly list of Materials used in this model.
        /// </summary>
        public List<string> Materials
        {
            get
            {
                var ret = new SortedSet<string>();
                foreach(var m in MeshGroups)
                {
                    if (m.Material != null)
                    {
                        ret.Add(m.Material);
                    }
                }
                return ret.ToList();
            }
        }

        /// <summary>
        /// Readonly list of attributes used by this model.
        /// </summary>
        public List<string> Attributes
        {
            get
            {
                var ret = new SortedSet<string>();
                foreach( var m in MeshGroups)
                {
                    foreach(var p in m.Parts)
                    {
                        foreach(var a in p.Attributes)
                        {
                            ret.Add(a);
                        }
                    }
                }
                return ret.ToList();
            }
        }


        /// <summary>
        /// Whether or not to write Shape data to the resulting MDL.
        /// </summary>
        public bool HasShapeData
        {
            get
            {
                return MeshGroups.Any(x => x.ShapeParts.Count > 0);
            }
        }
        
        /// <summary>
        /// List of all shape names used in the model.
        /// </summary>
        public List<string> ShapeNames
        {
            get
            {
                var shapes = new SortedSet<string>();
                foreach(var m in MeshGroups)
                {
                    foreach(var p in m.ShapeParts)
                    {
                        shapes.Add(p.Name);
                    }
                }
                return shapes.ToList();
            }
        }
        
        /// <summary>
        /// Total # of Shape Parts
        /// </summary>
        public short ShapePartCount
        {
            get
            {
                short sum = 0;
                foreach(var m in MeshGroups)
                {
                    sum += (short) m.ShapeParts.Count;
                }
                return sum;
            }
        }

        /// <summary>
        /// Total Shape Data (Index) Entries
        /// </summary>
        public short ShapeDataCount
        {
            get
            {
                short sum = 0;
                foreach (var m in MeshGroups)
                {
                    foreach(var p in m.ShapeParts)
                    {
                        sum += (short)p.Replacements.Count;
                    }
                }
                return sum;
            }
        }


        /// <summary>
        /// Per-Shape sum of parts; matches up by index to ShapeNames.
        /// </summary>
        /// <returns></returns>
        public List<short> ShapePartCounts
        {
            get
            {
                var counts = new List<short>(new short[ShapeNames.Count]);

                foreach (var m in MeshGroups)
                {
                    foreach (var p in m.ShapeParts)
                    {
                        var idx = ShapeNames.IndexOf(p.Name);
                        counts[idx]++;
                    }
                }
                return counts;
            }
        }

        /// <summary>
        /// List of all the Shape Parts in the mesh, grouped by Shape Name order.
        /// (Matches up with ShapePartCounts)
        /// </summary>
        public List<(TTShapePart Part, int MeshId)> ShapeParts
        {
            get
            {
                var byShape = new Dictionary<string, List<(TTShapePart Part, int MeshId)>>();

                var mIdx = 0;
                foreach (var m in MeshGroups)
                {
                    foreach (var p in m.ShapeParts)
                    {
                        if(!byShape.ContainsKey(p.Name))
                        {
                            byShape.Add(p.Name, new List<(TTShapePart Part, int MeshId)>());
                        }
                        byShape[p.Name].Add((p, mIdx));
                    }
                    mIdx++;
                }

                var ret = new List<(TTShapePart Part, int MeshId)>();
                foreach(var name in ShapeNames)
                {
                    ret.AddRange(byShape[name]);
                }
                return ret;
            }
        }

        /// <summary>
        /// Whether or not this Model actually has animation/weight data.
        /// </summary>
        public bool HasWeights
        {
            get
            {
                foreach (var m in MeshGroups)
                {
                    if (m.Bones.Count > 0)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Sum count of Vertices in this model.
        /// </summary>
        public uint VertexCount
        {
            get
            {
                uint count = 0;
                foreach (var m in MeshGroups)
                {
                    count += (uint)m.VertexCount;
                }
                return count;
            }
        }

        /// <summary>
        /// Sum count of Indices in this model.
        /// </summary>
        public uint IndexCount
        {
            get
            {
                uint count = 0;
                foreach (var m in MeshGroups)
                {
                    count += (uint)m.IndexCount;
                }
                return count;
            }
        }


        /// <summary>
        /// Creates a bone set from the model and group information.
        /// </summary>
        /// <param name="PartNumber"></param>
        public List<byte> GetBoneSet(int groupNumber)
        {
            var fullList = Bones;
            var partial = MeshGroups[groupNumber].Bones;

            var result = new List<byte>(new byte[128]);

            if(partial.Count > 64)
            {
                throw new InvalidDataException("Individual Mesh groups cannot reference more than 64 bones.");
            }

            // This is essential a translation table of [mesh group bone index] => [full model bone index]
            for (int i = 0; i < partial.Count; i++)
            {
                var b = BitConverter.GetBytes(((short) fullList.IndexOf(partial[i])));
                IOUtil.ReplaceBytesAt(result, b, i * 2);
            }

            result.AddRange(BitConverter.GetBytes(partial.Count));

            return result;
        }

        /// <summary>
        /// Gets the material index for a given group, based on model and group information.
        /// </summary>
        /// <param name="groupNumber"></param>
        /// <returns></returns>
        public short GetMaterialIndex(int groupNumber) {
            
            // Sanity check
            if (MeshGroups.Count <= groupNumber) return 0;

            var m = MeshGroups[groupNumber];

            
            short index = (short)Materials.IndexOf(m.Material);

            return index > 0 ? index : (short)0; 
        }

        /// <summary>
        /// Retrieves the bitmask value for a part's attributes, based on part and model settings.
        /// </summary>
        /// <param name="groupNumber"></param>
        /// <returns></returns>
        public uint GetAttributeBitmask(int groupNumber, int partNumber)
        {
            var allAttributes = Attributes;
            if(allAttributes.Count > 32)
            {
                throw new InvalidDataException("Models cannot have more than 32 total attributes.");
            }
            uint mask = 0;

            var partAttributes = MeshGroups[groupNumber].Parts[partNumber].Attributes;

            uint bit = 1;
            for(int i = 0; i < allAttributes.Count; i++)
            {
                var a = allAttributes[i];
                bit = (uint)1 << i;

                if(partAttributes.Contains(a))
                {
                    mask = (uint)(mask | bit);
                }
                
            }

            return mask;
        }

        /// <summary>
        /// Loads a TTModel file from a given SQLite3 DB filepath.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static TTModel LoadFromFile(string filePath, Action<bool, string> loggingFunction)
        {

            var connectionString = "Data Source=" + filePath + ";Pooling=False;";
            TTModel model = new TTModel();

            // Spawn a DB connection to do the raw queries.
            using (var db = new SQLiteConnection(connectionString))
            {
                db.Open();
                // Using statements help ensure we don't accidentally leave any connections open and lock the file handle.

                // Load Mesh Parts
                var query = "select * from parts order by mesh asc, part asc;";
                using (var cmd = new SQLiteCommand(query, db))
                {
                    using (var reader = new CacheReader(cmd.ExecuteReader()))
                    {
                        while (reader.NextRow())
                        {
                            var meshNum = reader.GetInt32("mesh");
                            var partNum = reader.GetInt32("part");

                            // Spawn mesh groups as needed.
                            while(model.MeshGroups.Count <= meshNum)
                            {
                                model.MeshGroups.Add(new TTMeshGroup());
                            }

                            // Spawn parts as needed.
                            while(model.MeshGroups[meshNum].Parts.Count <= partNum)
                            {
                                model.MeshGroups[meshNum].Parts.Add(new TTMeshPart());

                            }

                            model.MeshGroups[meshNum].Parts[partNum].Name = reader.GetString("name");
                        }
                    }
                }

                // Load Bones
                query = "select * from bones order by mesh asc, bone_id asc;";
                using (var cmd = new SQLiteCommand(query, db))
                {
                    using (var reader = new CacheReader(cmd.ExecuteReader()))
                    {
                        while (reader.NextRow())
                        {
                            var meshId = reader.GetInt32("mesh");
                            model.MeshGroups[meshId].Bones.Add(reader.GetString("name"));
                        }
                    }
                }

            }

            // Loop for each part, to populate their internal data structures.
            for (var mId = 0; mId < model.MeshGroups.Count; mId++)
            {
                var m = model.MeshGroups[mId];
                for (var pId = 0; pId < m.Parts.Count; pId++)
                {
                    var p = m.Parts[pId];
                    var where = new WhereClause();
                    var mWhere = new WhereClause();
                    mWhere.Column = "mesh";
                    mWhere.Value = mId;
                    var pWhere = new WhereClause();
                    pWhere.Column = "part";
                    pWhere.Value = pId;

                    where.Inner.Add(mWhere);
                    where.Inner.Add(pWhere);

                    // Load Vertices
                    // The reader handles coalescing the null types for us.
                    p.Vertices = BuildListFromTable(connectionString, "vertices", where, async (reader) =>
                    {
                        var vertex = new TTVertex();

                        // Positions
                        vertex.Position.X = reader.GetFloat("position_x");
                        vertex.Position.Y = reader.GetFloat("position_y");
                        vertex.Position.Z = reader.GetFloat("position_z");

                        // Normals
                        vertex.Normal.X = reader.GetFloat("normal_x");
                        vertex.Normal.Y = reader.GetFloat("normal_y");
                        vertex.Normal.Z = reader.GetFloat("normal_z");

                        // Vertex Colors - Vertex color is RGBA
                        vertex.VertexColor[0] = (byte)(Math.Round(reader.GetFloat("color_r") * 255));
                        vertex.VertexColor[1] = (byte)(Math.Round(reader.GetFloat("color_g") * 255));
                        vertex.VertexColor[2] = (byte)(Math.Round(reader.GetFloat("color_b") * 255));
                        vertex.VertexColor[3] = (byte)(Math.Round(reader.GetFloat("color_a") * 255));

                        // UV Coordinates
                        vertex.UV1.X = reader.GetFloat("uv_1_u");
                        vertex.UV1.Y = reader.GetFloat("uv_1_v");
                        vertex.UV2.X = reader.GetFloat("uv_2_u");
                        vertex.UV2.Y = reader.GetFloat("uv_2_v");

                        // Bone Ids
                        vertex.BoneIds[0] = (byte)(reader.GetByte("bone_1_id"));
                        vertex.BoneIds[1] = (byte)(reader.GetByte("bone_2_id"));
                        vertex.BoneIds[2] = (byte)(reader.GetByte("bone_3_id"));
                        vertex.BoneIds[3] = (byte)(reader.GetByte("bone_4_id"));

                        // Weights
                        vertex.Weights[0] = (byte)(Math.Round(reader.GetFloat("bone_1_weight") * 255));
                        vertex.Weights[1] = (byte)(Math.Round(reader.GetFloat("bone_2_weight") * 255));
                        vertex.Weights[2] = (byte)(Math.Round(reader.GetFloat("bone_3_weight") * 255));
                        vertex.Weights[3] = (byte)(Math.Round(reader.GetFloat("bone_4_weight") * 255));

                        return vertex;
                    }).GetAwaiter().GetResult();

                    p.TriangleIndices = BuildListFromTable(connectionString, "indices", where, async (reader) =>
                    {
                        try
                        {
                            return reader.GetInt32("vertex_id");
                        } catch(Exception ex)
                        {
                            throw ex;
                        }
                    }).GetAwaiter().GetResult();
                }
            }


            return model;
        }
    }
}