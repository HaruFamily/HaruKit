namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class HaruGraphDraft
{
    private static string[] ValueComponents(Type type)
    {
        if (type == typeof(Vector2) || type == typeof(Vector2Int)) return new[] { "x", "y" };
        if (type == typeof(Vector3) || type == typeof(Vector3Int)) return new[] { "x", "y", "z" };
        if (type == typeof(Vector4)) return new[] { "x", "y", "z", "w" };
        if (type == typeof(Color) || type == typeof(Color32)) return new[] { "r", "g", "b", "a" };
        if (type == typeof(Rect) || type == typeof(RectInt)) return new[] { "x", "y", "width", "height" };
        return null;
    }

    private static bool SupportedValue(Type type) => type != null && (type == typeof(string) || type == typeof(bool)
        || type == typeof(int) || type == typeof(float) || type == typeof(double) || type.IsEnum || ValueComponents(type) != null);

    private static bool IntegerComponents(Type type) => type == typeof(Vector2Int) || type == typeof(Vector3Int)
        || type == typeof(RectInt) || type == typeof(Color32);

    private static object ValueFormat(Type type) => !SupportedValue(type) ? null : new
    {
        kind = ValueComponents(type) == null ? "scalar" : "object",
        components = ValueComponents(type),
        component_type = ValueComponents(type) == null ? null : type == typeof(Color32) ? "byte" : IntegerComponents(type) ? "int32" : "float32",
        exact_components = ValueComponents(type) != null,
    };

    // Explicit wire values avoid serializing Unity's computed properties (normalized, linear, etc.).
    public static object WireValue(object value) => value switch
    {
        Vector2 v => new { x = v.x, y = v.y },
        Vector3 v => new { x = v.x, y = v.y, z = v.z },
        Vector4 v => new { x = v.x, y = v.y, z = v.z, w = v.w },
        Vector2Int v => new { x = v.x, y = v.y },
        Vector3Int v => new { x = v.x, y = v.y, z = v.z },
        Color v => new { r = v.r, g = v.g, b = v.b, a = v.a },
        Color32 v => new { r = v.r, g = v.g, b = v.b, a = v.a },
        Rect v => new { x = v.x, y = v.y, width = v.width, height = v.height },
        RectInt v => new { x = v.x, y = v.y, width = v.width, height = v.height },
        Enum v => v.ToString(),
        _ => value,
    };

    public static bool TryValue(JToken token, Type type, out object value)
    {
        value = null;
        if (token == null || !SupportedValue(type)) return false;
        if (token.Type == JTokenType.Null) return type == typeof(string);
        if (type == typeof(string))
        {
            if (token.Type != JTokenType.String || ((string)token).Length > 16384) return false;
            value = (string)token;
            return true;
        }
        if (type.IsEnum)
        {
            if (token.Type != JTokenType.String || !Enum.GetNames(type).Contains((string)token)) return false;
            value = Enum.Parse(type, (string)token);
            return true;
        }
        string[] components = ValueComponents(type);
        if (components != null)
        {
            if (token is not JObject obj || obj.Count != components.Length || components.Any(name => obj[name] == null)) return false;
            Type numberType = IntegerComponents(type) ? typeof(int) : typeof(float);
            var values = new object[components.Length];
            for (int i = 0; i < components.Length; i++)
                if (!TryValue(obj[components[i]], numberType, out values[i])
                    || type == typeof(Color32) && ((int)values[i] < 0 || (int)values[i] > 255)) return false;
            if (type == typeof(Vector2)) value = new Vector2((float)values[0], (float)values[1]);
            else if (type == typeof(Vector3)) value = new Vector3((float)values[0], (float)values[1], (float)values[2]);
            else if (type == typeof(Vector4)) value = new Vector4((float)values[0], (float)values[1], (float)values[2], (float)values[3]);
            else if (type == typeof(Vector2Int)) value = new Vector2Int((int)values[0], (int)values[1]);
            else if (type == typeof(Vector3Int)) value = new Vector3Int((int)values[0], (int)values[1], (int)values[2]);
            else if (type == typeof(Color)) value = new Color((float)values[0], (float)values[1], (float)values[2], (float)values[3]);
            else if (type == typeof(Color32)) value = new Color32((byte)(int)values[0], (byte)(int)values[1], (byte)(int)values[2], (byte)(int)values[3]);
            else if (type == typeof(Rect)) value = new Rect((float)values[0], (float)values[1], (float)values[2], (float)values[3]);
            else if (type == typeof(RectInt)) value = new RectInt((int)values[0], (int)values[1], (int)values[2], (int)values[3]);
            return value != null;
        }
        if (!(type == typeof(bool) && token.Type == JTokenType.Boolean
            || type == typeof(int) && token.Type == JTokenType.Integer
            || (type == typeof(float) || type == typeof(double)) && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer))) return false;
        try { value = token.ToObject(type); }
        catch (Exception error) when (error is OverflowException || error is FormatException || error is ArgumentException)
        { return false; }
        return !(value is float f && (float.IsInfinity(f) || float.IsNaN(f))
            || value is double d && (double.IsInfinity(d) || double.IsNaN(d)));
    }
}
}
