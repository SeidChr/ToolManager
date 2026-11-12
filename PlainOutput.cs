namespace ToolManager;

// State is retained across chunks because terminal escape sequences can span pipe reads.
internal sealed class PlainOutput
{
    private State state;

    private bool afterCr;

    private enum State
    {
        Text,
        Escape,
        Csi,
        String,
        StringEscape,
        EscapeIntermediate,
    }

    public string Filter(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (this.state)
            {
                case State.Text:
                    if (c == '\x1b')
                    {
                        this.state = State.Escape;
                        break;
                    }

                    if (c == '\u009b')
                    {
                        this.state = State.Csi;
                        break;
                    }

                    if (c is '\u009d' or '\u0090' or '\u009e' or '\u009f')
                    {
                        this.state = State.String;
                        break;
                    }

                    if (c == '\r')
                    {
                        result.Append('\n');
                        this.afterCr = true;
                        break;
                    }

                    if (c == '\n')
                    {
                        if (!this.afterCr)
                        {
                            result.Append(c);
                        }

                        this.afterCr = false;
                        break;
                    }

                    this.afterCr = false;
                    if (c == '\t' || !char.IsControl(c))
                    {
                        result.Append(c);
                    }

                    break;
                case State.Escape:
                    this.state = c switch
                    {
                        '[' => State.Csi,
                        ']' => State.String,
                        'P' => State.String,
                        '^' => State.String,
                        '_' => State.String,
                        >= ' ' and <= '/' => State.EscapeIntermediate,
                        _ => State.Text,
                    };
                    break;
                case State.Csi:
                    if (c is >= '@' and <= '~')
                    {
                        this.state = State.Text;
                    }
                    else if (c == '\x1b')
                    {
                        this.state = State.Escape;
                    }

                    break;
                case State.String:
                    if (c is '\a' or '\u009c')
                    {
                        this.state = State.Text;
                    }
                    else if (c == '\x1b')
                    {
                        this.state = State.StringEscape;
                    }

                    break;
                case State.StringEscape:
                    this.state = c == '\\' ? State.Text : c == '\x1b' ? State.StringEscape : State.String;
                    break;
                case State.EscapeIntermediate:
                    if (c is >= '0' and <= '~')
                    {
                        this.state = State.Text;
                    }

                    break;
            }
        }

        return result.ToString();
    }
}
