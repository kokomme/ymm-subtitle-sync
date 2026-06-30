namespace YmmSubtitleSync;

public class SubtitleSyncViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // ─── プロパティ ────────────────────────────────────────────────────

    public ObservableCollection<CharacterSubtitleEntry> Characters { get; } = [];

    string _resultText = "「キャラ一覧を更新」でキャラクターを読み込んでください。";
    public string ResultText
    {
        get => _resultText;
        set { _resultText = value; OnPropertyChanged(); }
    }

    bool _allEnabled = true;
    public bool AllEnabled
    {
        get => _allEnabled;
        set
        {
            _allEnabled = value;
            foreach (var c in Characters) c.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    // ─── コマンド ──────────────────────────────────────────────────────

    public ICommand RefreshCommand  => new RelayCommand(_ => Refresh());
    public ICommand ApplyCommand    => new RelayCommand(_ => Apply());
    public ICommand ShowLogCommand  => new RelayCommand(_ => ShowLog());
    public ICommand DumpCommand     => new RelayCommand(_ => Dump());

    // ─── 実装 ──────────────────────────────────────────────────────────

    void Refresh()
    {
        ResultText = "キャラクター一覧を取得中...";
        try
        {
            var names = SubtitleSyncCommand.GetCharacterNames();
            if (names.Count == 0)
            {
                ResultText =
                    "キャラクターが見つかりませんでした。\n" +
                    "・YMM4でプロジェクトを保存しましたか？\n" +
                    "・タイムラインにボイスアイテムはありますか？";
                return;
            }

            // 既存の IsEnabled 設定を保持しながらリストを更新
            var prev = Characters.ToDictionary(c => c.CharacterName, c => c.IsEnabled);
            Characters.Clear();
            foreach (var name in names)
                Characters.Add(new CharacterSubtitleEntry
                {
                    CharacterName = name,
                    IsEnabled = prev.TryGetValue(name, out bool was) ? was : true,
                });

            ResultText = $"{names.Count} 件のキャラクターを読み込みました。";
        }
        catch (Exception ex)
        {
            ResultText = $"エラー: {ex.Message}";
        }
    }

    void Apply()
    {
        var enabled = Characters.Where(c => c.IsEnabled).Select(c => c.CharacterName).ToList();

        // キャラ一覧が空の場合は全キャラ対象
        IEnumerable<string>? enabledParam = Characters.Count > 0 ? enabled : null;

        ResultText = "処理中...";
        try
        {
            int count    = SubtitleSyncCommand.Execute(enabledParam);
            bool reloaded = SubtitleSyncCommand.AutoReloaded;

            ResultText = count switch
            {
                < 0 =>
                    "プロジェクトファイルが見つかりませんでした。\n" +
                    "YMM4でプロジェクトを開いて保存してください。",
                0 =>
                    "対象アイテムが見つかりませんでした。\n" +
                    "（表情延長のないアイテムはスキップされます）",
                _ => reloaded
                    ? $"{count} 件の字幕タイミングを調整しました。（自動再読み込み済み）"
                    : $"{count} 件の字幕タイミングを調整しました。\n" +
                      "YMM4でプロジェクトを再読み込みしてください。",
            };
        }
        catch (Exception ex)
        {
            ResultText = $"エラー: {ex.Message}";
        }
    }

    void ShowLog()
    {
        var log = SubtitleSyncCommand.LastDiagLog;
        ResultText = string.IsNullOrEmpty(log)
            ? "（まだ処理が実行されていません）"
            : log;
    }

    void Dump()
    {
        ResultText = "構造解析中...";
        try { ResultText = SubtitleSyncCommand.DumpStructure(); }
        catch (Exception ex) { ResultText = $"エラー: {ex.Message}"; }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add    => System.Windows.Input.CommandManager.RequerySuggested += value;
        remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
    }
    public bool CanExecute(object? p) => canExecute?.Invoke(p) ?? true;
    public void Execute(object? p) => execute(p);
}
