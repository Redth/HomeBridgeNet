using System.Text;

namespace HomeBridge.Net.Build;

/// <summary>Minimal JSON writer — avoids a System.Text.Json dependency in the MSBuild task.</summary>
internal sealed class Json
{
    private readonly StringBuilder _sb = new();
    private int _indent;
    private bool _pendingComma;

    public override string ToString() => _sb.ToString();

    public Json Object(Action<Json> body)
    {
        Open('{');
        body(this);
        Close('}');
        return this;
    }

    /// <summary>Writes an object as a named member of the current object.</summary>
    public Json Object(string name, Action<Json> body)
    {
        Key(name);
        Open('{');
        body(this);
        Close('}');
        return this;
    }

    public Json Array(string name, Action<Json> body)
    {
        Key(name);
        Open('[');
        body(this);
        Close(']');
        return this;
    }

    public Json Prop(string name, string? value)
    {
        Key(name);
        _sb.Append(value is null ? "null" : Quote(value));
        _pendingComma = true;
        return this;
    }

    public Json Prop(string name, bool value)
    {
        Key(name);
        _sb.Append(value ? "true" : "false");
        _pendingComma = true;
        return this;
    }

    public Json Raw(string name, string rawValue)
    {
        Key(name);
        _sb.Append(rawValue);
        _pendingComma = true;
        return this;
    }

    /// <summary>Writes a bare string element inside an array.</summary>
    public Json Element(string value)
    {
        Separator();
        NewLineIndent();
        _sb.Append(Quote(value));
        _pendingComma = true;
        return this;
    }

    private void Key(string name)
    {
        Separator();
        NewLineIndent();
        _sb.Append(Quote(name)).Append(": ");
        _pendingComma = false;
    }

    private void Open(char c)
    {
        _sb.Append(c);
        _indent++;
        _pendingComma = false;
    }

    private void Close(char c)
    {
        _indent--;
        NewLineIndent();
        _sb.Append(c);
        _pendingComma = true;
    }

    private void Separator()
    {
        if (_pendingComma)
            _sb.Append(',');
    }

    private void NewLineIndent()
    {
        _sb.Append('\n');
        _sb.Append(' ', _indent * 2);
    }

    private static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
