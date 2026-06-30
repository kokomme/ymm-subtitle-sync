#if !YMM4_SDK
namespace YmmSubtitleSync.Stubs
{
    public interface IToolPlugin
    {
        string Name { get; }
        Type ViewModelType { get; }
        Type ViewType { get; }
    }
}
#endif
