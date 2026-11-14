using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace InstallerAssistant.AR
{
    /// <summary>
    /// What the AR view did, one JSON object per line (package B), so a phone test leaves records that can be
    /// measured afterwards (ml/tools/ar_records.py). Written to persistentDataPath/ar-records/&lt;start time&gt;.jsonl;
    /// on Android: adb pull /sdcard/Android/data/&lt;package&gt;/files/ar-records. Holds geometry only (poses,
    /// matrices, image points, where marks were drawn) and the names of screenshots; no camera image.
    /// The perception service's frame log (IA_SAVE_FRAMES) keeps the images under the same frame ids.
    /// </summary>
    public sealed class ArRecords
    {
        public readonly string Folder, File;
        StreamWriter writer;
        readonly StringBuilder sb = new StringBuilder(2048);
        bool first;

        ArRecords(string folder, string file, StreamWriter w) { Folder = folder; File = file; writer = w; }

        /// <summary>Opens a new record file, or returns null (and logs why) when it cannot.</summary>
        public static ArRecords Open()
        {
            try
            {
                string folder = Path.Combine(Application.persistentDataPath, "ar-records");
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".jsonl");
                var w = new StreamWriter(file, true, new UTF8Encoding(false)) { AutoFlush = true };
                Debug.Log("[ArRecords] writing " + file);
                return new ArRecords(folder, file, w);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ArRecords] no AR records: " + e.Message);
                return null;
            }
        }

        // ------------------------------------------------------------------ a tiny JSON line writer

        public ArRecords Begin(string kind)
        {
            sb.Length = 0;
            sb.Append("{\"kind\":\"").Append(kind).Append("\",\"t\":");
            Num(Time.unscaledTimeAsDouble);
            sb.Append(",\"clock\":\"").Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append('"');
            return this;
        }

        public ArRecords Field(string name) { sb.Append(",\"").Append(name).Append("\":"); return this; }
        public ArRecords Put(string name, string value) { Field(name); Str(value); return this; }
        public ArRecords Put(string name, double value) { Field(name); Num(value); return this; }
        public ArRecords Put(string name, long value) { Field(name); sb.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }
        public ArRecords Put(string name, bool value) { Field(name); sb.Append(value ? "true" : "false"); return this; }
        public ArRecords Put(string name, Vector3 v) { Field(name); Vec(v); return this; }
        public ArRecords Put(string name, Matrix4x4 m)
        {
            Field(name); sb.Append('[');
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++) { if (r + c > 0) sb.Append(','); Num(m[r, c]); }   // row-major
            sb.Append(']');
            return this;
        }
        public ArRecords Put(string name, float[] values)
        {
            Field(name);
            if (values == null) { sb.Append("null"); return this; }
            sb.Append('[');
            for (int i = 0; i < values.Length; i++) { if (i > 0) sb.Append(','); Num(values[i]); }
            sb.Append(']');
            return this;
        }

        /// <summary>Starts an array of objects; each Item() opens the next one, EndArray() closes it.</summary>
        public ArRecords Array(string name) { Field(name); sb.Append('['); first = true; return this; }
        public ArRecords Item() { if (!first) sb.Append("},"); first = false; sb.Append("{\"_\":0"); return this; }
        public ArRecords EndArray() { if (!first) sb.Append('}'); sb.Append(']'); first = false; return this; }

        public void End()
        {
            sb.Append('}');
            try { writer?.WriteLine(sb.ToString()); }
            catch (Exception e) { Debug.LogWarning("[ArRecords] write failed, records stop: " + e.Message); Close(); }
        }

        public void Close()
        {
            try { writer?.Dispose(); } catch (Exception) { }
            writer = null;
        }

        void Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) { sb.Append("null"); return; }
            sb.Append(v.ToString("0.######", CultureInfo.InvariantCulture));
        }

        void Vec(Vector3 v) { sb.Append('['); Num(v.x); sb.Append(','); Num(v.y); sb.Append(','); Num(v.z); sb.Append(']'); }

        void Str(string s)
        {
            sb.Append('"');
            foreach (char ch in s ?? "")
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                else sb.Append(ch);
            }
            sb.Append('"');
        }
    }
}
