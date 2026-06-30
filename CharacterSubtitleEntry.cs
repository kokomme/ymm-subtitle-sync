namespace YmmSubtitleSync;

/// <summary>
/// プラグインパネルの「字幕グループ」に表示する、キャラクター1件分のデータ。
/// IsEnabled = true の場合、ボイス終了時に字幕を消す処理を適用する。
/// </summary>
public class CharacterSubtitleEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    string _characterName = "";
    public string CharacterName
    {
        get => _characterName;
        set { _characterName = value; OnPropertyChanged(); }
    }

    bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
