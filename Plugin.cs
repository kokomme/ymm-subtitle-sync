#if YMM4_SDK
using YukkuriMovieMaker.Plugin;
#endif

namespace YmmSubtitleSync;

public class SubtitleSyncPlugin : IToolPlugin
{
    public string Name => "字幕タイミング調整";
    public Type ViewModelType => typeof(SubtitleSyncViewModel);
    public Type ViewType => typeof(SettingsPanel);
}
