namespace ToolManager.Execution;

public sealed class RunRecord
{
    private readonly StringBuilder buffer = new();

    private bool truncated;
    private int outputVersion;
    private DateTime? ended;
    private DateTime? stopDeadline;
    private string state = "Starting";
    private string customStatus = string.Empty;

    public event EventHandler? Changed;

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string ToolId { get; init; } = string.Empty;
    public string Action { get; init; } = "Run";
    public DateTime Started { get; } = DateTime.Now;

    public DateTime? Ended
    {
        get => this.ended;
        set => this.Set(ref this.ended, value);
    }

    public string State
    {
        get => this.state;
        set => this.Set(ref this.state, value);
    }

    public int? ExitCode
    { get; set; }

    public DateTime? StopDeadline
    {
        get => this.stopDeadline;
        set => this.Set(ref this.stopDeadline, value);
    }

    public string CustomStatus
    {
        get => this.customStatus;
        internal set => this.Set(ref this.customStatus, value);
    }

    public string DisplayStatus => this.Active && this.State == "Running" && !this.StopRequested && this.CustomStatus.Length > 0
        ? this.CustomStatus
        : this.State;

    public bool StopRequested
    { get; set; }

    public bool Interactive
    { get; init; }

    public bool Active => this.Ended is null;

    public int Length
    {
        get
        {
            lock (this.buffer)
            {
                return this.buffer.Length;
            }
        }
    }

    public string Output
    {
        get
        {
            lock (this.buffer)
            {
                return (this.truncated ? "[Earlier output discarded: retention limit reached]\n" : string.Empty) + this.buffer;
            }
        }
    }

    internal int OutputVersion => Volatile.Read(ref this.outputVersion);

    public void Append(string value, int maxChars)
    {
        lock (this.buffer)
        {
            if (value.Length > maxChars)
            {
                value = value[^maxChars..];
                this.truncated = true;
            }

            this.buffer.Append(value);
            this.outputVersion++;
            if (this.buffer.Length > maxChars)
            {
                this.buffer.Remove(0, this.buffer.Length - maxChars);
                this.truncated = true;
            }
        }
    }

    public void Trim(int maxChars)
    {
        lock (this.buffer)
        {
            if (this.buffer.Length > maxChars)
            {
                this.buffer.Remove(0, this.buffer.Length - maxChars);
                this.truncated = true;
                this.outputVersion++;
            }
        }
    }

    private void Set<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
