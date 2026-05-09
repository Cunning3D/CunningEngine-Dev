namespace CunningEngine {
    public interface ICunningInputHandle { ulong CurrentHandle { get; } }
    public interface ICunningInputValueHandle {
        ulong CurrentInputDirtyId { get; }
        ulong ImportCunningValue(CunningRuntimeContext runtime);
    }
}

