namespace YmmSubtitleSync;

/// <summary>
/// .ymmp を読み込み、有効キャラクターのボイスアイテムに
/// 「ボイス終了で字幕を消す」処理を適用する。
///
/// 確認済みフィールド（ProcessCommand.cs より）:
///   doc["Timelines"][*]["Items"][*]
///   item["Frame"], item["Length"], item["Layer"]
///   item["VoiceLength"]  … TimeSpan 文字列 e.g. "00:00:02.3450000"
///   item["AdditionalTime"] … double（秒）
///
/// 未確定フィールド（実際の .ymmp で要確認）:
///   字幕制御フィールド → ScanSubtitleBoolField() で自動探索
///   TextItem の $type  → ScanTextItemType() で自動探索
/// </summary>
public static class SubtitleSyncCommand
{
    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // 字幕制御フィールド候補（IsSubtitleEnabled が最有力）
    static readonly string[] SubtitleBoolCandidates =
    [
        "IsSubtitleEnabled", "SubtitleEnabled",
        "IsTextVisible",     "TextVisible",
        "ShowSubtitle",      "IsSubtitleVisible",
        "EnableSubtitle",    "SubtitleVisible",
    ];

    // 字幕専用タイムライン識別名
    const string SubtitleTimelineName = "字幕（自動生成）";

    public static string LastDiagLog  { get; private set; } = "";
    public static bool   AutoReloaded { get; private set; } = false;

    // ─── 公開 API ─────────────────────────────────────────────────────

    /// <summary>
    /// .ymmp の構造をダンプして診断用テキストを返す。
    /// 「構造ダンプ」ボタンから呼ぶ。
    /// </summary>
    public static string DumpStructure()
    {
        var ymmpPath = ProjectDetector.GetCurrentProjectPath();
        if (ymmpPath == null) return "プロジェクト未検出";

        var doc = ParseYmmp(ymmpPath);
        if (doc == null) return "JSONパース失敗";

        var sb = new System.Text.StringBuilder();

        var timelines = doc["Timelines"]?.AsArray() ?? [];
        sb.AppendLine($"=== Timelines: {timelines.Count} 件 ===");

        foreach (var tl in timelines)
        {
            if (tl is not JsonObject tlObj) continue;
            sb.AppendLine($"\n[Timeline] {tl["Name"]}");

            // VideoInfo から FPS を確認
            if (tlObj["VideoInfo"] is JsonObject vi)
            {
                sb.AppendLine($"  VideoInfo keys: {string.Join(", ", vi.Select(kv => kv.Key))}");
                foreach (var kv in vi)
                {
                    var val = kv.Value?.ToJsonString() ?? "null";
                    if (val.Length > 80) val = val[..80] + "...";
                    sb.AppendLine($"    {kv.Key}: {val}");
                }
            }

            var items = tl["Items"]?.AsArray() ?? [];
            sb.AppendLine($"  Items: {items.Count} 件");

            // $type の種類一覧
            var types = items
                .OfType<JsonObject>()
                .Select(o => o["$type"]?.GetValue<string>() ?? "(no type)")
                .GroupBy(t => t)
                .Select(g => $"{g.Key.Split(',')[0].Split('.').Last()} x{g.Count()}");
            sb.AppendLine($"  Types: {string.Join(", ", types)}");

            // VoiceItem を探して最初の1件を表示
            var voice = items
                .OfType<JsonObject>()
                .FirstOrDefault(o =>
                {
                    var t = o["$type"]?.GetValue<string>() ?? "";
                    return t.Contains("Voice") || o.ContainsKey("VoiceLength");
                });

            if (voice != null)
            {
                sb.AppendLine("\n  --- 最初の VoiceItem ---");
                DumpObject(voice, sb, "  ");
            }
            else
            {
                sb.AppendLine("  ※ VoiceItem が見つかりません");
            }

            // TextItem も表示
            var text = items
                .OfType<JsonObject>()
                .FirstOrDefault(o =>
                {
                    var t = o["$type"]?.GetValue<string>() ?? "";
                    return t.Contains("Text") && !t.Contains("Voice");
                });
            if (text != null)
            {
                sb.AppendLine("\n  --- 最初の TextItem ---");
                DumpObject(text, sb, "  ");
            }
        }

        var dumpPath = ymmpPath + ".structure.txt";
        try { File.WriteAllText(dumpPath, sb.ToString()); }
        catch { }

        LastDiagLog = sb.ToString();
        return $"ダンプ完了。ファイルも保存しました:\n{dumpPath}\n\n" + sb.ToString();
    }

    static void DumpObject(JsonObject obj, System.Text.StringBuilder sb, string indent)
    {
        foreach (var kv in obj)
        {
            var val = kv.Value?.ToJsonString() ?? "null";
            if (val.Length > 120) val = val[..120] + "...";
            sb.AppendLine($"{indent}  {kv.Key}: {val}");
        }
    }

    /// <summary>
    /// 現在のプロジェクトからキャラクター名一覧を返す。
    /// UI の「キャラ一覧を更新」ボタンから呼ぶ。
    /// </summary>
    public static IReadOnlyList<string> GetCharacterNames()
    {
        var ymmpPath = ProjectDetector.GetCurrentProjectPath();
        if (ymmpPath == null) return [];

        var doc = ParseYmmp(ymmpPath);
        if (doc == null) return [];

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in EnumerateItems(doc))
        {
            var name = ExtractCharacterName(item);
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }
        return [.. names.OrderBy(n => n)];
    }

    /// <summary>
    /// 字幕タイミング調整を実行する。
    /// enabledCharacters = null のときは全キャラクター対象。
    /// </summary>
    public static int Execute(IEnumerable<string>? enabledCharacters)
    {
        var log = new System.Text.StringBuilder();

        var ymmpPath = ProjectDetector.GetCurrentProjectPath();
        if (ymmpPath == null) { LastDiagLog = "プロジェクト未検出"; return -1; }
        log.AppendLine($"プロジェクト: {Path.GetFileName(ymmpPath)}");

        var doc = ParseYmmp(ymmpPath);
        if (doc == null) { LastDiagLog = "JSONパース失敗"; return -1; }

        var timelines = doc["Timelines"]?.AsArray();
        if (timelines == null) { LastDiagLog = "Timelinesキーなし"; return 0; }

        double fps = DetectInternalFps(timelines, out string fpsDiag);
        log.AppendLine($"内部FPS: {fps} ({fpsDiag})");

        // 字幕制御フィールドと TextItem $type を探索
        var subtitleField = ScanSubtitleBoolField(timelines);
        var textItemType  = ScanTextItemType(timelines);
        log.AppendLine($"字幕制御フィールド: {subtitleField ?? "(未検出)"}");
        log.AppendLine($"TextItem $type: {textItemType ?? "(未検出・推定値使用)"}");

        var enabledSet = enabledCharacters != null
            ? new HashSet<string>(enabledCharacters, StringComparer.Ordinal)
            : null;  // null = 全キャラクター対象

        var subtitleTimeline = GetOrCreateSubtitleTimeline(doc, textItemType);
        int patchCount = 0;

        foreach (var (item, tl) in EnumerateItemsWithTimeline(doc))
        {
            // 有効キャラクターフィルター
            var charName = ExtractCharacterName(item);
            if (enabledSet != null && !enabledSet.Contains(charName ?? ""))
                continue;

            // 音声実尺フレーム数を計算
            int audioFrames = ComputeAudioFrames(item, fps, out string lenDiag);
            int totalLength = item["Length"]?.GetValue<int>() ?? 0;
            if (totalLength <= audioFrames)
                continue;  // 延長なし → スキップ

            int deltaFrames = totalLength - audioFrames;
            int frame = item["Frame"]?.GetValue<int>() ?? 0;
            string serif = ExtractSerif(item);

            log.AppendLine(
                $"  [{charName}] Frame={frame} Length={totalLength}fr " +
                $"音声={audioFrames}fr 差分=+{deltaFrames}fr");

            // ① 内蔵字幕を無効化
            DisableBuiltinSubtitle(item, subtitleField);

            // ② TextItem を字幕タイムラインに追加
            var textItem = BuildTextItem(frame, audioFrames, item, textItemType, serif);
            subtitleTimeline["Items"]!.AsArray().Add(textItem);

            patchCount++;
        }

        if (patchCount == 0)
        {
            LastDiagLog = log.AppendLine("対象アイテムなし").ToString().TrimEnd();
            return 0;
        }

        // バックアップ → 上書き保存
        File.Copy(ymmpPath, ymmpPath + ".bak", overwrite: true);
        File.WriteAllText(ymmpPath, doc.ToJsonString(WriteOptions));
        log.AppendLine($"{patchCount} 件を処理しました。");

        LastDiagLog = log.ToString().TrimEnd();

        // 自動再読み込み
        AutoReloaded = false;
        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
                dispatcher.Invoke(() => { AutoReloaded = ProjectDetector.TryReloadProject(ymmpPath); });
        }
        catch { }

        return patchCount;
    }

    // ─── 内部ヘルパー ─────────────────────────────────────────────────

    static JsonNode? ParseYmmp(string path)
    {
        try { return JsonNode.Parse(File.ReadAllText(path)); }
        catch { return null; }
    }

    static IEnumerable<JsonObject> EnumerateItems(JsonNode doc)
    {
        foreach (var tl in doc["Timelines"]?.AsArray() ?? [])
            foreach (var item in tl?["Items"]?.AsArray() ?? [])
                if (item is JsonObject obj) yield return obj;
    }

    static IEnumerable<(JsonObject Item, JsonObject Timeline)> EnumerateItemsWithTimeline(JsonNode doc)
    {
        foreach (var tl in doc["Timelines"]?.AsArray() ?? [])
        {
            if (tl is not JsonObject tlObj) continue;
            foreach (var item in tl["Items"]?.AsArray() ?? [])
                if (item is JsonObject itemObj && IsVoiceItem(itemObj))
                    yield return (itemObj, tlObj);
        }
    }

    static bool IsVoiceItem(JsonObject obj)
    {
        var type = obj["$type"]?.GetValue<string>() ?? "";
        if (type.Contains("VoiceItem")) return true;
        return obj.ContainsKey("VoiceLength");
    }

    static string? ExtractCharacterName(JsonObject item)
    {
        foreach (var key in new[] { "CharacterName", "Name" })
            if (item[key]?.GetValue<string>() is string s && !string.IsNullOrEmpty(s))
                return s;

        // ネスト 1 段探索
        foreach (var kv in item)
        {
            if (kv.Value is JsonObject nested)
                foreach (var key in new[] { "CharacterName", "Name" })
                    if (nested[key]?.GetValue<string>() is string s && !string.IsNullOrEmpty(s))
                        return s;
        }
        return null;
    }

    static string ExtractSerif(JsonObject item)
    {
        foreach (var key in new[] { "Serif", "Text", "SubtitleText" })
            if (item[key]?.GetValue<string>() is string s)
                return s;
        return "";
    }

    // VoiceLength をトップレベル + 1階層ネストまで探す
    static string? FindVoiceLength(JsonObject item)
    {
        if (item["VoiceLength"]?.GetValue<string>() is string s0) return s0;
        foreach (var kv in item)
            if (kv.Value is JsonObject sub && sub["VoiceLength"]?.GetValue<string>() is string s1)
                return s1;
        return null;
    }

    static int ComputeAudioFrames(JsonObject item, double fps, out string diag)
    {
        var vlStr = FindVoiceLength(item);
        if (vlStr != null && TimeSpan.TryParse(vlStr, out var vl) && vl.TotalSeconds > 0)
        {
            // AdditionalTime が負 = 音声末尾をカット、正 = 末尾に余白追加
            // 実際に再生される秒数 = VoiceLength + AdditionalTime
            double addTime = item["AdditionalTime"]?.GetValue<double>() ?? 0.0;
            double effectiveSec = Math.Max(0.01, vl.TotalSeconds + addTime);
            int frames = Math.Max(1, (int)Math.Ceiling(effectiveSec * fps));
            diag = $"voice={vl.TotalSeconds:F3}s add={addTime:F3}s eff={effectiveSec:F3}s → {frames}fr";
            return frames;
        }
        int origLen = item["Length"]?.GetValue<int>() ?? 1;
        diag = vlStr == null
            ? $"VoiceLengthフィールドなし → {origLen}fr"
            : $"VoiceLength解析失敗('{vlStr}') → {origLen}fr";
        return origLen;
    }

    static double DetectInternalFps(JsonArray timelines, out string diag)
    {
        foreach (var tl in timelines)
        {
            // 優先1: Timeline.VideoInfo.FPS
            if (tl?["VideoInfo"] is JsonObject vi)
            {
                foreach (var key in new[] { "FPS", "Fps", "FrameRate", "fps" })
                    if (vi[key]?.GetValue<double>() is double viFps && viFps > 0)
                    { diag = $"VideoInfo.{key}={viFps}"; return viFps; }
            }
            // 優先2: Timeline.FPS
            if (tl?["FPS"]?.GetValue<double>() is double tFps && tFps > 0)
            { diag = $"Timeline.FPS={tFps}"; return tFps; }
        }
        diag = "検出失敗 → 30fps";
        return 30.0;
    }

    static string? ScanSubtitleBoolField(JsonArray timelines)
    {
        foreach (var item in timelines.SelectMany(tl => tl?["Items"]?.AsArray() ?? []))
        {
            if (item is not JsonObject obj || !IsVoiceItem(obj)) continue;
            // YMM4 実際のフィールド: JimakuVisibility (string enum)
            if (obj.ContainsKey("JimakuVisibility")) return "JimakuVisibility";
            // フォールバック: bool フィールド候補
            foreach (var candidate in SubtitleBoolCandidates)
                if (obj[candidate] is JsonValue v && v.TryGetValue<bool>(out _))
                    return candidate;
        }
        return null;
    }

    static string? ScanTextItemType(JsonArray timelines)
    {
        foreach (var item in timelines.SelectMany(tl => tl?["Items"]?.AsArray() ?? []))
        {
            if (item is not JsonObject obj) continue;
            var type = obj["$type"]?.GetValue<string>() ?? "";
            if ((type.Contains("TextItem") || type.Contains("Telop")) && !type.Contains("VoiceItem"))
                return type;
        }
        return null;
    }

    static void DisableBuiltinSubtitle(JsonObject item, string? subtitleField)
    {
        if (subtitleField == "JimakuVisibility")
        {
            item["JimakuVisibility"] = JsonValue.Create("Hidden");
            return;
        }
        if (subtitleField != null && item.ContainsKey(subtitleField))
        {
            item[subtitleField] = JsonValue.Create(false);
            return;
        }
        // フォールバック: JimakuVisibility が存在すれば Hidden に
        if (item.ContainsKey("JimakuVisibility"))
            item["JimakuVisibility"] = JsonValue.Create("Hidden");
        else
            foreach (var candidate in SubtitleBoolCandidates)
                if (item.ContainsKey(candidate))
                    item[candidate] = JsonValue.Create(false);
    }

    static JsonObject BuildTextItem(
        int frame, int length, JsonObject srcVoice,
        string? textItemType, string serif)
    {
        var type = textItemType
            ?? "YukkuriMovieMaker.Project.Items.TextItem, YukkuriMovieMaker";

        var obj = new JsonObject
        {
            ["$type"]    = JsonValue.Create(type),
            ["Frame"]    = JsonValue.Create(frame),
            ["Length"]   = JsonValue.Create(length),
            ["Layer"]    = JsonValue.Create(srcVoice["Layer"]?.GetValue<int>() ?? 0),
            ["IsEnabled"] = JsonValue.Create(true),
            // セリフ（フィールド名は TextItem の実際の構造に合わせて変わる可能性あり）
            ["Text"]     = JsonValue.Create(serif),
            ["Serif"]    = JsonValue.Create(serif),
        };

        // キャラクター名をコピー（あれば）
        var charName = ExtractCharacterName(srcVoice);
        if (charName != null)
            obj["CharacterName"] = JsonValue.Create(charName);

        return obj;
    }

    static JsonObject GetOrCreateSubtitleTimeline(JsonNode doc, string? textItemType)
    {
        var timelines = doc["Timelines"]!.AsArray();
        foreach (var tl in timelines)
            if (tl is JsonObject tlObj &&
                tlObj["Name"]?.GetValue<string>() == SubtitleTimelineName)
                return tlObj;

        var newTl = new JsonObject
        {
            ["Name"]  = JsonValue.Create(SubtitleTimelineName),
            ["Items"] = new JsonArray(),
        };
        timelines.Add(newTl);
        return newTl;
    }
}
