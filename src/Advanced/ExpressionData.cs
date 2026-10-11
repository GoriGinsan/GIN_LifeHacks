using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace GinAdvanceHack
{
    // File identities are GUIDs. User-visible names never become filesystem paths.
    [Serializable, XmlRoot("GinExpression")]
    public sealed class ExpressionPreset
    {
        [XmlAttribute] public int Version = 1;
        [XmlAttribute] public string Id = Guid.NewGuid().ToString("N");
        [XmlAttribute] public string GroupId = "default";
        public string Name = "Expression";
        public string CharacterName = "";
        public string CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        [XmlArrayItem("Shape")] public List<ExpressionShape> Shapes = new List<ExpressionShape>();
        [XmlArrayItem("Bone")] public List<ExpressionBone> Bones = new List<ExpressionBone>();
    }

    [Serializable]
    public sealed class ExpressionShape
    {
        [XmlAttribute] public string RendererPath = "";
        [XmlAttribute] public string RendererName = "";
        [XmlAttribute] public string Role = "";
        [XmlAttribute] public string Name = "";
        [XmlAttribute] public float Weight;
    }

    [Serializable]
    public sealed class ExpressionBone
    {
        [XmlAttribute] public string Name = "";
        [XmlAttribute] public string Path = "";
        // P = 1, R = 2, S = 4; apply never writes an unrequested component.
        [XmlAttribute] public int Components;
        public ExpressionVector Position = new ExpressionVector();
        public ExpressionVector Scale = new ExpressionVector(1, 1, 1);
        public ExpressionRotation Rotation = new ExpressionRotation();
    }

    [Serializable]
    public sealed class ExpressionVector
    {
        [XmlAttribute] public float X;
        [XmlAttribute] public float Y;
        [XmlAttribute] public float Z;
        public ExpressionVector() { }
        public ExpressionVector(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    [Serializable]
    public sealed class ExpressionRotation
    {
        [XmlAttribute] public float X;
        [XmlAttribute] public float Y;
        [XmlAttribute] public float Z;
        [XmlAttribute] public float W = 1;
    }

    [Serializable, XmlRoot("GinExpressionGroups")]
    public sealed class ExpressionGroups
    {
        [XmlElement("Group")] public List<ExpressionGroup> Items = new List<ExpressionGroup>();
    }

    [Serializable]
    public sealed class ExpressionGroup
    {
        [XmlAttribute] public string Id = Guid.NewGuid().ToString("N");
        [XmlAttribute] public string Name = "Group";
    }

    internal static class ExpressionStorage
    {
        internal static bool ValidId(string id)
        {
            if (String.IsNullOrEmpty(id) || id.Length != 32) return false;
            for (int n = 0; n < id.Length; n++) if (!((id[n] >= '0' && id[n] <= '9') || (id[n] >= 'a' && id[n] <= 'f'))) return false;
            return true;
        }
        internal static string FilePath(string root, string id, string suffix)
        {
            if (!ValidId(id)) throw new IOException("Invalid expression file identity.");
            return Path.Combine(root, id + suffix);
        }
        internal static string PresetPath(string root, string id) { return FilePath(root, id, ".ginexpression.xml"); }
        internal static string ImagePath(string root, string id) { return FilePath(root, id, ".png"); }
        internal static void Write<T>(string path, T value)
        {
            string temporary = path + ".new-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) new XmlSerializer(typeof(T)).Serialize(stream, value);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static T Read<T>(string path)
        {
            FileInfo info = new FileInfo(path);
            if (info.Length > 4 * 1024 * 1024) throw new IOException("Expression file is too large.");
            XmlReaderSettings settings = new XmlReaderSettings(); settings.ProhibitDtd = true; settings.XmlResolver = null;
            using (XmlReader reader = XmlReader.Create(path, settings)) return (T)new XmlSerializer(typeof(T)).Deserialize(reader);
        }
        internal static int AllowedComponents(string name)
        {
            if (String.Equals(name, "cf_J_hitomi_tx_L", StringComparison.OrdinalIgnoreCase) || String.Equals(name, "cf_J_hitomi_tx_R", StringComparison.OrdinalIgnoreCase)) return 5;
            if (String.Equals(name, "cf_j_tang_01", StringComparison.OrdinalIgnoreCase)) return 7;
            if (String.Equals(name, "cf_j_tang_03", StringComparison.OrdinalIgnoreCase) || String.Equals(name, "cf_j_tang_04", StringComparison.OrdinalIgnoreCase) || String.Equals(name, "cf_j_tang_05", StringComparison.OrdinalIgnoreCase)) return 6;
            return 0;
        }
        internal static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        internal static bool Finite(ExpressionVector v) { return v != null && Finite(v.X) && Finite(v.Y) && Finite(v.Z); }
        internal static bool Finite(ExpressionRotation q)
        {
            if (q == null || !Finite(q.X) || !Finite(q.Y) || !Finite(q.Z) || !Finite(q.W)) return false;
            float length = q.X*q.X + q.Y*q.Y + q.Z*q.Z + q.W*q.W;
            return Finite(length) && length > 0.000001f;
        }
        internal static void Validate(ExpressionPreset value)
        {
            if (value == null || value.Version != 1 || !ValidId(value.Id) || String.IsNullOrEmpty(value.Name)) throw new IOException("Unsupported or incomplete expression.");
            if (value.Shapes == null || value.Bones == null || value.Shapes.Count > 8192 || value.Bones.Count > 6) throw new IOException("Invalid expression component count.");
            foreach (ExpressionBone bone in value.Bones)
            {
                int allowed = AllowedComponents(bone.Name);
                if (allowed == 0 || bone.Components == 0 || (bone.Components & ~allowed) != 0) throw new IOException("Expression contains an unsupported bone component: " + bone.Name);
                if (((bone.Components & 1) != 0 && !Finite(bone.Position)) || ((bone.Components & 2) != 0 && !Finite(bone.Rotation)) || ((bone.Components & 4) != 0 && !Finite(bone.Scale))) throw new IOException("Expression contains a non-finite bone value.");
            }
            foreach (ExpressionShape shape in value.Shapes)
                if (String.IsNullOrEmpty(shape.Name) || !Finite(shape.Weight) || (shape.Role != "Eyes / Mouth" && shape.Role != "Teeth" && shape.Role != "Eyebrows" && shape.Name != "kuti_ha.ha00_name_op")) throw new IOException("Expression contains an unsupported Blend Shape.");
        }
    }
}
